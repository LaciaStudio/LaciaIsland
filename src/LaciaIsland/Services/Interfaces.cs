using LaciaIsland.Model;

namespace LaciaIsland.Services;

// All events are raised on the UI thread.

public interface IMediaService : IDisposable
{
    MediaInfo? Current { get; }
    event Action? Changed;
    void PlayPause();
    void Next();
    void Previous();
    void Seek(TimeSpan position);
    void ActivateSource();
}

public interface IAudioService : IDisposable
{
    double Volume { get; }
    bool Muted { get; }
    /// <summary>Volume or mute changed (keys, mixer, our own wheel).</summary>
    event Action? VolumeChanged;
    void SetVolume(double v);
    void ToggleMute();

    /// <summary>Turns the loopback spectrum capture on/off.</summary>
    bool VisualizerEnabled { get; set; }
    /// <summary>True when real spectrum data is flowing.</summary>
    bool HasSpectrum { get; }
    /// <summary>Fills normalised band levels; returns false when no real data is available.</summary>
    bool ReadBands(float[] bands);

    AudioDeviceInfo? Device { get; }
    event Action<AudioDeviceInfo>? DefaultDeviceChanged;

    bool MicMonitoring { get; set; }
    double MicLevel { get; }
    bool MicMuted { get; }
    void ToggleMicMute();
    void OpenSoundSettings();
}

public interface IPrivacyService : IDisposable
{
    IReadOnlyList<CaptureUsage> Active { get; }
    event Action? Changed;
}

public interface IPowerService : IDisposable
{
    bool HasBattery { get; }
    int Percent { get; }
    bool Charging { get; }
    event Action<PowerEvent>? Event;
}

public interface IKeyboardService : IDisposable
{
    string Current { get; }
    event Action<string>? LayoutChanged;
}

/// <summary>OS integration that does not fit elsewhere.</summary>
public interface ISystemService
{
    bool IsFullscreenAppActive();
    void OpenUri(string uri);
    void PlayAlert();
    bool AutostartEnabled { get; set; }
    void BringToFront(string? exePathOrApp);
}

public sealed class ServiceHub : IDisposable
{
    public required IMediaService Media { get; init; }
    public required IAudioService Audio { get; init; }
    public required IPrivacyService Privacy { get; init; }
    public required IPowerService Power { get; init; }
    public required IKeyboardService Keyboard { get; init; }
    public required ISystemService System { get; init; }
    public TimerService Timer { get; } = new();

    public void Dispose()
    {
        foreach (var d in new IDisposable[] { Media, Audio, Privacy, Power, Keyboard })
        {
            try { d.Dispose(); } catch (Exception ex) { Log.Error("dispose", ex); }
        }
    }
}
