using System.Text.Json.Serialization;

namespace FreedomMedia.Core;

public sealed class KdfParams
{
    [JsonPropertyName("algo")] public string Algo { get; set; } = "argon2id";
    [JsonPropertyName("memoryKiB")] public int MemoryKiB { get; set; } = VaultFormat.ArgonMemoryKiB;
    [JsonPropertyName("iterations")] public int Iterations { get; set; } = VaultFormat.ArgonIterations;
    [JsonPropertyName("parallelism")] public int Parallelism { get; set; } = VaultFormat.ArgonParallelism;
    [JsonPropertyName("salt")] public string SaltBase64 { get; set; } = "";
}

public sealed class WrappedSecret
{
    [JsonPropertyName("nonce")] public string NonceBase64 { get; set; } = "";
    [JsonPropertyName("ciphertext")] public string CiphertextBase64 { get; set; } = "";
}

public sealed class VaultHeader
{
    [JsonPropertyName("version")] public int Version { get; set; } = VaultFormat.CurrentVersion;
    [JsonPropertyName("kdf")] public KdfParams Kdf { get; set; } = new();
    [JsonPropertyName("masterKeyWrap")] public WrappedSecret MasterKeyWrap { get; set; } = new();
    [JsonPropertyName("verifier")] public WrappedSecret Verifier { get; set; } = new();
}

public sealed class VaultEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("title")] public string Title { get; set; } = "";

    /// <summary>"image" or "video" (see VaultFormat.MediaKind*). Decides which built-in viewer opens it.</summary>
    [JsonPropertyName("mediaKind")] public string MediaKind { get; set; } = VaultFormat.MediaKindImage;

    [JsonPropertyName("originalFileName")] public string OriginalFileName { get; set; } = "";
    [JsonPropertyName("originalSize")] public long OriginalSize { get; set; }
    [JsonPropertyName("dataOffset")] public long DataOffset { get; set; }
    [JsonPropertyName("chunkPlainSize")] public int ChunkPlainSize { get; set; } = VaultFormat.ChunkPlainSize;
    [JsonPropertyName("chunkCount")] public long ChunkCount { get; set; }
    [JsonPropertyName("lastChunkPlainSize")] public int LastChunkPlainSize { get; set; }
    [JsonPropertyName("fileSalt")] public string FileSaltBase64 { get; set; } = "";
    [JsonPropertyName("baseNonce")] public string BaseNonceBase64 { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256Base64 { get; set; } = "";
    [JsonPropertyName("addedAt")] public string AddedAt { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }

    [JsonIgnore] public bool IsVideo => MediaKind == VaultFormat.MediaKindVideo;
    [JsonIgnore] public bool IsImage => MediaKind == VaultFormat.MediaKindImage;

    /// <summary>Length in bytes of this entry's encrypted chunk stream on disk.</summary>
    public long EncryptedDataLength()
    {
        if (ChunkCount == 0) return 0;
        long fullChunks = ChunkCount - 1;
        return fullChunks * (ChunkPlainSize + VaultFormat.GcmTagLength) + (LastChunkPlainSize + VaultFormat.GcmTagLength);
    }
}

public sealed class VaultIndex
{
    [JsonPropertyName("entries")] public List<VaultEntry> Entries { get; set; } = new();
}
