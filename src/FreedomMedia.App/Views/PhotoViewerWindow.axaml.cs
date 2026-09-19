using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using FreedomMedia.Core;

namespace FreedomMedia.App.Views;

public partial class PhotoViewerWindow : Window
{
    private readonly VaultSession? _session;
    private readonly List<VaultEntry> _images = new();
    private int _index;
    private Bitmap? _current;

    // Parameterless constructor for the XAML loader.
    public PhotoViewerWindow()
    {
        InitializeComponent();
    }

    public PhotoViewerWindow(VaultSession session, IEnumerable<VaultEntry> images, VaultEntry start) : this()
    {
        _session = session;
        _images.AddRange(images.Where(e => e.IsImage));
        _index = Math.Max(0, _images.FindIndex(e => e.Id == start.Id));
        Opened += (_, _) => Show(_index);
    }

    private void Show(int index)
    {
        if (_session == null || _images.Count == 0) return;
        _index = (index % _images.Count + _images.Count) % _images.Count;
        var entry = _images[_index];

        var old = _current;
        try
        {
            using var stream = _session.OpenEntryStream(entry);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            _current = new Bitmap(ms);
            ImageView.Source = _current;
        }
        catch (Exception ex)
        {
            ImageView.Source = null;
            TitleText.Text = $"Could not open this photo: {ex.Message}";
        }
        old?.Dispose();

        Title = entry.Title;
        TitleText.Text = entry.Title;
        CounterText.Text = $"{_index + 1} of {_images.Count}";
        bool many = _images.Count > 1;
        PrevButton.IsVisible = many;
        NextButton.IsVisible = many;
    }

    private void OnPrev(object? sender, RoutedEventArgs e) => Show(_index - 1);
    private void OnNext(object? sender, RoutedEventArgs e) => Show(_index + 1);
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Show(_index - 1); e.Handled = true; break;
            case Key.Right: Show(_index + 1); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.F11:
                WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _current?.Dispose();
        _current = null;
        base.OnClosed(e);
    }
}
