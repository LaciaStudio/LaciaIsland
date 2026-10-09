using Avalonia.Threading;
using LaciaIsland.Model;
using LaciaIsland.UI;

namespace LaciaIsland.Services;

/// <summary>Scripted showcase used by --demo (and on non-Windows builds).</summary>
public sealed class DemoDirector
{
    private readonly ServiceHub _hub;
    private readonly IslandController _c;
    private readonly List<(double At, Action Do)> _script = new();
    private double _t;
    private int _next;
    private DispatcherTimer? _timer;

    public DemoDirector(ServiceHub hub, IslandController c)
    {
        _hub = hub;
        _c = c;
        Build();
    }

    private DemoMedia Media => (DemoMedia)_hub.Media;
    private DemoAudio Audio => (DemoAudio)_hub.Audio;
    private DemoPrivacy Privacy => (DemoPrivacy)_hub.Privacy;
    private DemoPower Power => (DemoPower)_hub.Power;
    private DemoKeyboard Keyboard => (DemoKeyboard)_hub.Keyboard;

    private void Build()
    {
        var tracks = DemoHub.SampleTracks();
        Media.Playlist.AddRange(tracks);
        void At(double t, Action a) => _script.Add((t, a));

        At(1.0, () => Media.Set(DemoMedia.Clone(tracks[0], true, tracks[0].Position, Clock.UtcNow)));
        At(4.5, () => Audio.SetVolume(0.52));
        At(4.8, () => Audio.SetVolume(0.6));
        At(5.1, () => Audio.SetVolume(0.66));
        At(8.0, () => _hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)));
        At(13.0, () => Keyboard.Raise("RU"));
        At(16.0, () => Privacy.Set(new CaptureUsage { AppName = "Discord", StartUtc = Clock.UtcNow, Mic = true }));
        At(24.0, () => Privacy.Set());
        At(27.0, () => Power.Raise(PowerEventKind.PluggedIn));
        At(31.0, () => Audio.RaiseDevice(new AudioDeviceInfo("Наушники (AirPods Pro)", "AirPods Pro", true, 80)));
        At(35.0, () => _hub.Timer.Cancel());
        At(36.0, () => Media.Next());
        At(42.0, () => Media.PlayPause());
        At(60.0, () => { });
    }

    public void Start()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick(0.1));
        _timer.Start();
    }

    private void Tick(double dt)
    {
        _t += dt;
        while (_next < _script.Count && _script[_next].At <= _t)
        {
            try { _script[_next].Do(); } catch (Exception ex) { Log.Error("demo", ex); }
            _next++;
        }
        if (_next >= _script.Count)
        {
            _t = 0;
            _next = 0;
        }
    }
}
