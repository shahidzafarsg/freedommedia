using System.Text;

namespace FreedomMedia.Core;

/// <summary>
/// A vault opened with the correct passphrase: holds the (in-memory only) master key and the
/// decrypted catalogue. The passphrase and master key are never written to disk. Dispose()
/// zeroes the master key.
///
/// Opening is read-only and has no attempt limit: a wrong passphrase simply throws
/// WrongPasswordException and leaves the file untouched, so the caller may prompt again.
/// </summary>
public sealed class VaultSession : IDisposable
{
    public string VaultPath { get; }
    public IReadOnlyList<VaultEntry> Entries { get; }

    private readonly byte[] _masterKey;
    private bool _disposed;

    private VaultSession(string vaultPath, byte[] masterKey, List<VaultEntry> entries)
    {
        VaultPath = vaultPath;
        _masterKey = masterKey;
        Entries = entries;
    }

    /// <summary>Opens a vault file and unlocks it with the given passphrase. Throws
    /// WrongPasswordException if the passphrase is incorrect, or VaultCorruptException if the
    /// file is not a valid vault.</summary>
    public static VaultSession Open(string vaultPath, string password)
    {
        ValidatePassword(password);
        var passwordUtf8 = Encoding.UTF8.GetBytes(password);
        try
        {
            using var fs = new FileStream(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var parsed = VaultFileIO.ReadMagicAndHeader(fs);
            var header = parsed.Header;

            byte[]? masterKey = null;
            bool passwordOk;
            try
            {
                var salt = Convert.FromBase64String(header.Kdf.SaltBase64);
                var kek = CryptoUtil.DeriveKek(passwordUtf8, salt, header.Kdf.MemoryKiB, header.Kdf.Iterations, header.Kdf.Parallelism);
                var wrapNonce = Convert.FromBase64String(header.MasterKeyWrap.NonceBase64);
                var wrapCipher = Convert.FromBase64String(header.MasterKeyWrap.CiphertextBase64);
                try
                {
                    masterKey = CryptoUtil.AesGcmDecrypt(kek, wrapNonce, wrapCipher);
                }
                finally
                {
                    Array.Clear(kek);
                }

                // Verify before trusting the key further (also catches bit-flip false negatives cheaply).
                var verNonce = Convert.FromBase64String(header.Verifier.NonceBase64);
                var verCipher = Convert.FromBase64String(header.Verifier.CiphertextBase64);
                var verPlain = CryptoUtil.AesGcmDecrypt(masterKey, verNonce, verCipher);
                passwordOk = Encoding.UTF8.GetString(verPlain) == VaultFormat.VerifierPlaintext;
            }
            catch (WrongPasswordException)
            {
                passwordOk = false;
            }

            if (!passwordOk)
            {
                if (masterKey != null) Array.Clear(masterKey);
                throw new WrongPasswordException();
            }

            var index = VaultFileIO.ReadAndDecryptIndex(fs, parsed.IndexBlockLenOffset, masterKey!, out _);
            return new VaultSession(vaultPath, masterKey!, index.Entries);
        }
        finally
        {
            Array.Clear(passwordUtf8);
        }
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Passphrase cannot be empty.");
        if (Encoding.UTF8.GetByteCount(password) > VaultFormat.MaxPasswordLength)
            throw new ArgumentException($"Passphrase must be at most {VaultFormat.MaxPasswordLength} characters.");
    }

    /// <summary>Returns a copy of the master key, for the rebuild-on-edit flow (add, remove or
    /// rename an item, or change the passphrase, without re-encrypting the media that stays).
    /// Callers must Array.Clear() it when done.</summary>
    public byte[] ExportMasterKeyForRebuild()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VaultSession));
        return (byte[])_masterKey.Clone();
    }

    /// <summary>Opens a seekable, decrypting stream for one catalogue entry. The caller must
    /// Dispose it. Nothing is written to disk: bytes are decrypted a chunk at a time in memory.</summary>
    public VaultEntryStream OpenEntryStream(VaultEntry entry)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VaultSession));
        if (!Entries.Contains(entry)) throw new ArgumentException("Entry does not belong to this vault session.");
        var fileKey = CryptoUtil.HkdfDeriveFileKey(_masterKey, Convert.FromBase64String(entry.FileSaltBase64));
        var baseNonce = Convert.FromBase64String(entry.BaseNonceBase64);
        var fs = new FileStream(VaultPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: false);
        return new VaultEntryStream(fs, entry, fileKey, baseNonce);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Array.Clear(_masterKey);
    }
}
