using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FreedomMedia.App.Models;
using FreedomMedia.Core;

namespace FreedomMedia.App.Services;

/// <summary>
/// Produces gallery thumbnails for image entries by decrypting them into memory and decoding at a
/// reduced resolution. Nothing is written to disk. Video entries get no bitmap here (the gallery
/// shows a film placeholder for them); extracting a video frame cross-platform is left to a later
/// release. Work is throttled so a large vault does not saturate the machine, and each request is
/// tied to a generation token so switching or closing a vault cancels stale work.
/// </summary>
public sealed class ThumbnailService
{
    private readonly SemaphoreSlim _gate = new(3);
    private int _generation;

    public const int ThumbnailWidth = 320;

    /// <summary>Invalidate all in-flight and future work from before this call (e.g. on vault close).</summary>
    public void Reset() => Interlocked.Increment(ref _generation);

    /// <summary>Kick off background thumbnail loading for the given items against the current vault.
    /// Safe to call again; already-loaded or already-requested items are skipped.</summary>
    public void QueueAll(IEnumerable<MediaItemViewModel> items, VaultSession session)
    {
        int generation = _generation;
        foreach (var item in items)
        {
            if (item.ThumbnailRequested || item.Thumbnail != null || item.Entry.IsVideo) continue;
            item.ThumbnailRequested = true;
            _ = LoadAsync(item, session, generation);
        }
    }

    private async Task LoadAsync(MediaItemViewModel item, VaultSession session, int generation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != _generation) return;
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    using var stream = session.OpenEntryStream(item.Entry);
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    return Bitmap.DecodeToWidth(ms, ThumbnailWidth);
                }
                catch
                {
                    return null;
                }
            }).ConfigureAwait(false);

            if (bitmap != null && generation == _generation)
                await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bitmap);
            else
                bitmap?.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }
}
