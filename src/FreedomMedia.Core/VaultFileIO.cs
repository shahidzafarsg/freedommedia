using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace FreedomMedia.Core;

/// <summary>
/// Low-level (magic + header + encrypted index) reading/writing shared by VaultWriter (which
/// creates and rebuilds vaults) and VaultSession (which opens them). Keeping it in one place
/// stops the reader and writer layouts from drifting apart.
/// </summary>
internal static class VaultFileIO
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public readonly struct ParsedHeader
    {
        public required VaultHeader Header { get; init; }
        /// <summary>File offset of the 4-byte indexBlockLen field.</summary>
        public required long IndexBlockLenOffset { get; init; }
    }

    public static ParsedHeader ReadMagicAndHeader(Stream fs)
    {
        fs.Position = 0;
        var magic = new byte[VaultFormat.MagicLength];
        ReadExact(fs, magic);
        if (Encoding.ASCII.GetString(magic) != VaultFormat.Magic)
            throw new VaultCorruptException("Not a FreedomMedia vault (bad magic).");

        var lenBuf = new byte[4];
        ReadExact(fs, lenBuf);
        int headerLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (headerLen <= 0 || headerLen > 1_000_000)
            throw new VaultCorruptException("Vault header length out of range.");

        var headerBytes = new byte[headerLen];
        ReadExact(fs, headerBytes);
        var header = JsonSerializer.Deserialize<VaultHeader>(headerBytes)
            ?? throw new VaultCorruptException("Vault header JSON invalid.");
        if (header.Version != VaultFormat.CurrentVersion)
            throw new VaultCorruptException($"Unsupported vault version {header.Version}.");

        long indexBlockLenOffset = fs.Position;
        return new ParsedHeader { Header = header, IndexBlockLenOffset = indexBlockLenOffset };
    }

    /// <summary>Decrypts the index block located right after the header. Returns the index and, via
    /// the out parameter, the absolute offset where chunk data begins.</summary>
    public static VaultIndex ReadAndDecryptIndex(Stream fs, long indexBlockLenOffset, byte[] masterKey, out long chunkDataStartOffset)
    {
        fs.Position = indexBlockLenOffset;
        var lenBuf = new byte[4];
        ReadExact(fs, lenBuf);
        int indexBlockLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (indexBlockLen <= VaultFormat.GcmNonceLength + VaultFormat.GcmTagLength || indexBlockLen > 100_000_000)
            throw new VaultCorruptException("Vault index length out of range.");

        var block = new byte[indexBlockLen];
        ReadExact(fs, block);
        chunkDataStartOffset = fs.Position;

        var nonce = block.AsSpan(0, VaultFormat.GcmNonceLength).ToArray();
        var cipherWithTag = block.AsSpan(VaultFormat.GcmNonceLength).ToArray();
        var plain = CryptoUtil.AesGcmDecrypt(masterKey, nonce, cipherWithTag);
        var index = JsonSerializer.Deserialize<VaultIndex>(plain)
            ?? throw new VaultCorruptException("Vault index JSON invalid.");
        return index;
    }

    /// <summary>Writes magic + header + encrypted index to a fresh stream (positioned at 0).
    /// Returns the absolute offset where chunk data should be written next.</summary>
    public static long WriteMagicHeaderAndIndex(Stream fs, VaultHeader header, VaultIndex index, byte[] masterKey)
    {
        fs.Position = 0;
        fs.Write(Encoding.ASCII.GetBytes(VaultFormat.Magic));

        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        var headerLenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(headerLenBuf, headerBytes.Length);
        fs.Write(headerLenBuf);
        fs.Write(headerBytes);

        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions);
        var (nonce, cipherWithTag) = CryptoUtil.AesGcmEncrypt(masterKey, indexBytes);
        int indexBlockLen = VaultFormat.GcmNonceLength + cipherWithTag.Length;
        var indexLenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(indexLenBuf, indexBlockLen);
        fs.Write(indexLenBuf);
        fs.Write(nonce);
        fs.Write(cipherWithTag);

        return fs.Position;
    }

    /// <summary>Computes the encrypted index block length in advance (without writing), so the
    /// writer can work out chunk data offsets before it commits the final file layout.</summary>
    public static long ComputeIndexBlockLength(VaultIndex index)
    {
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions);
        return VaultFormat.GcmNonceLength + indexBytes.Length + VaultFormat.GcmTagLength;
    }

    public static void ReadExact(Stream fs, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = fs.Read(buffer, read, buffer.Length - read);
            if (n == 0) throw new VaultCorruptException("Unexpected end of vault file.");
            read += n;
        }
    }
}
