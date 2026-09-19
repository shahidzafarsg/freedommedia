using System.Security.Cryptography;
using System.Text;

namespace FreedomMedia.Core;

public sealed record VaultBuildProgress(string CurrentItem, long BytesDone, long BytesTotal);

/// <summary>An item to include in the vault being (re)built.</summary>
public abstract class PendingItem
{
    public required string Title { get; init; }
}

/// <summary>A plaintext photo or video on disk to encrypt fresh into the vault (import).</summary>
public sealed class NewFileItem : PendingItem
{
    public required string SourceFilePath { get; init; }
}

/// <summary>An entry already present in a vault being edited (add, remove or rename an item, or
/// change the passphrase) — its encrypted bytes are copied across as-is, never re-encrypted.</summary>
public sealed class KeepExistingItem : PendingItem
{
    public required VaultEntry Entry { get; init; }
    public required VaultSession SourceSession { get; init; }
}

/// <summary>
/// Builds (or rebuilds) a FreedomMedia vault. Creating a brand-new vault and editing an existing
/// one are the same operation: write the full set of desired entries to a fresh temporary file,
/// then atomically replace the target path. This keeps the vault on disk consistent even if the
/// process is interrupted part way through.
/// </summary>
public static class VaultWriter
{
    public static void Build(
        string finalOutputPath,
        string password,
        byte[]? existingMasterKey,
        IReadOnlyList<PendingItem> items,
        IProgress<VaultBuildProgress>? progress,
        CancellationToken ct)
    {
        VaultSession.ValidatePassword(password);
        var passwordUtf8 = Encoding.UTF8.GetBytes(password);
        byte[] masterKey = existingMasterKey ?? CryptoUtil.RandomBytes(VaultFormat.MasterKeyLength);
        string tempOutputPath = finalOutputPath + ".tmp" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            var kdfSalt = CryptoUtil.RandomBytes(VaultFormat.ArgonSaltLength);
            var kek = CryptoUtil.DeriveKek(passwordUtf8, kdfSalt, VaultFormat.ArgonMemoryKiB, VaultFormat.ArgonIterations, VaultFormat.ArgonParallelism);
            var (wrapNonce, wrapCipher) = CryptoUtil.AesGcmEncrypt(kek, masterKey);
            var (verNonce, verCipher) = CryptoUtil.AesGcmEncrypt(masterKey, Encoding.UTF8.GetBytes(VaultFormat.VerifierPlaintext));
            Array.Clear(kek);

            var header = new VaultHeader
            {
                Version = VaultFormat.CurrentVersion,
                Kdf = new KdfParams
                {
                    Algo = "argon2id",
                    MemoryKiB = VaultFormat.ArgonMemoryKiB,
                    Iterations = VaultFormat.ArgonIterations,
                    Parallelism = VaultFormat.ArgonParallelism,
                    SaltBase64 = Convert.ToBase64String(kdfSalt),
                },
                MasterKeyWrap = new WrappedSecret { NonceBase64 = Convert.ToBase64String(wrapNonce), CiphertextBase64 = Convert.ToBase64String(wrapCipher) },
                Verifier = new WrappedSecret { NonceBase64 = Convert.ToBase64String(verNonce), CiphertextBase64 = Convert.ToBase64String(verCipher) },
            };

            // Pass 1: build entry metadata (sizes/keys/nonces are known without touching file content yet).
            var entries = new List<VaultEntry>(items.Count);
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                entries.Add(item switch
                {
                    KeepExistingItem keep => CloneWithTitle(keep.Entry, keep.Title),
                    NewFileItem nv => BuildFreshEntry(nv),
                    _ => throw new InvalidOperationException("Unknown pending item type."),
                });
            }

            long headerOnlyPrefixLen = ComputeHeaderPrefixLength(header);

            // Fixed-point: dataOffset values live in the index JSON, so the index's encrypted length
            // (and hence where chunk data starts) depends on the offsets, which depend on where chunk
            // data starts. This converges in one or two iterations in practice.
            long chunkDataStart = headerOnlyPrefixLen;
            var index = new VaultIndex { Entries = entries };
            for (int iter = 0; iter < 8; iter++)
            {
                AssignDataOffsets(entries, chunkDataStart);
                long computedIndexLen = VaultFileIO.ComputeIndexBlockLength(index);
                long candidate = headerOnlyPrefixLen + 4 /* indexBlockLen field */ + computedIndexLen;
                if (candidate == chunkDataStart) break;
                chunkDataStart = candidate;
            }
            AssignDataOffsets(entries, chunkDataStart);

            long totalBytes = entries.Sum(e => e.EncryptedDataLength());

            using (var outFs = new FileStream(tempOutputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                long actualChunkStart = VaultFileIO.WriteMagicHeaderAndIndex(outFs, header, index, masterKey);
                if (actualChunkStart != chunkDataStart)
                    throw new InvalidOperationException("Internal error: vault layout offset mismatch.");

                long bytesDone = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = items[i];
                    var entry = entries[i];
                    long doneBeforeThisItem = bytesDone;
                    progress?.Report(new VaultBuildProgress(item.Title, bytesDone, totalBytes));

                    if (item is KeepExistingItem keep)
                        CopyRawEncrypted(keep.SourceSession, keep.Entry, outFs, entry.DataOffset, ct);
                    else if (item is NewFileItem nv)
                        EncryptNewFile(nv.SourceFilePath, entry, masterKey, outFs, entry.DataOffset,
                            delta => progress?.Report(new VaultBuildProgress(item.Title, doneBeforeThisItem + delta, totalBytes)), ct);

                    bytesDone += entry.EncryptedDataLength();
                }
                outFs.Flush(true);
            }

            if (File.Exists(finalOutputPath)) File.Delete(finalOutputPath);
            File.Move(tempOutputPath, finalOutputPath);
        }
        finally
        {
            Array.Clear(passwordUtf8);
            if (existingMasterKey == null) Array.Clear(masterKey);
            if (File.Exists(tempOutputPath)) { try { File.Delete(tempOutputPath); } catch { /* best effort */ } }
        }
    }

    private static VaultEntry CloneWithTitle(VaultEntry src, string title) => new()
    {
        Id = src.Id,
        Title = title,
        MediaKind = src.MediaKind,
        OriginalFileName = src.OriginalFileName,
        OriginalSize = src.OriginalSize,
        ChunkPlainSize = src.ChunkPlainSize,
        ChunkCount = src.ChunkCount,
        LastChunkPlainSize = src.LastChunkPlainSize,
        FileSaltBase64 = src.FileSaltBase64,
        BaseNonceBase64 = src.BaseNonceBase64,
        Sha256Base64 = src.Sha256Base64,
        AddedAt = src.AddedAt,
        Width = src.Width,
        Height = src.Height,
        DurationMs = src.DurationMs,
    };

    private static VaultEntry BuildFreshEntry(NewFileItem nv)
    {
        var fi = new FileInfo(nv.SourceFilePath);
        if (!fi.Exists) throw new FileNotFoundException("Source media not found.", nv.SourceFilePath);
        long size = fi.Length;
        long chunkCount = Math.Max(1, (size + VaultFormat.ChunkPlainSize - 1) / VaultFormat.ChunkPlainSize);
        int lastChunkPlainSize = size == 0 ? 0 : (int)(size - (chunkCount - 1) * VaultFormat.ChunkPlainSize);
        var info = MediaProbe.Probe(nv.SourceFilePath);

        return new VaultEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = nv.Title,
            MediaKind = info.Kind,
            OriginalFileName = fi.Name,
            OriginalSize = size,
            ChunkPlainSize = VaultFormat.ChunkPlainSize,
            ChunkCount = chunkCount,
            LastChunkPlainSize = lastChunkPlainSize,
            FileSaltBase64 = Convert.ToBase64String(CryptoUtil.RandomBytes(16)),
            BaseNonceBase64 = Convert.ToBase64String(CryptoUtil.RandomBytes(VaultFormat.GcmNonceLength)),
            Sha256Base64 = "", // filled in during the encryption pass
            AddedAt = DateTime.UtcNow.ToString("o"),
            Width = info.Width,
            Height = info.Height,
            DurationMs = info.DurationMs,
        };
    }

    private static void AssignDataOffsets(List<VaultEntry> entries, long chunkDataStart)
    {
        long offset = chunkDataStart;
        foreach (var e in entries)
        {
            e.DataOffset = offset;
            offset += e.EncryptedDataLength();
        }
    }

    private static long ComputeHeaderPrefixLength(VaultHeader header)
    {
        var headerBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(header);
        return VaultFormat.MagicLength + 4 + headerBytes.Length;
    }

    private static void CopyRawEncrypted(VaultSession sourceSession, VaultEntry sourceEntry, FileStream outFs, long destOffset, CancellationToken ct)
    {
        using var srcFs = new FileStream(sourceSession.VaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        srcFs.Position = sourceEntry.DataOffset;
        outFs.Position = destOffset;
        long remaining = sourceEntry.EncryptedDataLength();
        var buffer = new byte[1024 * 1024 + VaultFormat.GcmTagLength];
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int toRead = (int)Math.Min(buffer.Length, remaining);
            ReadExact(srcFs, buffer, toRead);
            outFs.Write(buffer, 0, toRead);
            remaining -= toRead;
        }
    }

    private static void ReadExact(Stream s, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buffer, read, count - read);
            if (n == 0) throw new VaultCorruptException("Unexpected end of source vault file while copying.");
            read += n;
        }
    }

    private static void EncryptNewFile(string sourcePath, VaultEntry entry, byte[] masterKey, FileStream outFs, long destOffset, Action<long> onBytesWritten, CancellationToken ct)
    {
        var fileKey = CryptoUtil.HkdfDeriveFileKey(masterKey, Convert.FromBase64String(entry.FileSaltBase64));
        var baseNonce = Convert.FromBase64String(entry.BaseNonceBase64);
        outFs.Position = destOffset;

        using var srcFs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();
        var plainBuf = new byte[VaultFormat.ChunkPlainSize];
        var cipherBuf = new byte[VaultFormat.ChunkPlainSize];
        var tagBuf = new byte[VaultFormat.GcmTagLength];

        long cumulativeWritten = 0;
        for (long chunkIndex = 0; chunkIndex < entry.ChunkCount; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();
            bool isLast = chunkIndex == entry.ChunkCount - 1;
            int plainLen = isLast ? entry.LastChunkPlainSize : entry.ChunkPlainSize;
            ReadExact(srcFs, plainBuf, plainLen);
            sha256.TransformBlock(plainBuf, 0, plainLen, null, 0);

            var nonce = CryptoUtil.DeriveChunkNonce(baseNonce, chunkIndex);
            CryptoUtil.AesGcmEncryptInto(fileKey, nonce, plainBuf.AsSpan(0, plainLen), cipherBuf.AsSpan(0, plainLen), tagBuf);
            outFs.Write(cipherBuf, 0, plainLen);
            outFs.Write(tagBuf, 0, VaultFormat.GcmTagLength);
            cumulativeWritten += plainLen + VaultFormat.GcmTagLength;
            onBytesWritten(cumulativeWritten);
        }
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        entry.Sha256Base64 = Convert.ToBase64String(sha256.Hash!);
        Array.Clear(fileKey);
    }
}
