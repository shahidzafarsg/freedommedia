using System.Runtime.InteropServices;
using System.Text;
using FreedomMedia.Core;
using LibVLCSharp.Shared;

// Headless reproduction of FreedomMedia's NEW video path: play a vault video through LibVLC's frame
// callbacks (the same SetVideoFormatCallbacks/SetVideoCallbacks approach VlcVideoRenderer uses),
// writing decoded frames into an unmanaged buffer. This validates the native-interop that replaced
// the crashing VideoView, on real Windows, without needing a display.

string srcVideo = args.Length > 0 ? args[0] : throw new ArgumentException("Pass a source video path.");
string work = Path.Combine(Path.GetTempPath(), "fm-videoprobe-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(work);
string vaultPath = Path.Combine(work, "test.dvault");
const string password = "test-pass";

IntPtr buffer = IntPtr.Zero;
uint width = 0, height = 0, pitch = 0;
int displayCount = 0;
long lastNonZeroByteSum = 0;

// Keep delegates rooted.
MediaPlayer.LibVLCVideoFormatCb formatCb = OnFormat;
MediaPlayer.LibVLCVideoCleanupCb cleanupCb = OnCleanup;
MediaPlayer.LibVLCVideoLockCb lockCb = OnLock;
MediaPlayer.LibVLCVideoDisplayCb displayCb = OnDisplay;

try
{
    Console.WriteLine($"Source: {srcVideo} ({new FileInfo(srcVideo).Length} bytes)");
    VaultWriter.Build(vaultPath, password, null,
        new[] { (PendingItem)new NewFileItem { Title = "clip", SourceFilePath = srcVideo } },
        null, CancellationToken.None);

    using var session = VaultSession.Open(vaultPath, password);
    var entry = session.Entries[0];
    Console.WriteLine($"Entry: {entry.Width}x{entry.Height} dur={entry.DurationMs}ms");

    Core.Initialize();
    using var libVLC = new LibVLC("--no-audio", "--quiet");
    using var stream = session.OpenEntryStream(entry);
    using var media = new Media(libVLC, new StreamMediaInput(stream));
    using var mp = new MediaPlayer(libVLC);

    mp.SetVideoFormatCallbacks(formatCb, cleanupCb);
    mp.SetVideoCallbacks(lockCb, null, displayCb);

    Console.WriteLine("Play() with frame callbacks...");
    mp.Play(media);

    for (int i = 0; i < 16; i++)
    {
        Thread.Sleep(500);
        Console.WriteLine($"t={i * 0.5:0.0}s state={mp.State} frames={displayCount} size={width}x{height} lastFrameByteSum={lastNonZeroByteSum}");
        if (displayCount >= 10 && lastNonZeroByteSum > 0)
        {
            Console.WriteLine($"SUCCESS: {displayCount} frames rendered into the buffer, pixels are non-zero. No crash.");
            break;
        }
        if (mp.State == VLCState.Error) { Console.WriteLine("Player entered Error state."); break; }
    }

    mp.Stop();
    Console.WriteLine("Stopped cleanly.");
}
finally
{
    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
    try { Directory.Delete(work, true); } catch { }
}

uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h, ref uint pitches, ref uint lines)
{
    var fourcc = Encoding.ASCII.GetBytes("RV32");
    Marshal.Copy(fourcc, 0, chroma, 4);
    pitch = w * 4;
    pitches = pitch;
    lines = h;
    width = w; height = h;
    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
    buffer = Marshal.AllocHGlobal(checked((int)(pitch * h)));
    Console.WriteLine($"format cb: {w}x{h} RV32 pitch={pitch} buffer=0x{buffer:X}");
    return 1;
}

IntPtr OnLock(IntPtr opaque, IntPtr planes)
{
    Marshal.WriteIntPtr(planes, 0, buffer);
    return buffer;
}

void OnDisplay(IntPtr opaque, IntPtr picture)
{
    displayCount++;
    // Sanity-check the buffer is readable and holds real pixel data (sample a few bytes).
    if (buffer != IntPtr.Zero && (displayCount % 15 == 1))
    {
        long sum = 0;
        int samples = Math.Min(4096, (int)(pitch * height));
        for (int i = 0; i < samples; i += 4) sum += Marshal.ReadByte(buffer, i);
        lastNonZeroByteSum = sum;
    }
}

void OnCleanup(ref IntPtr opaque)
{
    if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
}
