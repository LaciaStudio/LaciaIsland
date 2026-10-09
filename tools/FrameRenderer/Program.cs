using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.Services;
using LaciaIsland.UI;

namespace FrameRenderer;

public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}

public sealed class Ctx
{
    public required IslandController C;
    public required ServiceHub Hub;
    public required List<MediaInfo> Tracks;
    public DemoMedia Media => (DemoMedia)Hub.Media;
    public DemoAudio Audio => (DemoAudio)Hub.Audio;
    public DemoPrivacy Privacy => (DemoPrivacy)Hub.Privacy;
    public DemoPower Power => (DemoPower)Hub.Power;
    public DemoKeyboard Keyboard => (DemoKeyboard)Hub.Keyboard;
    public Point Center => C.Engine.Frame.Visible.Center;
    public Point BubbleCenter => C.Engine.Frame.BubbleCenter;
    public Point At(double dx, double fromTop) => new(C.Engine.Frame.Visible.Center.X + dx, C.Engine.Frame.Visible.Top + fromTop);

    public void Play(int i = 0, double? pos = null) =>
        Media.Set(DemoMedia.Clone(Tracks[i], true, TimeSpan.FromSeconds(pos ?? Tracks[i].Position.TotalSeconds), Clock.UtcNow));
    public void Hover(Point? p = null) => C.OnCursor(p ?? Center);
    public void Leave() => C.OnCursorLeft();
    public void Press(Point? p = null, MouseButton b = MouseButton.Left) => C.OnPress(p ?? Center, b);
    public void Release(Point? p = null, MouseButton b = MouseButton.Left) => C.OnRelease(p ?? Center, b);
    public void Call(string app = "Discord") => Privacy.Set(new CaptureUsage { AppName = app, StartUtc = Clock.UtcNow.AddSeconds(-7), Mic = true });
}

public sealed record Scenario(string Name, string Wallpaper, double Duration, Action<Ctx>? Setup, params (double At, Action<Ctx> Do)[] Steps)
{
    public double Preroll { get; init; } = 1.0;
    public int Every { get; init; } = 1;
    public IslandMode Mode { get; init; } = IslandMode.Island;
    public double Length { get; init; } = 1.0;
    /// <summary>Save only the last frame.</summary>
    public bool OnlyLast { get; init; }
}

/// <summary>
/// Off-screen renderer: plays scripted scenarios on a virtual clock and saves frames as PNG.
/// Used for visual checks and for the GIFs / pictures in docs/.
///   dotnet run --project tools/FrameRenderer -c Release -- &lt;outDir&gt; [scenario1,scenario2,...]
///   LI_ZOOM=3  — render at 3x (default 2x);  LI_WALL=white|black — backdrop for "none" scenarios.
/// </summary>
public static class Program
{
    private static readonly DateTime T0 = new(2026, 10, 9, 15, 36, 0, DateTimeKind.Utc);
    private static double _vt;
    private const double Dt = 1 / 60.0;
    private static readonly double Zoom = double.TryParse(Environment.GetEnvironmentVariable("LI_ZOOM"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z) ? z : 2;

    public static int Main(string[] args)
    {
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();
        Clock.Provider = () => T0.AddSeconds(_vt);

        if (args.Length >= 2 && args[0] == "--fonts")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var name in new[] { "Inter-Light", "Inter-Regular", "Inter-Medium", "Inter-SemiBold", "Inter-Bold" })
            {
                using var src = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{name}.ttf"));
                using var dst = File.Create(Path.Combine(args[1], name + ".ttf"));
                src.CopyTo(dst);
            }
            return 0;
        }

        var outDir = args.Length > 0 ? args[0] : "out";
        var only = args.Length > 1 ? args[1].Split(',') : null;
        var tracks = DemoHub.SampleTracks();

        foreach (var sc in Scenarios(tracks))
        {
            if (only is not null && !only.Contains(sc.Name)) continue;
            Run(sc, Path.Combine(outDir, sc.Name), tracks);
        }
        return 0;
    }

    private static void Run(Scenario sc, string dir, List<MediaInfo> tracks)
    {
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        _vt = 0;

        var settings = new AppSettings { Mode = sc.Mode, Length = sc.Length };
        var hub = DemoHub.Create();
        var c = new IslandController(settings, hub);
        var ctx = new Ctx { C = c, Hub = hub, Tracks = tracks };

        var size = c.WindowSize;
        var root = new Panel { Width = size.Width, Height = size.Height };
        var wall = sc.Wallpaper == "none" ? Environment.GetEnvironmentVariable("LI_WALL") ?? "none" : sc.Wallpaper;
        if (wall != "none")
        {
            var wp = Path.Combine(AppContext.BaseDirectory, "wallpapers", wall + ".png");
            root.Children.Add(new Image { Source = new Bitmap(wp), Stretch = Stretch.UniformToFill });
        }
        root.Children.Add(c.View);
        var win = new Window
        {
            Width = size.Width * Zoom,
            Height = size.Height * Zoom,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(Zoom, Zoom), Child = root },
        };
        win.Show();

        sc.Setup?.Invoke(ctx);
        for (var t = 0.0; t < sc.Preroll; t += Dt)
        {
            _vt += Dt;
            c.Update(Dt);
        }

        var steps = sc.Steps.OrderBy(s => s.At).ToList();
        var next = 0;
        var frames = (int)Math.Round(sc.Duration / Dt);
        var saved = 0;
        using var csv = new StreamWriter(Path.Combine(dir, "engine.csv"));
        csv.WriteLine("i,t,W,H,R,Vw,Vh,scale,bodyW,bodyH,bx,br,attach");
        for (var i = 0; i <= frames; i++)
        {
            var t = i * Dt;
            while (next < steps.Count && steps[next].At <= t + 1e-9)
            {
                steps[next].Do(ctx);
                next++;
            }
            // The real host resizes and re-centres its window when Length changes; this capture window
            // has a fixed size, so keep the island centred in it instead.
            c.Engine.CenterX = size.Width / 2;
            _vt += Dt;
            c.Update(Dt);
            var e = c.Engine; var fr = e.Frame;
            csv.WriteLine(FormattableString.Invariant($"{i},{t:0.000},{e.W.Value:0.0},{e.H.Value:0.0},{e.R.Value:0.0},{e.W.Velocity:0},{e.H.Velocity:0},{e.Scale.Value:0.000},{fr.Body.Width:0.0},{fr.Visible.Height:0.0},{fr.BubbleCenter.X:0.0},{fr.BubbleRadius:0.0},{e.Attach.Value:0.00}"));
            if (sc.OnlyLast ? i != frames : i % sc.Every != 0) continue;
            Dispatcher.UIThread.RunJobs();
            var bmp = win.CaptureRenderedFrame();
            bmp?.Save(Path.Combine(dir, $"f_{i:0000}.png"));
            saved++;
        }
        win.Close();
        hub.Dispose();
        Console.WriteLine($"{sc.Name}: {saved} frames → {dir}   [{c.Describe()}]");
    }

    private static IEnumerable<Scenario> Scenarios(List<MediaInfo> tracks)
    {
        // ── stills of every state (one settled frame each)
        Scenario Still(string name, string wp, Action<Ctx> setup, IslandMode mode = IslandMode.Island, double settle = 1.6, double length = 1.0) =>
            new(name, wp, 0, setup) { Preroll = settle, Mode = mode, Length = length };

        // ── media for README / posters (transparent cut-outs; render with LI_ZOOM=3)
        Scenario Cut(string name, Action<Ctx> setup, IslandMode mode = IslandMode.Island, double settle = 1.6, double length = 1.0) =>
            new(name, "none", 0, setup) { Preroll = settle, Mode = mode, Length = length };
        void Expand(Ctx x) { x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); x.Leave(); x.Hover(x.At(0, 20)); }
        yield return Cut("m_idle", _ => { });
        yield return Cut("m_music", x => x.Play());
        yield return Cut("m_music_exp", x => { x.Play(); Expand(x); });
        yield return new Scenario("m_split", "none", 2.6, x => x.Play(), (1.0, x => x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)))) { OnlyLast = true };
        yield return new Scenario("m_drip", "none", 1.0 + 8 / 60.0, x => x.Play(), (1.0, x => x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)))) { OnlyLast = true };
        yield return new Scenario("m_split_call", "none", 2.6, x => x.Play(), (1.0, x => x.Call())) { OnlyLast = true };
        yield return Cut("m_call", x => x.Call());
        yield return Cut("m_call_exp", x => { x.Call(); Expand(x); });
        yield return Cut("m_timer", x => x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(25)));
        yield return Cut("m_timer_exp", x => { x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(25)); Expand(x); });
        yield return Cut("m_alert", x => x.Hub.Timer.StartCountdown(TimeSpan.FromSeconds(0.5)), settle: 1.4);
        yield return Cut("m_volume", x => x.Audio.SetVolume(0.62), settle: 1.0);
        yield return Cut("m_layout", x => x.Keyboard.Raise("RU"), settle: 0.9);
        yield return Cut("m_charge", x => x.Power.Raise(PowerEventKind.PluggedIn), settle: 1.2);
        yield return Cut("m_device", x => x.Audio.RaiseDevice(new AudioDeviceInfo("Наушники (AirPods Pro)", "AirPods Pro", true, 80)), settle: 1.2);
        yield return Cut("m_home", x => { x.C.Update(0.02); x.Hover(); x.Press(null, MouseButton.Right); x.Release(null, MouseButton.Right); x.Leave(); x.Hover(x.At(0, 12)); });
        yield return Cut("m_notch_idle", _ => { }, IslandMode.Notch);
        yield return Cut("m_notch_music", x => x.Play(), IslandMode.Notch);
        yield return Cut("m_notch_exp", x => { x.Play(); Expand(x); }, IslandMode.Notch);
        yield return Cut("m_long", x => x.Play(), length: 1.6);
        yield return Cut("m_short", x => x.Play(), length: 0.8);

        // ── animations for GIFs (30 fps)
        yield return new Scenario("g_expand", "purple", 4.4, x => x.Play(),
            (0.5, x => x.Hover()),
            (0.9, x => x.Press()),
            (1.0, x => x.Release()),
            (2.8, x => x.Leave()))
        { Every = 2, Preroll = 1.2 };
        yield return new Scenario("g_split", "purple", 4.0, x => x.Play(),
            (0.4, x => x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5))),
            (2.6, x => x.Hub.Timer.Cancel()))
        { Every = 2, Preroll = 1.2 };
        yield return new Scenario("g_call", "purple", 5.0, x => x.Play(),
            (0.4, x => x.Call()),
            (2.0, x => { x.Hover(); x.Press(); }),
            (2.1, x => x.Release()),
            (3.6, x => x.Leave()))
        { Every = 2, Preroll = 1.2 };
        yield return new Scenario("g_notch", "light", 6.4, null,
            (0.4, x => x.C.ToggleMode()),
            (1.6, x => x.Play()),
            (2.6, x => { x.Hover(); x.Press(); }),
            (2.7, x => x.Release()),
            (4.2, x => x.Leave()),
            (5.0, x => x.C.ToggleMode()))
        { Every = 2, Preroll = 0.6 };
        yield return new Scenario("g_length", "purple", 3.0, x => x.Play(),
            (0.3, x => x.Hover()),
            (0.4, x => { for (var i = 0; i < 8; i++) x.C.OnWheel(1, true); }),
            (1.7, x => { for (var i = 0; i < 8; i++) x.C.OnWheel(-1, true); }))
        { Every = 2, Preroll = 1.2 };
        yield return new Scenario("g_huds", "dark", 8.4, x => x.Play(),
            (0.3, x => x.Audio.SetVolume(0.55)),
            (0.5, x => x.Audio.SetVolume(0.62)),
            (0.7, x => x.Audio.SetVolume(0.7)),
            (2.6, x => x.Keyboard.Raise("RU")),
            (4.2, x => x.Power.Raise(PowerEventKind.PluggedIn)),
            (6.2, x => x.Audio.RaiseDevice(new AudioDeviceInfo("Наушники (AirPods Pro)", "AirPods Pro", true, 80))))
        { Every = 2, Preroll = 1.2 };

        yield return new Scenario("h_buttons", "purple", 2.0, x => { x.Play(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); },
            (0.0, x => x.Hover(x.At(0, 145))),
            (0.5, x => x.Hover(x.At(-66, 145))),
            (1.0, x => x.Hover(x.At(66, 145))),
            (1.5, x => x.Hover(x.At(172, 145))))
        { Every = 30, Preroll = 1.6 };

        yield return Still("l07_idle", "purple", _ => { }, length: 0.7);
        yield return Still("l07_music", "purple", x => x.Play(), length: 0.7);
        yield return Still("l07_volume", "dark", x => { x.Play(); x.Audio.SetVolume(0.62); }, settle: 1.0, length: 0.7);
        yield return Still("l07_call", "purple", x => x.Call(), length: 0.7);
        yield return Still("l16_idle", "purple", _ => { }, length: 1.6);
        yield return Still("l16_music", "purple", x => x.Play(), length: 1.6);
        yield return Still("l16_exp", "purple", x => { x.Play(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); }, length: 1.6);
        yield return Still("l16_split", "purple", x => { x.Play(); x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)); }, length: 1.6);
        yield return Still("l22_music", "purple", x => x.Play(), length: 2.2);
        yield return Still("l22_notch", "light", x => x.Play(), IslandMode.Notch, length: 2.2);
        yield return Still("l22_exp", "purple", x => { x.Play(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); }, length: 2.2);

        yield return Still("s_idle", "purple", _ => { });
        yield return Still("s_music", "purple", x => x.Play());
        yield return Still("s_music_exp", "purple", x => { x.Play(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); });
        yield return Still("s_call", "purple", x => x.Call());
        yield return Still("s_call_exp", "purple", x => { x.Call(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); });
        yield return Still("s_timer", "purple", x => x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)));
        yield return Still("s_timer_exp", "purple", x => { x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); });
        yield return Still("s_split", "purple", x => { x.Play(); x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)); });
        yield return Still("s_split_call", "purple", x => { x.Play(); x.Call(); });
        yield return Still("s_volume", "dark", x => { x.Play(); x.Audio.SetVolume(0.62); }, settle: 1.0);
        yield return Still("s_layout", "dark", x => x.Keyboard.Raise("RU"), settle: 0.9);
        yield return Still("s_charge", "dark", x => x.Power.Raise(PowerEventKind.PluggedIn), settle: 1.2);
        yield return Still("s_device", "dark", x => x.Audio.RaiseDevice(new AudioDeviceInfo("Наушники (AirPods Pro)", "AirPods Pro", true, 80)), settle: 1.2);
        yield return Still("s_home", "purple", x => { x.C.Update(0.02); x.Press(null, MouseButton.Right); x.Release(null, MouseButton.Right); });
        yield return Still("s_notch_idle", "light", _ => { }, IslandMode.Notch);
        yield return Still("s_notch_music", "light", x => x.Play(), IslandMode.Notch);
        yield return Still("s_notch_exp", "light", x => { x.Play(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); }, IslandMode.Notch);
        yield return Still("s_notch_dual", "light", x => { x.Play(); x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5)); }, IslandMode.Notch);
        yield return Still("s_alert", "purple", x => { x.Hub.Timer.StartCountdown(TimeSpan.FromSeconds(0.5)); }, settle: 1.4);
        yield return Still("s_light_exp", "light", x => { x.Play(); x.C.Update(0.02); x.Hover(); x.Press(); x.Release(); });

        // ── animations
        yield return new Scenario("a_expand", "purple", 5.2, null,
            (0.2, x => x.Play()),
            (1.4, x => x.Hover()),
            (1.9, x => x.Press()),
            (2.05, x => x.Release()),
            (3.6, x => x.Leave()))
        { Every = 2 };

        yield return new Scenario("a_split", "purple", 4.6, x => x.Play(),
            (0.3, x => x.Hub.Timer.StartCountdown(TimeSpan.FromMinutes(5))),
            (2.6, x => x.Hub.Timer.Cancel()))
        { Every = 1 };

        yield return new Scenario("a_call", "purple", 6.4, x => x.Play(),
            (0.3, x => x.Call()),
            (2.3, x => x.Hover()),
            (2.4, x => x.Press()),
            (2.5, x => x.Release()),
            (4.0, x => x.Leave()),
            (5.0, x => x.Privacy.Set()))
        { Every = 2 };

        yield return new Scenario("a_notch", "light", 7.0, null,
            (0.3, x => x.C.ToggleMode()),
            (1.8, x => x.Play()),
            (3.0, x => { x.Hover(); x.Press(); }),
            (3.1, x => x.Release()),
            (4.5, x => x.Leave()),
            (5.4, x => x.C.ToggleMode()))
        { Every = 2 };

        yield return new Scenario("a_huds", "dark", 10.5, x => x.Play(),
            (0.3, x => x.Audio.SetVolume(0.55)),
            (0.5, x => x.Audio.SetVolume(0.6)),
            (0.7, x => x.Audio.SetVolume(0.68)),
            (2.6, x => x.Keyboard.Raise("RU")),
            (4.3, x => x.Power.Raise(PowerEventKind.PluggedIn)),
            (7.4, x => x.Audio.RaiseDevice(new AudioDeviceInfo("Наушники (AirPods Pro)", "AirPods Pro", true, 80))))
        { Every = 3 };

        yield return new Scenario("a_home", "purple", 3.6, null,
            (0.3, x => { x.Hover(); x.Press(null, MouseButton.Right); x.Release(null, MouseButton.Right); }),
            (2.2, x => x.Leave()))
        { Every = 2 };

        yield return new Scenario("a_alert", "purple", 3.8, x => x.Hub.Timer.StartCountdown(TimeSpan.FromSeconds(1.2)),
            (2.8, x => { x.Hover(); x.Press(); x.Release(); }))
        { Every = 2, Preroll = 0.1 };

        yield return new Scenario("a_track", "purple", 1.6, x => x.Play(0),
            (0.4, x => x.Play(1)),
            (1.0, x => x.Play(2)))
        { Every = 6 };

        yield return new Scenario("a_length", "purple", 2.4, x => x.Play(),
            (0.2, x => x.Hover()),
            (0.3, x => { for (var i = 0; i < 8; i++) x.C.OnWheel(1, true); }),
            (1.3, x => { for (var i = 0; i < 12; i++) x.C.OnWheel(-1, true); }))
        { Every = 4 };

        yield return new Scenario("a_hover", "purple", 2.4, x => x.Play(),
            (0.3, x => x.Hover()),
            (0.9, x => x.Press()),
            (1.25, x => x.Release(new Point(x.Center.X + 60, x.Center.Y + 200))),
            (1.3, x => x.Leave()))
        { Every = 1 };
    }
}
