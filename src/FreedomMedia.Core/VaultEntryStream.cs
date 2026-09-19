namespace FreedomMedia.Core;

/// <summary>
/// A read-only, seekable Stream that presents the decrypted plaintext of one vault entry,
/// decrypting 1 MiB chunks on demand. This is what the built-in photo viewer and media player
/// read from, so a photograph or a video is decrypted only into memory, for the moment it is
/// shown, and no readable copy is ever written to a temporary folder.
/// </summary>
public sealed class VaultEntryStream : Stream
{
    private readonly FileStream _vaultFile;
    private readonly VaultEntry _entry;
    private readonly byte[] _fileKey;
    private readonly byte[] _baseNonce;
    private readonly object _lock = new();

    private long _position;
    private long _cachedChunkIndex = -1;
    private byte[] _cachedChunkPlain = Array.Empty<byte>();

    internal VaultEntryStream(FileStream vaultFile, VaultEntry entry, byte[] fileKey, byte[] baseNonce)
    {
        _vaultFile = vaultFile;
        _entry = entry;
        _fileKey = fileKey;
        _baseNonce = baseNonce;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _entry.OriginalSize;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > Length) throw new IOException("Seek out of range.");
        _position = target;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_position >= Length) return 0;
            int totalCopied = 0;
            while (count > 0 && _position < Length)
            {
                long chunkIndex = _position / _entry.ChunkPlainSize;
                var chunkPlain = GetChunkPlain(chunkIndex);
                int offsetInChunk = (int)(_position % _entry.ChunkPlainSize);
                int available = chunkPlain.Length - offsetInChunk;
                if (available <= 0) break;
                int toCopy = Math.Min(count, available);
                toCopy = (int)Math.Min(toCopy, Length - _position);
                Buffer.BlockCopy(chunkPlain, offsetInChunk, buffer, offset, toCopy);
                offset += toCopy;
                count -= toCopy;
                _position += toCopy;
                totalCopied += toCopy;
            }
            return totalCopied;
        }
    }

    private byte[] GetChunkPlain(long chunkIndex)
    {
        if (chunkIndex == _cachedChunkIndex) return _cachedChunkPlain;

        bool isLast = chunkIndex == _entry.ChunkCount - 1;
        int plainLen = isLast ? _entry.LastChunkPlainSize : _entry.ChunkPlainSize;
        int encLen = plainLen + VaultFormat.GcmTagLength;
        long fileOffset = _entry.DataOffset + chunkIndex * (long)(_entry.ChunkPlainSize + VaultFormat.GcmTagLength);

        var encrypted = new byte[encLen];
        _vaultFile.Position = fileOffset;
        VaultFileIO.ReadExact(_vaultFile, encrypted);

        var nonce = CryptoUtil.DeriveChunkNonce(_baseNonce, chunkIndex);
        var plain = new byte[plainLen];
        CryptoUtil.AesGcmDecryptInto(_fileKey, nonce, encrypted.AsSpan(0, plainLen), encrypted.AsSpan(plainLen, VaultFormat.GcmTagLength), plain);

        _cachedChunkIndex = chunkIndex;
        _cachedChunkPlain = plain;
        return plain;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _vaultFile.Dispose();
            Array.Clear(_fileKey);
        }
        base.Dispose(disposing);
    }
}
