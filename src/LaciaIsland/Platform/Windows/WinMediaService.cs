using Avalonia.Media;
using Avalonia.Threading;
using LaciaIsland.Model;
using LaciaIsland.Services;
using Windows.Storage.Streams;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace LaciaIsland.Platform.Windows;

/// <summary>
/// Now playing via Windows "System Media Transport Controls" — the same source as the media flyout
/// next to the volume slider (Spotify, Yandex Music, browsers, AIMP, Media Player…).
///
/// All SMTC calls run on a background worker (they are cross-process and can be slow), refreshes are
/// strictly sequential (a stale result can never overwrite a newer one), and the cover is re-read on every
/// properties change and compared by content hash — players often publish the new title first and the new
/// cover a moment later, with the same title.
/// </summary>
internal sealed class WinMediaService : IMediaService
{
    private GsmtcManager? _mgr;
    private GsmtcSession? _session;
    private readonly List<GsmtcSession> _subscribed = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _flagLock = new();
    private bool _dirty;
    private bool _workerRunning;
    private volatile bool _resubscribe;
    private volatile bool _propsDirty = true;
    private volatile bool _disposed;

    private string _trackKey = "";
    private ulong _artHash;
    private ArtProcessor.Result? _art;

    public MediaInfo? Current { get; private set; }
    public event Action? Changed;

    public WinMediaService()
    {
        _ = Task.Run(InitAsync);
    }

    private async Task InitAsync()
    {
        try
        {
            _mgr = await GsmtcManager.RequestAsync();
            _mgr.SessionsChanged += (_, _) => Queue(resubscribe: true, props: true);
            _mgr.CurrentSessionChanged += (_, _) => Queue(props: true);
            Queue(resubscribe: true, props: true);
            Log.Info("SMTC ready");
        }
        catch (Exception ex) { Log.Error("smtc init", ex); }
    }

    /// <summary>Marks state dirty and makes sure one (and only one) worker drains it.</summary>
    private void Queue(bool resubscribe = false, bool props = false, int delayMs = 40)
    {
        if (_disposed) return;
        if (resubscribe) _resubscribe = true;
        if (props) _propsDirty = true;
        lock (_flagLock)
        {
            _dirty = true;
            if (_workerRunning) return;
            _workerRunning = true;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs); // coalesce bursts of events
            while (true)
            {
                lock (_flagLock)
                {
                    if (!_dirty || _disposed)
                    {
                        _workerRunning = false;
                        return;
                    }
                    _dirty = false;
                }
                await RefreshAsync();
            }
        });
    }

    private void QueueLater(int ms, bool props)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(ms);
            Queue(props: props, delayMs: 0);
        });
    }

    private void Resubscribe()
    {
        foreach (var s in _subscribed)
        {
            try
            {
                s.PlaybackInfoChanged -= OnPlayback;
                s.MediaPropertiesChanged -= OnProps;
                s.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { /* session gone */ }
        }
        _subscribed.Clear();
        if (_mgr is null) return;
        foreach (var s in _mgr.GetSessions())
        {
            try
            {
                s.PlaybackInfoChanged += OnPlayback;
                s.MediaPropertiesChanged += OnProps;
                s.TimelinePropertiesChanged += OnTimeline;
                _subscribed.Add(s);
            }
            catch (Exception ex) { Log.Error("smtc subscribe", ex); }
        }
    }

    private void OnPlayback(GsmtcSession s, object a) => Queue();
    private void OnTimeline(GsmtcSession s, object a) => Queue();
    private void OnProps(GsmtcSession s, object a) => Queue(props: true);

    private async Task RefreshAsync()
    {
        if (_disposed || _mgr is null) return;
        await _gate.WaitAsync();
        try
        {
            if (_resubscribe)
            {
                _resubscribe = false;
                Resubscribe();
            }

            var sessions = _mgr.GetSessions();
            var current = _mgr.GetCurrentSession();
            GsmtcSession? chosen = null;
            foreach (var s in sessions)
            {
                if (s.GetPlaybackInfo()?.PlaybackStatus != PlaybackStatus.Playing) continue;
                chosen = s;
                if (current is not null && s.SourceAppUserModelId == current.SourceAppUserModelId) break;
            }
            // keep showing the last session while it is merely paused
            if (chosen is null && _session is not null)
                chosen = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _session.SourceAppUserModelId);
            chosen ??= current ?? sessions.FirstOrDefault();

            if (!ReferenceEquals(chosen, _session) && chosen?.SourceAppUserModelId != _session?.SourceAppUserModelId)
                _propsDirty = true;
            _session = chosen;

            if (chosen is null)
            {
                Publish(null);
                return;
            }

            var readProps = _propsDirty;
            _propsDirty = false;
            var props = await chosen.TryGetMediaPropertiesAsync();
            var pb = chosen.GetPlaybackInfo();
            var tl = chosen.GetTimelineProperties();

            var title = props?.Title ?? "";
            var artist = string.IsNullOrWhiteSpace(props?.Artist) ? props?.AlbumArtist ?? "" : props!.Artist;
            var key = $"{chosen.SourceAppUserModelId}|{title}|{artist}|{props?.AlbumTitle}";
            var trackChanged = key != _trackKey;
            if (trackChanged)
            {
                _trackKey = key;
                // the player may publish the new cover a bit later without another event — look again
                QueueLater(700, true);
                QueueLater(2000, true);
                QueueLater(4500, true);
            }

            if (trackChanged || readProps)
            {
                var bytes = props?.Thumbnail is not null ? await ReadThumbnailAsync(props.Thumbnail) : null;
                var hash = bytes is null ? 0UL : Fnv1a(bytes);
                if (hash != _artHash || (bytes is null && _art is not null && trackChanged))
                {
                    _artHash = hash;
                    _art = bytes is null ? null : ArtProcessor.Process(bytes);
                }
            }

            var duration = tl is null ? TimeSpan.Zero : tl.EndTime - tl.StartTime;
            var position = tl is null ? TimeSpan.Zero : tl.Position - tl.StartTime;
            var stamp = tl?.LastUpdatedTime.UtcDateTime ?? DateTime.UtcNow;
            if (stamp.Year < 2000 || stamp > DateTime.UtcNow.AddMinutes(1)) stamp = DateTime.UtcNow;

            Publish(new MediaInfo
            {
                Title = title,
                Artist = artist,
                Album = props?.AlbumTitle ?? "",
                Art = _art?.Bitmap,
                Accent = _art?.Accent ?? Color.FromRgb(0xE8, 0xE8, 0xED),
                IsPlaying = pb?.PlaybackStatus == PlaybackStatus.Playing,
                Position = position,
                Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
                PositionStampUtc = stamp,
                CanNext = pb?.Controls.IsNextEnabled ?? true,
                CanPrev = pb?.Controls.IsPreviousEnabled ?? true,
                CanSeek = pb?.Controls.IsPlaybackPositionEnabled ?? false,
                SourceApp = chosen.SourceAppUserModelId ?? "",
            });
        }
        catch (Exception ex)
        {
            Log.Error("smtc refresh", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Publish(MediaInfo? info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (info is null && Current is null) return;
            Current = info;
            Changed?.Invoke();
        });
    }

    private static ulong Fnv1a(byte[] data)
    {
        var h = 14695981039346656037UL;
        foreach (var b in data)
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h ^ (ulong)data.Length;
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 20_000_000) return null;
            using var reader = new DataReader(stream);
            var size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            Log.Error("smtc art", ex);
            return null;
        }
    }

    private void Control(Func<GsmtcSession, global::Windows.Foundation.IAsyncOperation<bool>> op)
    {
        var s = _session;
        if (s is null) return;
        _ = Task.Run(async () =>
        {
            try { await op(s); }
            catch (Exception ex) { Log.Error("smtc control", ex); }
            Queue(props: true);
        });
    }

    public void PlayPause() => Control(s => s.TryTogglePlayPauseAsync());
    public void Next() => Control(s => s.TrySkipNextAsync());
    public void Previous() => Control(s => s.TrySkipPreviousAsync());

    public void Seek(TimeSpan position)
    {
        var s = _session;
        if (s is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var start = s.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
                await s.TryChangePlaybackPositionAsync((start + position).Ticks);
            }
            catch (Exception ex) { Log.Error("smtc seek", ex); }
            Queue();
        });
    }

    public void ActivateSource()
    {
        var id = _session?.SourceAppUserModelId;
        if (string.IsNullOrEmpty(id)) return;
        new WinSystemService().BringToFront(id);
    }

    public void Dispose()
    {
        _disposed = true;
        _subscribed.Clear();
    }
}
