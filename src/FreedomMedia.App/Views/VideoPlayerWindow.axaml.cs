using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FreedomMedia.App.Models;
using FreedomMedia.App.Services;
using FreedomMedia.Core;
using LibVLCSharp.Shared;

namespace FreedomMedia.App.Views;

public partial class VideoPlayerWindow : Window
{
    private static bool _coreInitialized;

    private static readonly Geometry IconPlay = Geometry.Parse("M8 5v14l11-7z");
    private static readonly Geometry IconPause = Geometry.Parse("M6 5h4v14H6zM14 5h4v14h-4z");
    private static readonly Geometry IconEnterFullscreen = Geometry.Parse("M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z");
    private static readonly Geometry IconExitFullscreen = Geometry.Parse("M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z");

    private readonly VaultSession? _session;
    private readonly VaultEntry? _entry;
    private LibVLC? _libVLC;
    private MediaPlayer? _player;
    private VaultEntryStream? _stream;
    private Media? _media;
    private VlcVideoRenderer? _renderer;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _hideTimer;
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
            AppLog.Write($"video: starting '{_entry.Title}' ({_entry.OriginalFileName})");
            if (!_coreInitialized) { LibVLCSharp.Shared.Core.Initialize(); _coreInitialized = true; AppLog.Write("video: LibVLC Core.Initialize ok"); }

            _libVLC = new LibVLC();
            _player = new MediaPlayer(_libVLC);

            _renderer = new VlcVideoRenderer(VideoImage, _entry.Width, _entry.Height);
            _renderer.Attach(_player);
            AppLog.Write("video: renderer attached");

            _stream = _session.OpenEntryStream(_entry);
            _media = new Media(_libVLC, new StreamMediaInput(_stream));
            _player.Play(_media);
            AppLog.Write("video: Play() returned");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += OnTick;
            _timer.Start();

            // Auto-hide the control bar 3 seconds after the last mouse movement (while playing).
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideTimer.Tick += (_, _) => HideControls();

            SeekBar.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, handledEventsToo: true);
            SeekBar.AddHandler(PointerReleasedEvent, (_, _) =>
            {
                if (_player != null) _player.Position = (float)(SeekBar.Value / 1000.0);
                _seeking = false;
            }, handledEventsToo: true);

            ShowControls();
        }
        catch (Exception ex)
        {
            AppLog.Exception("VideoPlayerWindow.Start", ex);
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
        PlayPauseIcon.Data = _player.IsPlaying ? IconPause : IconPlay;
    }

    private static string Format(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.Hours > 0 ? $"{ts.Hours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    // ===== Control-bar visibility =====

    private void ShowControls()
    {
        ControlBar.IsVisible = true;
        Cursor = Cursor.Default;
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    private void HideControls()
    {
        _hideTimer?.Stop();
        // Keep the controls up while paused, so the user always has them when the video is stopped.
        if (_player != null && !_player.IsPlaying) return;
        ControlBar.IsVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ShowControls();
        base.OnPointerMoved(e);
    }

    // ===== Transport =====

    private void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        if (_player.IsPlaying) _player.Pause();
        else _player.Play();
        PlayPauseIcon.Data = _player.IsPlaying ? IconPause : IconPlay;
        ShowControls();
    }

    private void OnStop(object? sender, RoutedEventArgs e) => Close();

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_player != null) _player.Time = Math.Max(0, _player.Time - 10_000);
        ShowControls();
    }

    private void OnForward(object? sender, RoutedEventArgs e)
    {
        if (_player != null && _player.Length > 0) _player.Time = Math.Min(_player.Length, _player.Time + 10_000);
        ShowControls();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // ===== Fullscreen =====

    private void OnFullscreen(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnVideoDoubleTapped(object? sender, TappedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        bool goingFull = WindowState != WindowState.FullScreen;
        WindowState = goingFull ? WindowState.FullScreen : WindowState.Normal;
        FullscreenIcon.Data = goingFull ? IconExitFullscreen : IconEnterFullscreen;
        ToolTip.SetTip(FullscreenButton, goingFull ? "Exit full screen" : "Full screen");
        ShowControls();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: OnPlayPause(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Left: OnBack(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Right: OnForward(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F or Key.F11: ToggleFullscreen(); e.Handled = true; break;
            case Key.Escape:
                if (WindowState == WindowState.FullScreen) ToggleFullscreen();
                else Close();
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        _hideTimer?.Stop();
        try
        {
            if (_player != null)
            {
                _player.Stop();
                _player.Dispose();
            }
        }
        catch (Exception ex) { AppLog.Exception("VideoPlayerWindow teardown", ex); }
        _renderer?.Dispose();
        _media?.Dispose();
        _stream?.Dispose();
        _libVLC?.Dispose();
        _player = null;
        _media = null;
        _stream = null;
        _libVLC = null;
        _renderer = null;
        base.OnClosed(e);
    }
}
