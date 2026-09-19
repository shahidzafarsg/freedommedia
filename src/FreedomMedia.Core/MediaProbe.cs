using System.Buffers.Binary;

namespace FreedomMedia.Core;

public readonly record struct MediaInfo(string Kind, int Width, int Height, long DurationMs);

/// <summary>
/// Best-effort inspection of a media file to decide whether it is an image or a video and to pull
/// pixel dimensions (and, for video, a duration) for display in the library. Anything it cannot
/// parse yields zeros; it is never required for viewing or playback, only for the details shown
/// beside each item.
/// </summary>
public static class MediaProbe
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".ico",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".m2ts", ".ts", ".3gp", ".ogv",
    };

    public static bool IsSupported(string path) => KindFromExtension(path) != null;

    /// <summary>Returns "image", "video", or null if the extension is not a recognised media type.</summary>
    public static string? KindFromExtension(string path)
    {
        var ext = Path.GetExtension(path);
        if (ImageExtensions.Contains(ext)) return VaultFormat.MediaKindImage;
        if (VideoExtensions.Contains(ext)) return VaultFormat.MediaKindVideo;
        return null;
    }

    public static MediaInfo Probe(string filePath)
    {
        string kind = KindFromExtension(filePath) ?? VaultFormat.MediaKindImage;
        try
        {
            if (kind == VaultFormat.MediaKindVideo)
            {
                var (w, h, ms) = ProbeMp4(filePath);
                return new MediaInfo(kind, w, h, ms);
            }
            var (iw, ih) = ProbeImage(filePath);
            return new MediaInfo(kind, iw, ih, 0);
        }
        catch
        {
            return new MediaInfo(kind, 0, 0, 0);
        }
    }

    // ---- Image dimensions -------------------------------------------------

    private static (int, int) ProbeImage(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> head = stackalloc byte[32];
        int n = fs.Read(head);
        if (n < 24) return (0, 0);
        fs.Position = 0;

        // PNG: 89 50 4E 47 0D 0A 1A 0A, then IHDR at offset 16 (width, height as big-endian uint32).
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
        {
            int w = (int)BinaryPrimitives.ReadUInt32BigEndian(head.Slice(16, 4));
            int h = (int)BinaryPrimitives.ReadUInt32BigEndian(head.Slice(20, 4));
            return (w, h);
        }
        // GIF: "GIF87a"/"GIF89a", width/height little-endian uint16 at offset 6.
        if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F')
        {
            int w = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(6, 2));
            int h = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(8, 2));
            return (w, h);
        }
        // BMP: "BM", width/height little-endian int32 at offset 18/22.
        if (head[0] == 'B' && head[1] == 'M')
        {
            int w = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(18, 4));
            int h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head.Slice(22, 4)));
            return (w, h);
        }
        // JPEG: scan the segment markers for a Start-Of-Frame.
        if (head[0] == 0xFF && head[1] == 0xD8)
        {
            return ProbeJpeg(fs);
        }
        // WEBP (RIFF....WEBP): handle the common VP8/VP8L/VP8X sub-formats.
        if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
            head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
        {
            return ProbeWebp(head);
        }
        return (0, 0);
    }

    private static (int, int) ProbeJpeg(FileStream fs)
    {
        fs.Position = 2;
        Span<byte> b = stackalloc byte[8];
        while (true)
        {
            int m0 = fs.ReadByte();
            if (m0 < 0) return (0, 0);
            if (m0 != 0xFF) continue;
            int marker = fs.ReadByte();
            while (marker == 0xFF) marker = fs.ReadByte();
            if (marker < 0) return (0, 0);
            // SOF0..SOF15 (excluding DHT=C4, DNL=C8, DAC=CC) carry the frame dimensions.
            if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (fs.Read(b.Slice(0, 7)) != 7) return (0, 0);
                int h = (b[3] << 8) | b[4];
                int w = (b[5] << 8) | b[6];
                return (w, h);
            }
            // Otherwise skip this segment using its length.
            int l0 = fs.ReadByte();
            int l1 = fs.ReadByte();
            if (l1 < 0) return (0, 0);
            int segLen = (l0 << 8) | l1;
            if (segLen < 2) return (0, 0);
            fs.Position += segLen - 2;
        }
    }

    private static (int, int) ProbeWebp(ReadOnlySpan<byte> head)
    {
        // Chunk FourCC at offset 12.
        string fourcc = System.Text.Encoding.ASCII.GetString(head.Slice(12, 4));
        if (fourcc == "VP8 ")
        {
            // Lossy: dimensions at offset 26 (14-bit little-endian each).
            int w = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(26, 2)) & 0x3FFF;
            int h = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(28, 2)) & 0x3FFF;
            return (w, h);
        }
        if (fourcc == "VP8L")
        {
            // Lossless: 1-byte signature (0x2F) then 14 bits width, 14 bits height.
            int b0 = head[21], b1 = head[22], b2 = head[23], b3 = head[24];
            int w = ((b1 & 0x3F) << 8 | b0) + 1;
            int h = ((b3 & 0x0F) << 10 | b2 << 2 | (b1 & 0xC0) >> 6) + 1;
            return (w, h);
        }
        if (fourcc == "VP8X")
        {
            // Extended: 24-bit little-endian (width-1) at 24, (height-1) at 27.
            int w = (head[24] | head[25] << 8 | head[26] << 16) + 1;
            int h = (head[27] | head[28] << 8 | head[29] << 16) + 1;
            return (w, h);
        }
        return (0, 0);
    }

    // ---- MP4 / MOV duration and dimensions --------------------------------

    private static (int, int, long) ProbeMp4(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long timescale = 0, duration = 0;
        int bestW = 0, bestH = 0;
        WalkBoxes(fs, fs.Length, 0, ref timescale, ref duration, ref bestW, ref bestH);
        long durationMs = timescale > 0 ? (long)(duration * 1000.0 / timescale) : 0;
        return (bestW, bestH, durationMs);
    }

    private static void WalkBoxes(Stream s, long end, int depth, ref long timescale, ref long duration, ref int bestW, ref int bestH)
    {
        if (depth > 8) return;
        var header = new byte[8];
        while (s.Position + 8 <= end)
        {
            long boxStart = s.Position;
            if (TryReadExact(s, header) != 8) return;
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            long headerSize = 8;
            if (size == 1)
            {
                var large = new byte[8];
                if (TryReadExact(s, large) != 8) return;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(large);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = end - boxStart;
            }
            long boxEnd = boxStart + size;
            if (boxEnd > end || size < headerSize) return;

            if (type is "moov" or "trak" or "mdia" or "minf" or "stbl")
                WalkBoxes(s, boxEnd, depth + 1, ref timescale, ref duration, ref bestW, ref bestH);
            else if (type == "mvhd")
                ReadMvhd(s, boxStart + headerSize, ref timescale, ref duration);
            else if (type == "tkhd")
                ReadTkhd(s, boxStart + headerSize, ref bestW, ref bestH);

            s.Position = boxEnd;
        }
    }

    private static int TryReadExact(Stream s, byte[] buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = s.Read(buf, read, buf.Length - read);
            if (n == 0) break;
            read += n;
        }
        return read;
    }

    private static void ReadMvhd(Stream s, long pos, ref long timescale, ref long duration)
    {
        s.Position = pos;
        var versionFlags = new byte[4];
        if (TryReadExact(s, versionFlags) != 4) return;
        if (versionFlags[0] == 1)
        {
            var buf = new byte[8 + 8 + 4 + 8];
            if (TryReadExact(s, buf) != buf.Length) return;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(16, 4));
            duration = (long)BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(20, 8));
        }
        else
        {
            var buf = new byte[4 + 4 + 4 + 4];
            if (TryReadExact(s, buf) != buf.Length) return;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12, 4));
        }
    }

    private static void ReadTkhd(Stream s, long pos, ref int bestW, ref int bestH)
    {
        s.Position = pos;
        var versionFlags = new byte[4];
        if (TryReadExact(s, versionFlags) != 4) return;
        int skip = versionFlags[0] == 1
            ? (8 + 8 + 4 + 4 + 8 + 2 + 2 + 2 + 2 + 36)
            : (4 + 4 + 4 + 4 + 4 + 8 + 2 + 2 + 2 + 2 + 36);
        var skipBuf = new byte[skip];
        if (TryReadExact(s, skipBuf) != skip) return;
        var wh = new byte[8];
        if (TryReadExact(s, wh) != 8) return;
        int w = (int)(BinaryPrimitives.ReadUInt32BigEndian(wh.AsSpan(0, 4)) >> 16);
        int h = (int)(BinaryPrimitives.ReadUInt32BigEndian(wh.AsSpan(4, 4)) >> 16);
        if ((long)w * h > (long)bestW * bestH) { bestW = w; bestH = h; }
    }
}
