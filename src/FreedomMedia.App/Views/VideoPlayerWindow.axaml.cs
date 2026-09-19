using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FreedomMedia.App.Models;
using FreedomMedia.Core;
using LibVLCSharp.Shared;

namespace FreedomMedia.App.Views;

public partial class VideoPlayerWindow : Window
{
    private static bool _coreInitialized;

    private readonly VaultSession? _session;
    private readonly VaultEntry? _entry;
    private LibVLC? _libVLC;
    private MediaPlayer? _player;
    private VaultEntryStream? _stream;
    private Media? _media;
    private DispatcherTimer? _timer;
    private bool _seeking;

    // Parameterless constructor for the XAML loader.
    public VideoPlayerWindow()
    {
        InitializeComponent();
    }

    public VideoPlayerWindow(VaultSession session, VaultEntry entry) : this()
    {
        _session = session;
        _entry = entry;
        Title = entry.Title;
        InfoText.Text = BuildInfo(entry);
        Opened += (_, _) => Start();
    }

    private static string BuildInfo(VaultEntry e)
    {
        string res = e.Width > 0 && e.Height > 0 ? $"{e.Width} × {e.Height}" : "";
        string size = MediaItemViewModel.FormatSize(e.OriginalSize);
        return string.Join("   •   ", new[] { e.Title, res, size }.Where(s => !string.IsNullOrEmpty(s)));
    }

    private void Start()
    {
        if (_session == null || _entry == null) return;
        try
        {
            if (!_coreInitialized) { LibVLCSharp.Shared.Core.Initialize(); _coreInitialized = true; }

            _libVLC = new LibVLC();
            _player = new MediaPlayer(_libVLC);
            VideoView.MediaPlayer = _player;

            _stream = _session.OpenEntryStream(_entry);
            _media = new Media(_libVLC, new StreamMediaInput(_stream));
            _player.Play(_media);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += OnTick;
            _timer.Start();

            SeekBar.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, handledEventsToo: true);
            SeekBar.AddHandler(PointerReleasedEvent, (_, _) =>
            {
                if (_player != null) _player.Position = (float)(SeekBar.Value / 1000.0);
                _seeking = false;
            }, handledEventsToo: true);
        }
        catch (Exception ex)
        {
            InfoText.Text = $"Could not play this video: {ex.Message}";
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_player == null) return;
        if (!_seeking) SeekBar.Value = Math.Clamp(_player.Position * 1000.0, 0, 1000);
        long timeMs = _player.Time < 0 ? 0 : _player.Time;
        long lenMs = _player.Length < 0 ? 0 : _player.Length;
        PositionText.Text = Format(timeMs);
        DurationText.Text = Format(lenMs);
        PlayPauseButton.Content = _player.IsPlaying ? "Pause" : "Play";
    }

    private static string Format(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.Hours > 0 ? $"{ts.Hours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    private void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        if (_player.IsPlaying) _player.Pause();
        else _player.Play();
    }

    private void OnStop(object? sender, RoutedEventArgs e) => Close();

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_player != null) _player.Time = Math.Max(0, _player.Time - 10_000);
    }

    private void OnForward(object? sender, RoutedEventArgs e)
    {
        if (_player != null && _player.Length > 0) _player.Time = Math.Min(_player.Length, _player.Time + 10_000);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: OnPlayPause(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Left: OnBack(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Right: OnForward(this, new RoutedEventArgs()); e.Handled = true; break;
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
        _timer?.Stop();
        try
        {
            if (_player != null)
            {
                _player.Stop();
                VideoView.MediaPlayer = null;
                _player.Dispose();
            }
        }
        catch { /* best effort teardown */ }
        _media?.Dispose();
        _stream?.Dispose();
        _libVLC?.Dispose();
        _player = null;
        _media = null;
        _stream = null;
        _libVLC = null;
        base.OnClosed(e);
    }
}
