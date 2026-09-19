namespace FreedomMedia.Core;

public sealed record ExtractProgress(string CurrentItem, long BytesDone, long BytesTotal, int FilesDone, int FilesTotal);

/// <summary>
/// Exports (decrypts) chosen entries out of a vault to ordinary files in a destination folder.
/// This is the deliberate escape hatch: the media leaves the vault and becomes a normal, readable
/// file again, at a location the user picks. Everything else in FreedomMedia keeps plaintext in
/// memory only, so export is the one place a readable copy is written to disk on purpose.
/// </summary>
public static class VaultExtractor
{
    public static void Extract(
        VaultSession session,
        IReadOnlyList<VaultEntry> entries,
        string destinationFolder,
        IProgress<ExtractProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destinationFolder);
        long totalBytes = entries.Sum(e => e.OriginalSize);
        long bytesDoneSoFar = 0;
        int filesDone = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            string outputPath = ResolveUniquePath(destinationFolder, BuildOutputFileName(entry));
            long entryStart = bytesDoneSoFar;
            progress?.Report(new ExtractProgress(entry.Title, entryStart, totalBytes, filesDone, entries.Count));

            using (var source = session.OpenEntryStream(entry))
            using (var dest = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 1024];
                long copied = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dest.Write(buffer, 0, read);
                    copied += read;
                    progress?.Report(new ExtractProgress(entry.Title, entryStart + copied, totalBytes, filesDone, entries.Count));
                }
            }
            bytesDoneSoFar += entry.OriginalSize;
            filesDone++;
        }
        progress?.Report(new ExtractProgress("", totalBytes, totalBytes, filesDone, entries.Count));
    }

    /// <summary>The filename to write for an exported entry: the original name where known, else
    /// the title, with a sensible extension and any characters illegal on the filesystem removed.</summary>
    public static string BuildOutputFileName(VaultEntry entry)
    {
        string baseName = !string.IsNullOrWhiteSpace(entry.OriginalFileName) ? entry.OriginalFileName : entry.Title;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(baseName)))
            baseName += entry.IsVideo ? ".mp4" : ".jpg";
        foreach (char c in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(c, '_');
        return baseName;
    }

    /// <summary>Never overwrites an existing file: appends " (2)", " (3)" and so on, the way a file
    /// manager resolves a name collision.</summary>
    public static string ResolveUniquePath(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;

        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int n = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(folder, $"{nameNoExt} ({n}){ext}");
            n++;
        } while (File.Exists(candidate));
        return candidate;
    }
}
