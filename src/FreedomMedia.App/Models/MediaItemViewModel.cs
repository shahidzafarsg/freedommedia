using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using FreedomMedia.Core;

namespace FreedomMedia.App.Models;

/// <summary>One row in the library: wraps a VaultEntry and adds the display strings and the
/// (lazily loaded) thumbnail the gallery binds to.</summary>
public sealed class MediaItemViewModel : INotifyPropertyChanged
{
    public VaultEntry Entry { get; }

    public MediaItemViewModel(VaultEntry entry)
    {
        Entry = entry;
        _title = entry.Title;
    }

    private string _title;
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; Entry.Title = value; OnPropertyChanged(); } }
    }

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    public bool ThumbnailRequested { get; set; }

    public bool IsVideo => Entry.IsVideo;
    public string KindLabel => Entry.IsVideo ? "Video" : "Photo";
    public long SizeBytes => Entry.OriginalSize;
    public string SizeDisplay => FormatSize(Entry.OriginalSize);

    public string DimensionsDisplay => Entry.Width > 0 && Entry.Height > 0 ? $"{Entry.Width} × {Entry.Height}" : "—";

    public string DurationDisplay => Entry.IsVideo
        ? (Entry.DurationMs > 0 ? FormatDuration(Entry.DurationMs) : "—")
        : "";

    public string AddedDisplay
    {
        get
        {
            if (DateTimeOffset.TryParse(Entry.AddedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return "";
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    public static string FormatDuration(long ms)
    {
        if (ms <= 0) return "—";
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
