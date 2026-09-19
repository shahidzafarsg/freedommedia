// Dependency-free generator for the FreedomMedia app icon. It reproduces, pixel for pixel, the
// FreedomMedia brand icon used on freedomsoft.uk (a teal->indigo rounded square with the white
// FreedomSoft "F" and a camera glyph), rasterising the same shapes the site draws in SVG, at 4x
// supersampling. Writes a PNG (256 and 1024) and a multi-size .ico. No System.Drawing, so it runs
// the same on Windows and macOS.

using System.IO.Compression;
using System.Buffers.Binary;

const int SS = 4;

static (byte r, byte g, byte b) Lerp((byte, byte, byte) a, (byte, byte, byte) b, double t)
    => ((byte)(a.Item1 + (b.Item1 - a.Item1) * t),
        (byte)(a.Item2 + (b.Item2 - a.Item2) * t),
        (byte)(a.Item3 + (b.Item3 - a.Item3) * t));

// Render one RGBA image at the given output size (supersampled internally). All coordinates are in
// the SVG's 256x256 space, scaled by `k` to the supersampled canvas.
static byte[] Render(int size)
{
    int S = size * SS;
    double k = S / 256.0;
    var px = new double[S * S * 4]; // straight RGBA, 0..255

    var teal = ((byte)0x14, (byte)0xb8, (byte)0xa6);
    var indigo = ((byte)0x4f, (byte)0x46, (byte)0xe5);
    var white = ((byte)0xff, (byte)0xff, (byte)0xff);

    double RoundRectCoverage(double x, double y, double x0, double y0, double x1, double y1, double r)
    {
        double dx = x - Math.Clamp(x, x0 + r, x1 - r);
        double dy = y - Math.Clamp(y, y0 + r, y1 - r);
        double dist = Math.Sqrt(dx * dx + dy * dy) - r;
        return Math.Clamp(0.5 - dist, 0, 1);
    }

    void Blend(int i, (byte r, byte g, byte b) c, double a)
    {
        if (a <= 0) return;
        px[i] = px[i] * (1 - a) + c.r * a;
        px[i + 1] = px[i + 1] * (1 - a) + c.g * a;
        px[i + 2] = px[i + 2] * (1 - a) + c.b * a;
        px[i + 3] = Math.Max(px[i + 3], a * 255);
    }

    // Background rounded square: rect 8,8 240x240 rx58, diagonal teal->indigo.
    double bx0 = 8 * k, by0 = 8 * k, bx1 = 248 * k, by1 = 248 * k, br = 58 * k;
    for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            double cov = RoundRectCoverage(x + 0.5, y + 0.5, bx0, by0, bx1, by1, br);
            if (cov <= 0) continue;
            double t = ((x / (double)S) + (y / (double)S)) / 2.0;
            Blend((y * S + x) * 4, Lerp(teal, indigo, t), cov);
        }

    // Top highlight: rect 8,8 240x120 rx58, white vertical gradient .22 -> 0.
    double hy0 = 8 * k, hy1 = 128 * k;
    for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int i = (y * S + x) * 4;
            if (px[i + 3] <= 0) continue;
            double cov = RoundRectCoverage(x + 0.5, y + 0.5, bx0, hy0, bx1, hy1, br);
            if (cov <= 0) continue;
            double f = 1 - Math.Clamp(((y + 0.5) - hy0) / (hy1 - hy0), 0, 1);
            Blend(i, white, 0.22 * f * cov * (px[i + 3] / 255.0));
        }

    // The white "F": path M62 54 h92 v26 H90 v28 h56 v24 H90 v68 H62 z
    (double x, double y)[] fPoly =
    {
        (62, 54), (154, 54), (154, 80), (90, 80), (90, 108),
        (146, 108), (146, 132), (90, 132), (90, 200), (62, 200),
    };
    bool InPoly((double x, double y) p, (double x, double y)[] poly)
    {
        bool inside = false;
        for (int a = 0, b = poly.Length - 1; a < poly.Length; b = a++)
        {
            var pa = poly[a]; var pb = poly[b];
            if (((pa.y > p.y) != (pb.y > p.y)) &&
                (p.x < (pb.x - pa.x) * (p.y - pa.y) / (pb.y - pa.y) + pa.x))
                inside = !inside;
        }
        return inside;
    }
    for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            var p = ((x + 0.5) / k, (y + 0.5) / k);
            if (InPoly(p, fPoly)) Blend((y * S + x) * 4, white, 1.0);
        }

    // Camera body: rect 138,166 72x54 rx12, white .9
    double cx0 = 138 * k, cy0 = 166 * k, cx1 = 210 * k, cy1 = 220 * k, cr = 12 * k;
    for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            double cov = RoundRectCoverage(x + 0.5, y + 0.5, cx0, cy0, cx1, cy1, cr);
            if (cov > 0) Blend((y * S + x) * 4, white, 0.9 * cov);
        }

    // Camera hump: stroke of path M152 170 v-14 a22 22 0 0 1 44 0 v14, width 14, white .9.
    // Left segment x=152 y 156..170; arc centre (174,156) r22 over the top; right segment x=196 y156..170.
    double half = 7 * k;
    (double x, double y) arcC = (174 * k, 156 * k);
    double arcR = 22 * k;
    double DistSeg(double px_, double py_, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax, vy = by - ay, wx = px_ - ax, wy = py_ - ay;
        double t = Math.Clamp((wx * vx + wy * vy) / (vx * vx + vy * vy), 0, 1);
        double dx = px_ - (ax + t * vx), dy = py_ - (ay + t * vy);
        return Math.Sqrt(dx * dx + dy * dy);
    }
    for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            double p_x = x + 0.5, p_y = y + 0.5;
            double d = Math.Min(
                DistSeg(p_x, p_y, 152 * k, 156 * k, 152 * k, 170 * k),
                DistSeg(p_x, p_y, 196 * k, 156 * k, 196 * k, 170 * k));
            if (p_y <= arcC.y) // upper half: distance to the arc ring
            {
                double dc = Math.Sqrt((p_x - arcC.x) * (p_x - arcC.x) + (p_y - arcC.y) * (p_y - arcC.y));
                d = Math.Min(d, Math.Abs(dc - arcR));
            }
            double cov = Math.Clamp(half - d + 0.5, 0, 1);
            if (cov > 0) Blend((y * S + x) * 4, white, 0.9 * cov);
        }

    // Downsample SSxSS -> 1x by averaging.
    var outPx = new byte[size * size * 4];
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double r = 0, g = 0, b = 0, a = 0;
            for (int dy = 0; dy < SS; dy++)
                for (int dx = 0; dx < SS; dx++)
                {
                    int si = ((y * SS + dy) * S + (x * SS + dx)) * 4;
                    r += px[si]; g += px[si + 1]; b += px[si + 2]; a += px[si + 3];
                }
            int n = SS * SS, oi = (y * size + x) * 4;
            outPx[oi] = (byte)(r / n); outPx[oi + 1] = (byte)(g / n); outPx[oi + 2] = (byte)(b / n); outPx[oi + 3] = (byte)(a / n);
        }
    return outPx;
}

static byte[] EncodePng(byte[] rgba, int w, int h)
{
    using var ms = new MemoryStream();
    ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    void Chunk(string type, byte[] data)
    {
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBuf, data.Length);
        ms.Write(lenBuf);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes);
        ms.Write(data);
        uint crc = Crc32(typeBytes, data);
        var crcBuf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        ms.Write(crcBuf);
    }

    var ihdr = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), w);
    BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), h);
    ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
    Chunk("IHDR", ihdr);

    var raw = new byte[h * (w * 4 + 1)];
    int pos = 0;
    for (int y = 0; y < h; y++)
    {
        raw[pos++] = 0;
        Buffer.BlockCopy(rgba, y * w * 4, raw, pos, w * 4);
        pos += w * 4;
    }
    using var comp = new MemoryStream();
    using (var z = new ZLibStream(comp, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
    Chunk("IDAT", comp.ToArray());
    Chunk("IEND", Array.Empty<byte>());
    return ms.ToArray();
}

static uint Crc32(byte[] a, byte[] b)
{
    uint c = 0xffffffff;
    void Feed(byte[] d) { foreach (var x in d) { c ^= x; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1; } }
    Feed(a); Feed(b);
    return c ^ 0xffffffff;
}

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

File.WriteAllBytes(Path.Combine(outDir, "freedommedia.png"), EncodePng(Render(256), 256, 256));
File.WriteAllBytes(Path.Combine(outDir, "freedommedia-1024.png"), EncodePng(Render(1024), 1024, 1024));

int[] icoSizes = { 256, 64, 32, 16 };
var pngs = icoSizes.Select(s => (s, data: EncodePng(Render(s), s, s))).ToList();
using (var ico = new MemoryStream())
{
    var hdr = new byte[6];
    BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(2, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(4, 2), (ushort)pngs.Count);
    ico.Write(hdr);
    int offset = 6 + 16 * pngs.Count;
    foreach (var (s, data) in pngs)
    {
        var e = new byte[16];
        e[0] = (byte)(s >= 256 ? 0 : s);
        e[1] = (byte)(s >= 256 ? 0 : s);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(6, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(8, 4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(12, 4), offset);
        ico.Write(e);
        offset += data.Length;
    }
    foreach (var (_, data) in pngs) ico.Write(data);
    File.WriteAllBytes(Path.Combine(outDir, "freedommedia.ico"), ico.ToArray());
}

Console.WriteLine("Icons written to " + Path.GetFullPath(outDir));
