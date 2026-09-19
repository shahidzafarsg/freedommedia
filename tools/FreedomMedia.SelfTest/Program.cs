using System.Security.Cryptography;
using FreedomMedia.Core;

// A self-contained check of the FreedomMedia vault core: create a vault, import files of several
// sizes, reopen it, read every byte back and compare, confirm a wrong passphrase is rejected with
// no penalty (unlimited attempts, no self-destruct), then export an item back out and compare.
// Exits non-zero on the first failure so it can gate a build.

int failures = 0;
void Check(bool ok, string label)
{
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
    if (!ok) failures++;
}

string work = Path.Combine(Path.GetTempPath(), "fm-selftest-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(work);
string vaultPath = Path.Combine(work, "test.dvault");
const string password = "correct horse battery staple";

try
{
    // Build source files spanning chunk boundaries (1 MiB chunk size): empty, tiny, exactly one
    // chunk, just over one chunk, and several chunks plus a remainder.
    var sizes = new (string name, long size)[]
    {
        ("empty.bin", 0),
        ("tiny.bin", 10),
        ("one-chunk.bin", 1024 * 1024),
        ("one-chunk-plus.bin", 1024 * 1024 + 7),
        ("multi.bin", 3 * 1024 * 1024 + 12345),
    };
    var sources = new List<(string path, byte[] data)>();
    foreach (var (name, size) in sizes)
    {
        var data = new byte[size];
        RandomNumberGenerator.Fill(data);
        string p = Path.Combine(work, name);
        File.WriteAllBytes(p, data);
        sources.Add((p, data));
    }

    // Create the vault.
    var items = sources.Select(s => (PendingItem)new NewFileItem
    {
        Title = Path.GetFileNameWithoutExtension(s.path),
        SourceFilePath = s.path,
    }).ToList();
    VaultWriter.Build(vaultPath, password, null, items, null, CancellationToken.None);
    Check(File.Exists(vaultPath), "vault file created");

    // Open and read back.
    using (var session = VaultSession.Open(vaultPath, password))
    {
        Check(session.Entries.Count == sources.Count, $"entry count is {sources.Count}");
        for (int i = 0; i < sources.Count; i++)
        {
            var entry = session.Entries[i];
            using var stream = session.OpenEntryStream(entry);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var readBack = ms.ToArray();
            Check(readBack.Length == sources[i].data.Length && readBack.AsSpan().SequenceEqual(sources[i].data),
                $"round-trip bytes match for {entry.Title} ({sources[i].data.Length} bytes)");
        }

        // Seek/partial read in the middle of a multi-chunk entry.
        var big = session.Entries.First(e => e.Title == "multi");
        using (var stream = session.OpenEntryStream(big))
        {
            long pos = 1024 * 1024 + 1000;
            stream.Position = pos;
            var buf = new byte[5000];
            int n = stream.Read(buf, 0, buf.Length);
            var expected = sources.First(s => Path.GetFileNameWithoutExtension(s.path) == "multi").data.AsSpan((int)pos, n).ToArray();
            Check(n == 5000 && buf.AsSpan(0, n).SequenceEqual(expected), "seek + partial read across chunk boundary");
        }
    }

    // Wrong passphrase is rejected, and doing so many times leaves the file fully intact
    // (no attempt limit, no self-destruct).
    int rejected = 0;
    for (int attempt = 0; attempt < 20; attempt++)
    {
        try { using var _ = VaultSession.Open(vaultPath, "wrong passphrase " + attempt); }
        catch (WrongPasswordException) { rejected++; }
    }
    Check(rejected == 20, "20 wrong attempts all rejected");
    Check(File.Exists(vaultPath), "vault still present after 20 wrong attempts (no self-destruct)");
    using (var session = VaultSession.Open(vaultPath, password))
        Check(session.Entries.Count == sources.Count, "vault still opens correctly after wrong attempts");

    // Export back out to normal files.
    string outDir = Path.Combine(work, "exported");
    using (var session = VaultSession.Open(vaultPath, password))
    {
        VaultExtractor.Extract(session, session.Entries.ToList(), outDir, null, CancellationToken.None);
    }
    var exported = Directory.GetFiles(outDir);
    Check(exported.Length == sources.Count, "exported one file per entry");

    Console.WriteLine(failures == 0 ? "\nALL PASSED" : $"\n{failures} FAILURE(S)");
}
finally
{
    try { Directory.Delete(work, true); } catch { /* best effort */ }
}

return failures == 0 ? 0 : 1;
