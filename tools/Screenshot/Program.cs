using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Webp;

// Renders a faithful image of the FreedomMedia application UI (dark theme: menu bar, toolbar with
// the view switcher, an icon gallery of photos and videos, and the status bar), matching the real
// app's colours and layout, at 1600x1000. Writes a PNG (for review) and a WebP (for the website).

const int W = 1600, H = 1000;

var bg = Color.ParseHex("#16171C");
var panel = Color.ParseHex("#20222A");
var panel2 = Color.ParseHex("#2A2D36");
var text = Color.ParseHex("#F1F2F6");
var subtext = Color.ParseHex("#9AA0AC");
var border = Color.ParseHex("#33363F");
var accent = Color.ParseHex("#5B8CFF");

Font Load(float size, FontStyle style = FontStyle.Regular)
{
    foreach (var name in new[] { "Segoe UI", "Arial", "Helvetica", "DejaVu Sans" })
        if (SystemFonts.TryGet(name, out var fam)) return fam.CreateFont(size, style);
    return SystemFonts.Families.First().CreateFont(size, style);
}

var menuFont = Load(15f);
var toolFont = Load(14.5f);
var titleFont = Load(15.5f);
var subFont = Load(13f);
var statusFont = Load(13.5f);
var badgeFont = Load(12f);

// Fill a rounded rectangle by composing it from two rectangles and four corner circles; the same
// brush over all pieces gives a seamless result (a gradient brush is defined in absolute coords).
void FillRounded(IImageProcessingContext c, Brush brush, float x, float y, float w, float h, float r)
{
    c.Fill(brush, new RectangularPolygon(x + r, y, w - 2 * r, h));
    c.Fill(brush, new RectangularPolygon(x, y + r, w, h - 2 * r));
    c.Fill(brush, new EllipsePolygon(x + r, y + r, r));
    c.Fill(brush, new EllipsePolygon(x + w - r, y + r, r));
    c.Fill(brush, new EllipsePolygon(x + r, y + h - r, r));
    c.Fill(brush, new EllipsePolygon(x + w - r, y + h - r, r));
}

using var img = new Image<Rgba32>(W, H);

var tiles = new (string title, string size, string dur, string c1, string c2, bool video)[]
{
    ("Sunset over the bay", "4.2 MB", "", "#F59E0B", "#7C3AED", false),
    ("Harbour walk", "3.1 MB", "", "#F472B6", "#4F46E5", false),
    ("Graduation 2024", "128 MB", "2:14", "#4F46E5", "#9333EA", true),
    ("Mountain trail", "5.6 MB", "", "#14B8A6", "#166534", false),
    ("City lights", "3.9 MB", "", "#3B82F6", "#1E1B4B", false),
    ("Birthday party", "96 MB", "1:47", "#EC4899", "#B91C1C", true),
    ("Autumn leaves", "2.8 MB", "", "#F59E0B", "#92400E", false),
    ("Beach morning", "4.5 MB", "", "#22D3EE", "#2563EB", false),
    ("Old town square", "3.4 MB", "", "#8B5CF6", "#312E81", false),
    ("Snowfall", "2.2 MB", "", "#64748B", "#1E3A8A", false),
    ("Concert night", "210 MB", "3:52", "#A855F7", "#DB2777", true),
    ("Garden in bloom", "3.7 MB", "", "#22C55E", "#65A30D", false),
    ("Riverside", "4.0 MB", "", "#14B8A6", "#0EA5E9", false),
    ("Family dinner", "5.1 MB", "", "#FB923C", "#DC2626", false),
    ("Coastline", "4.8 MB", "", "#38BDF8", "#0D9488", false),
};

img.Mutate(ctx =>
{
    ctx.Fill(bg, new RectangleF(0, 0, W, H));

    // ---- Menu bar ----
    ctx.Fill(panel, new RectangleF(0, 0, W, 40));
    float mx = 22;
    foreach (var m in new[] { "File", "Edit", "View", "Help" })
    {
        ctx.DrawText(m, menuFont, text, new PointF(mx, 11));
        mx += TextMeasurer.MeasureSize(m, new TextOptions(menuFont)).Width + 34;
    }

    // ---- Toolbar ----
    ctx.Fill(panel, new RectangleF(0, 40, W, 58));
    ctx.Fill(border, new RectangleF(0, 97, W, 1));
    float tx = 22;
    void Tool(string label, Color col)
    {
        ctx.DrawText(label, toolFont, col, new PointF(tx, 60));
        tx += TextMeasurer.MeasureSize(label, new TextOptions(toolFont)).Width + 22;
    }
    Tool("New", text); Tool("Open", text); Tool("Close", text);
    ctx.DrawText("|", toolFont, border, new PointF(tx - 8, 60)); tx += 14;
    Tool("Import", text); Tool("Export", text); Tool("Rename", text); Tool("Remove", text);

    // Right side: view switcher (Icons selected).
    ctx.DrawText("View", toolFont, subtext, new PointF(W - 300, 60));
    FillRounded(ctx, new SolidBrush(accent), W - 246, 54, 62, 30, 7);
    ctx.DrawText("Icons", toolFont, Color.White, new PointF(W - 236, 60));
    ctx.DrawText("List", toolFont, subtext, new PointF(W - 172, 60));
    ctx.DrawText("Details", toolFont, subtext, new PointF(W - 120, 60));

    // ---- Gallery ----
    float left = 26, top = 122, gap = 22;
    int cols = 5;
    float tw = (W - left * 2 - gap * (cols - 1)) / cols;
    float thumbH = tw * 0.62f;
    float rowH = thumbH + 62;

    for (int i = 0; i < tiles.Length; i++)
    {
        int r = i / cols, c = i % cols;
        float x = left + c * (tw + gap);
        float y = top + r * (rowH + 18);
        var t = tiles[i];

        var grad = new LinearGradientBrush(new PointF(x, y), new PointF(x + tw, y + thumbH),
            GradientRepetitionMode.None,
            new ColorStop(0f, Color.ParseHex(t.c1)), new ColorStop(1f, Color.ParseHex(t.c2)));
        FillRounded(ctx, grad, x, y, tw, thumbH, 10);

        // Scenic hints so the tiles read as photographs: a soft sun and two hills.
        ctx.Fill(Color.FromRgba(255, 255, 255, 55), new EllipsePolygon(x + tw * 0.27f, y + thumbH * 0.32f, thumbH * 0.11f));
        ctx.Fill(Color.FromRgba(0, 0, 0, 55), new Polygon(new LinearLineSegment(
            new PointF(x + tw * 0.02f, y + thumbH * 0.98f), new PointF(x + tw * 0.42f, y + thumbH * 0.52f), new PointF(x + tw * 0.74f, y + thumbH * 0.98f))));
        ctx.Fill(Color.FromRgba(0, 0, 0, 40), new Polygon(new LinearLineSegment(
            new PointF(x + tw * 0.46f, y + thumbH * 0.98f), new PointF(x + tw * 0.74f, y + thumbH * 0.60f), new PointF(x + tw * 1.0f, y + thumbH * 0.98f))));

        if (t.video)
        {
            float cx = x + tw / 2, cy = y + thumbH / 2, rad = thumbH * 0.16f;
            ctx.Fill(Color.FromRgba(91, 140, 255, 210), new EllipsePolygon(cx, cy, rad));
            ctx.Fill(Color.White, new Polygon(new LinearLineSegment(
                new PointF(cx - rad * 0.35f, cy - rad * 0.5f), new PointF(cx - rad * 0.35f, cy + rad * 0.5f), new PointF(cx + rad * 0.55f, cy))));

            FillRounded(ctx, new SolidBrush(Color.FromRgba(0, 0, 0, 160)), x + tw - 62, y + thumbH - 30, 50, 20, 6);
            ctx.DrawText(t.dur, badgeFont, Color.White, new PointF(x + tw - 54, y + thumbH - 27));
        }

        ctx.DrawText(t.title, titleFont, text, new PointF(x + 2, y + thumbH + 10));
        string sub = t.video ? "Video   " + t.size : t.size;
        ctx.DrawText(sub, subFont, subtext, new PointF(x + 2, y + thumbH + 34));
    }

    // ---- Status bar ----
    ctx.Fill(border, new RectangleF(0, H - 40, W, 1));
    ctx.Fill(panel, new RectangleF(0, H - 39, W, 39));
    ctx.DrawText("Holiday 2024.dvault   •   15 items (12 photos, 3 videos)   •   477.1 MB", statusFont, subtext, new PointF(22, H - 29));
    string sel = "Sunset over the bay   •   Photo   •   4.2 MB   •   4032 × 3024";
    float selW = TextMeasurer.MeasureSize(sel, new TextOptions(statusFont)).Width;
    ctx.DrawText(sel, statusFont, subtext, new PointF(W - 22 - selW, H - 29));
});

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
img.SaveAsPng(System.IO.Path.Combine(outDir, "freedommedia.png"));
img.SaveAsWebp(System.IO.Path.Combine(outDir, "freedommedia-dark.webp"), new WebpEncoder { Quality = 84, FileFormat = WebpFileFormatType.Lossy });
Console.WriteLine("Wrote freedommedia.png and freedommedia-dark.webp to " + System.IO.Path.GetFullPath(outDir));
