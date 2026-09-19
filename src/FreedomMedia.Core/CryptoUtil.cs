using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace FreedomMedia.Core;

/// <summary>Thrown when the passphrase given to open a vault is incorrect. There is no attempt
/// limit and no penalty: the caller may simply prompt again.</summary>
public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException() : base("Incorrect passphrase.") { }
}

/// <summary>Thrown when a file is not a valid FreedomMedia vault, or its contents fail the
/// authenticated-encryption check (wrong key, corruption, or tampering).</summary>
public sealed class VaultCorruptException : Exception
{
    public VaultCorruptException(string message) : base(message) { }
}

public static class CryptoUtil
{
    public static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    /// <summary>Derive a 32-byte key-encryption-key from a passphrase using Argon2id.</summary>
    public static byte[] DeriveKek(byte[] passwordUtf8, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(passwordUtf8)
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            MemorySize = memoryKiB,
            Iterations = iterations,
        };
        return argon2.GetBytes(VaultFormat.MasterKeyLength);
    }

    public static (byte[] nonce, byte[] cipherWithTag) AesGcmEncrypt(byte[] key, byte[] plaintext, byte[]? associatedData = null)
    {
        var nonce = RandomBytes(VaultFormat.GcmNonceLength);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[VaultFormat.GcmTagLength];
        using var aes = new AesGcm(key, VaultFormat.GcmTagLength);
        aes.Encrypt(nonce, plaintext, cipher, tag, associatedData);
        var combined = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);
        return (nonce, combined);
    }

    /// <summary>Encrypt with an explicit (pre-derived) nonce — used for per-chunk data encryption.</summary>
    public static void AesGcmEncryptInto(byte[] key, byte[] nonce, ReadOnlySpan<byte> plaintext, Span<byte> cipherOut, Span<byte> tagOut, byte[]? associatedData = null)
    {
        using var aes = new AesGcm(key, VaultFormat.GcmTagLength);
        aes.Encrypt(nonce, plaintext, cipherOut, tagOut, associatedData);
    }

    public static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] cipherWithTag, byte[]? associatedData = null)
    {
        if (cipherWithTag.Length < VaultFormat.GcmTagLength)
            throw new VaultCorruptException("Ciphertext too short.");
        int cipherLen = cipherWithTag.Length - VaultFormat.GcmTagLength;
        var cipher = cipherWithTag.AsSpan(0, cipherLen);
        var tag = cipherWithTag.AsSpan(cipherLen, VaultFormat.GcmTagLength);
        var plain = new byte[cipherLen];
        using var aes = new AesGcm(key, VaultFormat.GcmTagLength);
        try
        {
            aes.Decrypt(nonce, cipher, tag, plain, associatedData);
        }
        catch (CryptographicException)
        {
            throw new WrongPasswordException();
        }
        return plain;
    }

    /// <summary>Decrypt directly into a caller-provided buffer (avoids an extra allocation/copy for large chunks).</summary>
    public static void AesGcmDecryptInto(byte[] key, byte[] nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, Span<byte> plainOut, byte[]? associatedData = null)
    {
        using var aes = new AesGcm(key, VaultFormat.GcmTagLength);
        try
        {
            aes.Decrypt(nonce, cipher, tag, plainOut, associatedData);
        }
        catch (CryptographicException)
        {
            throw new VaultCorruptException("A chunk failed authentication (wrong key or corrupted/tampered data).");
        }
    }

    public static byte[] HkdfDeriveFileKey(byte[] masterKey, byte[] fileSalt)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, VaultFormat.MasterKeyLength, fileSalt,
            System.Text.Encoding.UTF8.GetBytes(VaultFormat.HkdfInfo));
    }

    /// <summary>Derive the per-chunk nonce: baseNonce with the last 4 bytes XORed with the big-endian chunk index.</summary>
    public static byte[] DeriveChunkNonce(byte[] baseNonce, long chunkIndex)
    {
        if (baseNonce.Length != VaultFormat.GcmNonceLength)
            throw new ArgumentException("baseNonce must be 12 bytes");
        var nonce = (byte[])baseNonce.Clone();
        Span<byte> idx = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(idx, (uint)chunkIndex);
        nonce[8] ^= idx[0];
        nonce[9] ^= idx[1];
        nonce[10] ^= idx[2];
        nonce[11] ^= idx[3];
        return nonce;
    }
}
