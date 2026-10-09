using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LaciaIsland.Model;

/// <summary>Wall clock that the headless renderer can replace with virtual time.</summary>
public static class Clock
{
    public static Func<DateTime> Provider = () => DateTime.UtcNow;
    public static DateTime UtcNow => Provider();
}

public sealed class MediaInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public Bitmap? Art { get; init; }
    public Color Accent { get; init; } = Colors.White;
    public bool IsPlaying { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime PositionStampUtc { get; init; }
    public bool CanPrev { get; init; } = true;
    public bool CanNext { get; init; } = true;
    public bool CanSeek { get; init; } = true;
    public string SourceApp { get; init; } = "";

    public string TrackKey => Title + "\u0001" + Artist;

    public TimeSpan LivePosition(DateTime nowUtc)
    {
        var p = Position;
        if (IsPlaying && PositionStampUtc != default) p += nowUtc - PositionStampUtc;
        if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
        return p < TimeSpan.Zero ? TimeSpan.Zero : p;
    }
}

public sealed class CaptureUsage
{
    public string AppName { get; init; } = "";
    public string? ExePath { get; init; }
    public DateTime StartUtc { get; init; }
    public bool Mic { get; init; }
    public bool Camera { get; init; }
    public Bitmap? Icon { get; init; }
    public string Key => (ExePath ?? AppName).ToLowerInvariant();
}

public sealed record AudioDeviceInfo(string Name, string Model, bool IsHeadphones, int? Battery);

public enum PowerEventKind { PluggedIn, Unplugged, Low }

public sealed record PowerEvent(PowerEventKind Kind, int Percent);
