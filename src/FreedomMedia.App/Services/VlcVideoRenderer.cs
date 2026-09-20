using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace FreedomMedia.App.Services;

/// <summary>
/// Renders LibVLC video into an Avalonia <see cref="Image"/> through LibVLC's own frame callbacks,
/// rather than the native-window VideoView. LibVLC decodes each frame into a buffer we own; we copy
/// it into a WriteableBitmap that an ordinary Image control displays. This avoids embedding a native
/// child window inside Avalonia (the source of the earlier crash), works identically on Windows and
/// macOS, and lets the on-screen controls sit above the video with no "airspace" problem.
/// </summary>
public sealed unsafe class VlcVideoRenderer : IDisposable
{
    private readonly Image _target;
    private readonly int _visibleWidth;
    private readonly int _visibleHeight;

    // Delegates are held in fields so the GC cannot collect them while native code holds pointers.
    private readonly MediaPlayer.LibVLCVideoFormatCb _formatCb;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    private readonly object _sync = new();
    private IntPtr _buffer;
    private uint _pitch;              // bytes per row of the LibVLC buffer (aligned width * 4)
    private int _displayWidth;        // pixels actually shown (video's real size)
    private int _displayHeight;
    private WriteableBitmap? _bitmap;
    private bool _disposed;

    /// <param name="visibleWidth">The video's real pixel width (0 if unknown), used to crop the
    /// alignment padding LibVLC adds to its decode buffer. Same for height.</param>
    public VlcVideoRenderer(Image target, int visibleWidth, int visibleHeight)
    {
        _target = target;
        _visibleWidth = visibleWidth;
        _visibleHeight = visibleHeight;
        _formatCb = OnFormat;
        _cleanupCb = OnCleanup;
        _lockCb = OnLock;
        _displayCb = OnDisplay;
    }

    public void Attach(MediaPlayer player)
    {
        player.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
        player.SetVideoCallbacks(_lockCb, null, _displayCb);
    }

    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // Ask LibVLC for BGRA (little-endian "RV32"): four bytes B, G, R, X per pixel.
        var fourcc = Encoding.ASCII.GetBytes("RV32");
        Marshal.Copy(fourcc, 0, chroma, 4);

        uint pitch = width * 4;
        pitches = pitch;
        lines = height;

        // LibVLC pads the decode buffer up to alignment boundaries (e.g. 1080 -> 1090 rows), so we
        // show only the video's real size where known, cropping the padding.
        int dispW = _visibleWidth > 0 && _visibleWidth <= (int)width ? _visibleWidth : (int)width;
        int dispH = _visibleHeight > 0 && _visibleHeight <= (int)height ? _visibleHeight : (int)height;

        lock (_sync)
        {
            _pitch = pitch;
            _displayWidth = dispW;
            _displayHeight = dispH;
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal(checked((int)(pitch * height)));
        }

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_bitmap == null || _bitmap.PixelSize.Width != dispW || _bitmap.PixelSize.Height != dispH)
            {
                _bitmap = new WriteableBitmap(new PixelSize(dispW, dispH),
                    new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                _target.Source = _bitmap;
            }
        }).Wait();

        AppLog.Write($"video format: buffer {width}x{height} shown {dispW}x{dispH} pitch={pitch}");
        return 1; // one plane
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, 0, _buffer);
        return _buffer;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        WriteableBitmap? bmp = _bitmap;
        if (bmp == null) return;

        lock (_sync)
        {
            if (_buffer == IntPtr.Zero) return;
            using var fb = bmp.Lock();
            byte* src = (byte*)_buffer;
            byte* dst = (byte*)fb.Address;
            int srcPitch = (int)_pitch;
            int dstPitch = fb.RowBytes;
            int rowBytes = Math.Min(Math.Min(srcPitch, dstPitch), _displayWidth * 4);
            int rows = Math.Min(_displayHeight, fb.Size.Height);
            for (int y = 0; y < rows; y++)
                Buffer.MemoryCopy(src + (long)y * srcPitch, dst + (long)y * dstPitch, dstPitch, rowBytes);
        }

        Dispatcher.UIThread.Post(() => _target.InvalidateVisual(), DispatcherPriority.Render);
    }

    private void OnCleanup(ref IntPtr opaque) => FreeBuffer();

    private void FreeBuffer()
    {
        lock (_sync)
        {
            if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FreeBuffer();
    }
}
