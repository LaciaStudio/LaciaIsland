using Avalonia.Media.Imaging;
using LaciaIsland.Model;
using SkiaSharp;

namespace LaciaIsland.Services;

/// <summary>In-memory services used on non-Windows builds, by the headless renderer and by --demo.</summary>
public sealed class DemoMedia : IMediaService
{
    public MediaInfo? Current { get; private set; }
    public event Action? Changed;

    public void Set(MediaInfo? info) { Current = info; Changed?.Invoke(); }

    public void PlayPause()
    {
        if (Current is null) return;
        var now = Clock.UtcNow;
        Current = Clone(Current, playing: !Current.IsPlaying, position: Current.LivePosition(now), stamp: now);
        Changed?.Invoke();
    }

    public void Next() => Skip(+1);
    public void Previous() => Skip(-1);

    private int _index;
    public List<MediaInfo> Playlist { get; } = new();

    private void Skip(int d)
    {
        if (Playlist.Count == 0) return;
        _index = (_index + d + Playlist.Count) % Playlist.Count;
        var p = Playlist[_index];
        Current = Clone(p, playing: true, position: TimeSpan.Zero, stamp: Clock.UtcNow);
        Changed?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (Current is null) return;
        Current = Clone(Current, playing: Current.IsPlaying, position: position, stamp: Clock.UtcNow);
        Changed?.Invoke();
    }

    public void ActivateSource() { }
    public void Dispose() { }

    public static MediaInfo Clone(MediaInfo m, bool playing, TimeSpan position, DateTime stamp) => new()
    {
        Title = m.Title, Artist = m.Artist, Album = m.Album, Art = m.Art, Accent = m.Accent,
        IsPlaying = playing, Position = position, Duration = m.Duration, PositionStampUtc = stamp,
        CanNext = m.CanNext, CanPrev = m.CanPrev, CanSeek = m.CanSeek, SourceApp = m.SourceApp,
    };

    /// <summary>Procedural album covers so the demo never ships anyone's artwork.</summary>
    public static ArtProcessor.Result MakeArt(int variant)
    {
        const int size = 256;
        using var bmp = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        var palettes = new[]
        {
            new[] { new SKColor(0x1B, 0x10, 0x5E), new SKColor(0xE0, 0x3E, 0x8B), new SKColor(0xFF, 0x9A, 0x62) },
            new[] { new SKColor(0x06, 0x2B, 0x3A), new SKColor(0x1F, 0xB5, 0xA8), new SKColor(0xD8, 0xF5, 0x7A) },
            new[] { new SKColor(0x2A, 0x0C, 0x0C), new SKColor(0xE8, 0x4A, 0x27), new SKColor(0xFF, 0xD3, 0x6B) },
        };
        var pal = palettes[variant % palettes.Length];
        using (var bg = new SKPaint())
        {
            bg.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size), new[] { pal[0], pal[0].WithAlpha(255) }, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, size, size, bg);
        }
        using (var blob = new SKPaint { IsAntialias = true })
        {
            blob.Shader = SKShader.CreateRadialGradient(new SKPoint(size * 0.62f, size * 0.42f), size * 0.55f,
                new[] { pal[2], pal[1], pal[0].WithAlpha(0) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawCircle(size * 0.58f, size * 0.5f, size * 0.52f, blob);
        }
        using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6, Color = pal[2].WithAlpha(150) })
            canvas.DrawCircle(size * 0.3f, size * 0.72f, size * 0.16f, ring);
        canvas.Flush();
        return ArtProcessor.Process(bmp, 192);
    }
}

public sealed class DemoAudio : IAudioService
{
    public double Volume { get; private set; } = 0.45;
    public bool Muted { get; private set; }
    public event Action? VolumeChanged;
    public void SetVolume(double v) { Volume = Math.Clamp(v, 0, 1); Muted = false; VolumeChanged?.Invoke(); }
    public void ToggleMute() { Muted = !Muted; VolumeChanged?.Invoke(); }
    public bool VisualizerEnabled { get; set; }
    public bool HasSpectrum => false;
    public bool ReadBands(float[] bands) => false;
    public AudioDeviceInfo? Device { get; private set; } = new("Динамики (Realtek)", "Realtek", false, null);
    public event Action<AudioDeviceInfo>? DefaultDeviceChanged;
    public void RaiseDevice(AudioDeviceInfo d) { Device = d; DefaultDeviceChanged?.Invoke(d); }
    public bool MicMonitoring { get; set; }
    public double MicLevel
    {
        get
        {
            var t = (Clock.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            var syll = Math.Max(0, Math.Sin(t * 7.3)) * (0.55 + 0.45 * Core.M.Noise(t * 3.1));
            var phrase = Core.M.Noise(t * 0.7) > 0.35 ? 1 : 0.15;
            return Math.Clamp(syll * phrase, 0, 1);
        }
    }
    public bool MicMuted { get; private set; }
    public void ToggleMicMute() => MicMuted = !MicMuted;
    public void OpenSoundSettings() { }
    public void Dispose() { }
}

public sealed class DemoPrivacy : IPrivacyService
{
    private readonly List<CaptureUsage> _active = new();
    public IReadOnlyList<CaptureUsage> Active => _active;
    public event Action? Changed;
    public void Set(params CaptureUsage[] usages)
    {
        _active.Clear();
        _active.AddRange(usages);
        Changed?.Invoke();
    }
    public void Dispose() { }
}

public sealed class DemoPower : IPowerService
{
    public bool HasBattery => true;
    public int Percent { get; set; } = 54;
    public bool Charging { get; set; }
    public event Action<PowerEvent>? Event;
    public void Raise(PowerEventKind k)
    {
        Charging = k == PowerEventKind.PluggedIn;
        Event?.Invoke(new PowerEvent(k, Percent));
    }
    public void Dispose() { }
}

public sealed class DemoKeyboard : IKeyboardService
{
    public string Current { get; private set; } = "EN";
    public event Action<string>? LayoutChanged;
    public void Raise(string code) { Current = code; LayoutChanged?.Invoke(code); }
    public void Dispose() { }
}

public sealed class DemoSystem : ISystemService
{
    public bool Fullscreen { get; set; }
    public bool IsFullscreenAppActive() => Fullscreen;
    public void OpenUri(string uri) => Log.Info("open " + uri);
    public void PlayAlert() { }
    public bool AutostartEnabled { get; set; }
    public void BringToFront(string? exePathOrApp) { }
}

public static class DemoHub
{
    public static ServiceHub Create() => new()
    {
        Media = new DemoMedia(),
        Audio = new DemoAudio(),
        Privacy = new DemoPrivacy(),
        Power = new DemoPower(),
        Keyboard = new DemoKeyboard(),
        System = new DemoSystem(),
    };

    public static List<MediaInfo> SampleTracks()
    {
        var a0 = DemoMedia.MakeArt(0);
        var a1 = DemoMedia.MakeArt(1);
        var a2 = DemoMedia.MakeArt(2);
        return new()
        {
            new MediaInfo { Title = "Ночной город", Artist = "Lacia Demo", Art = a0.Bitmap, Accent = a0.Accent, Duration = TimeSpan.FromSeconds(221), Position = TimeSpan.FromSeconds(170), SourceApp = "Demo" },
            new MediaInfo { Title = "Северное сияние", Artist = "Aurora Lines", Art = a1.Bitmap, Accent = a1.Accent, Duration = TimeSpan.FromSeconds(187), SourceApp = "Demo" },
            new MediaInfo { Title = "Оранжевый закат", Artist = "Sunset Drive", Art = a2.Bitmap, Accent = a2.Accent, Duration = TimeSpan.FromSeconds(204), SourceApp = "Demo" },
        };
    }
}
