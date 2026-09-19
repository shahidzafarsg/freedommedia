namespace FreedomMedia.Core;

/// <summary>
/// Constants that define the on-disk FreedomMedia vault layout (a ".dvault" file).
///
/// Layout: magic | headerLen (int32 LE) | header JSON | indexBlockLen (int32 LE) |
///         [nonce | AES-256-GCM(index JSON)] | chunk data
///
/// Each media file is split into 1 MiB plaintext chunks; every chunk is encrypted with
/// AES-256-GCM under a per-file key (HKDF-SHA256 of the master key and a per-file salt) and a
/// per-chunk nonce (a per-file base nonce XORed with the big-endian chunk index). The master
/// key is random, wrapped with a key-encryption-key derived from the passphrase by Argon2id.
///
/// Unlike some sibling projects, this format has NO wrong-attempt counter and NO self-destruct:
/// a vault is a plain encrypted file that may be opened as many times as its owner likes.
/// </summary>
public static class VaultFormat
{
    public const string Magic = "FMVLT001";
    public const int MagicLength = 8;
    public const int CurrentVersion = 1;

    // Argon2id parameters for passphrase -> key-encryption-key.
    public const int ArgonMemoryKiB = 131072; // 128 MiB
    public const int ArgonIterations = 3;
    public const int ArgonParallelism = 4;
    public const int ArgonSaltLength = 16;
    public const int MasterKeyLength = 32; // AES-256

    public const int GcmNonceLength = 12;
    public const int GcmTagLength = 16;

    public const int ChunkPlainSize = 1024 * 1024; // 1 MiB

    public const string HkdfInfo = "freedommedia-file-key-v1";
    public const string VerifierPlaintext = "FREEDOMMEDIA-OK-v1";

    public const int MaxPasswordLength = 128;

    public const string MediaKindImage = "image";
    public const string MediaKindVideo = "video";
}
