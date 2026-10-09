using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.Services;
using LaciaIsland.UI.Draw;
using LaciaIsland.UI.Presentations;

namespace LaciaIsland.UI;

/// <summary>
/// Decides what the island shows (activities, HUDs, panels), drives the jelly engine,
/// crossfades content, routes pointer input and renders everything through <see cref="Render"/>
/// (one immediate-mode pass, so any host — an Avalonia control or a Win32 layered window — can draw it).
/// </summary>
public sealed class IslandController
{
    public AppSettings Settings { get; }
    public ServiceHub Svc { get; }
    public IslandEngine Engine { get; } = new();
    public IslandView View { get; } = new();

    public double S => Settings.Scale;
    public double Time { get; private set; }
    public CaptureUsage? CurrentCall { get; private set; }
    public bool FullscreenActive { get; set; }

    public static readonly Size BaseWindow = new(640, 280);
    public Size WindowSize => new(
        Math.Ceiling(Math.Max(BaseWindow.Width, 360 * Settings.Length + 160) * S),
        Math.Ceiling(BaseWindow.Height * S + Settings.TopGap));

    public event Action? SettingsRequested;
    public event Action? FrameRequested;

    private readonly IdlePresentation _idle;
    private readonly MusicPresentation _music;
    private readonly CallPresentation _call;
    private readonly TimerPresentation _timer;
    private readonly VolumeHud _volume;
    private readonly LayoutHud _layout;
    private readonly ChargeHud _charge;
    private readonly DeviceHud _device;
    private readonly HomePresentation _home;
    private readonly AlertPresentation _alert;
    private readonly Presentation[] _all;

    private Presentation _main;
    private Presentation? _out;
    private readonly Tween _outT = new(0.2);
    private readonly Tween _inT = new(0.34, 0.06);
    private Presentation? _bubble;
    private Presentation? _bubbleOut;
    private readonly Tween _bubbleInT = new(0.3, 0.16);
    private readonly Tween _bubbleOutT = new(0.14);

    private bool _expanded;
    private Presentation? _expandTarget;
    private Presentation? _promoted;
    private readonly Dictionary<Presentation, double> _activeSince = new();

    private Point _cursor;
    private bool _cursorKnown;
    private bool _hover;
    private bool _pressed;
    private MouseButton _pressButton;
    private Point _pressPoint;
    private string? _pressId;
    private bool _pressOnBubble;
    private bool _dragging;
    private string? _hoverId;
    private double _leaveTimer;
    private double _hoverTimer;
    private DateTime _lastPlayingUtc = DateTime.MinValue;
    private bool _wasSettled;

    private StreamGeometry? _shape;
    private StreamGeometry? _mainClip;
    private StreamGeometry? _bubbleClip;
    private IslandFrame _frame;
    private double _opOutBase, _opOutDetail, _opInBase, _opInDetail, _opBubbleOut, _opBubbleIn;
    private double _outScale = 1, _inScale = 1;

    /// <summary>Raised after every <see cref="Update"/> (hosts redraw on it).</summary>
    public event Action? Updated;
    public IslandFrame Frame => _frame;

    public IslandController(AppSettings settings, ServiceHub svc)
    {
        Settings = settings;
        Svc = svc;
        _idle = new IdlePresentation(this);
        _music = new MusicPresentation(this);
        _call = new CallPresentation(this);
        _timer = new TimerPresentation(this);
        _volume = new VolumeHud(this);
        _layout = new LayoutHud(this);
        _charge = new ChargeHud(this);
        _device = new DeviceHud(this);
        _home = new HomePresentation(this);
        _alert = new AlertPresentation(this);
        _all = new Presentation[] { _idle, _music, _call, _timer, _volume, _layout, _charge, _device, _home, _alert };
        _main = _idle;
        _inT.Finish();
        _outT.Finish();
        _bubbleInT.Finish();
        _bubbleOutT.Finish();

        View.Painter = Render;

        View.PointerMovedAt += OnCursor;
        View.PointerLeft += OnCursorLeft;
        View.Pressed += OnPress;
        View.Released += OnRelease;
        View.Wheel += OnWheel;

        Svc.Media.Changed += () =>
        {
            if (Svc.Media.Current?.IsPlaying == true) _lastPlayingUtc = Clock.UtcNow;
            Wake();
        };
        Svc.Audio.VolumeChanged += () =>
        {
            if (Settings.ShowVolume) _volume.Show(Svc.Audio.Volume, Svc.Audio.Muted);
            Wake();
        };
        Svc.Audio.DefaultDeviceChanged += d =>
        {
            if (Settings.ShowDevices) _device.Show(d);
            Wake();
        };
        Svc.Privacy.Changed += () => { RefreshCall(); Wake(); };
        Svc.Power.Event += e =>
        {
            if (Settings.ShowCharging && e.Kind != PowerEventKind.Unplugged) _charge.Show(e);
            Wake();
        };
        Svc.Keyboard.LayoutChanged += code =>
        {
            if (Settings.ShowLayout) _layout.Show(code);
            Wake();
        };
        Svc.Timer.Finished += () => { _alert.Show(); Wake(); };
        Svc.Timer.Changed += Wake;
        Settings.Changed += () => { RefreshCall(); ApplySettings(); };

        ApplySettings();
        RefreshCall();
        Decide(out var main, out var variant, out _, out var hidden);
        _main = main;
        _main.Variant = variant;
        var size = main.TargetSize(Settings.Mode, variant, S);
        Engine.SetTargetSize(size.Width, size.Height, main.TargetRadius(Settings.Mode, variant, size, S));
        Engine.Attach.Target = Settings.Mode == IslandMode.Notch ? 1 : 0;
        Engine.Reveal.Target = hidden ? 0 : 1;
        Engine.SnapAll();
        _frame = Engine.Frame;
    }

    // ───────────────────────────── settings ─────────────────────────────

    private void ApplySettings()
    {
        Engine.S = S;
        Engine.Gap = Settings.TopGap;
        Engine.CompactHeight = 34;
        Engine.ShoulderBase = 9 * S;
        Engine.CenterX = WindowSize.Width / 2;
        Wake();
    }

    public void ToggleMode()
    {
        Settings.Mode = Settings.Mode == IslandMode.Island ? IslandMode.Notch : IslandMode.Island;
        Settings.NotifyChanged();
        Collapse();
        Engine.KickScale(0.3);
    }

    public void StartTimer(TimeSpan duration)
    {
        Svc.Timer.StartCountdown(duration);
        Collapse();
        Wake();
    }

    public void StartStopwatch()
    {
        Svc.Timer.StartStopwatch();
        Collapse();
        Wake();
    }

    public void RequestSettings()
    {
        Collapse();
        SettingsRequested?.Invoke();
    }

    public void Wake() => FrameRequested?.Invoke();

    // ───────────────────────────── decisions ─────────────────────────────

    private void RefreshCall()
    {
        bool Ignored(CaptureUsage u) => Settings.IgnoredCallApps.Any(n =>
            !string.IsNullOrWhiteSpace(n) &&
            (u.AppName.Contains(n.Trim(), StringComparison.OrdinalIgnoreCase) ||
             (u.ExePath?.Contains(n.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)));
        CurrentCall = Settings.ShowCalls
            ? Svc.Privacy.Active.Where(u => u.Mic && !Ignored(u)).OrderBy(u => u.StartUtc).FirstOrDefault()
            : null;
        try { Svc.Audio.MicMonitoring = CurrentCall is not null; } catch (Exception ex) { Log.Error("mic monitor", ex); }
    }

    private bool MusicActive()
    {
        if (!Settings.ShowMusic) return false;
        var m = Svc.Media.Current;
        if (m is null) return false;
        if (m.IsPlaying) return true;
        if ((Clock.UtcNow - _lastPlayingUtc).TotalSeconds < 15) return true;
        return _expanded && ReferenceEquals(_expandTarget, _music);
    }

    private List<Presentation> Persistent()
    {
        var raw = new List<Presentation>(3);
        if (CurrentCall is not null) raw.Add(_call);
        if (Svc.Timer.Mode != TimerService.Kind.None) raw.Add(_timer);
        if (MusicActive()) raw.Add(_music);

        foreach (var p in raw) if (!_activeSince.ContainsKey(p)) _activeSince[p] = Time;
        foreach (var k in _activeSince.Keys.ToList()) if (!raw.Contains(k)) _activeSince.Remove(k);
        if (_promoted is not null && !raw.Contains(_promoted)) _promoted = null;

        // calls first; otherwise the activity that was there first stays in the main island
        var ordered = raw.OrderBy(p => p == _call ? 0 : 1).ThenBy(p => _activeSince[p]).ToList();
        if (_promoted is not null && ordered.Remove(_promoted)) ordered.Insert(0, _promoted);
        return ordered;
    }

    private HudPresentation? ActiveHud()
    {
        HudPresentation? best = null;
        foreach (var h in new HudPresentation[] { _volume, _layout, _charge, _device })
            if (h.Active && (best is null || h.ShownAt > best.ShownAt)) best = h;
        return best;
    }

    private bool PeekZone() =>
        _cursorKnown && Math.Abs(_cursor.X - Engine.CenterX) < 110 * S &&
        _cursor.Y < (Engine.Reveal.Value > 0.5 ? 56 * S : 6);

    private void Decide(out Presentation main, out Variant variant, out Presentation? bubble, out bool hidden)
    {
        var persistent = Persistent();
        var notch = Settings.Mode == IslandMode.Notch;
        bubble = null;
        variant = Variant.Compact;

        if (_alert.Active)
        {
            main = _alert;
            variant = Variant.Expanded;
        }
        else if (_expanded && _expandTarget is not null &&
                 (_expandTarget == _home || persistent.Contains(_expandTarget) || (_expandTarget == _music && Svc.Media.Current is not null)))
        {
            main = _expandTarget;
            variant = Variant.Expanded;
        }
        else
        {
            if (_expanded) { _expanded = false; _expandTarget = null; }
            var hud = ActiveHud();
            if (hud is not null)
            {
                main = hud;
                if (persistent.Count >= 2 && !notch) bubble = persistent[1];
            }
            else if (persistent.Count == 0) main = _idle;
            else if (persistent.Count == 1) main = persistent[0];
            else
            {
                main = persistent[0];
                variant = Variant.Minimal;
                if (!notch) bubble = persistent[1];
            }
        }

        foreach (var p in _all) p.TrailingGuest = null;
        if (notch && variant == Variant.Minimal && persistent.Count >= 2) main.TrailingGuest = persistent[1];

        hidden = false;
        if (FullscreenActive && Settings.HideInFullscreen && CurrentCall is null && !_alert.Active) hidden = true;
        if (Settings.HideWhenIdle && main == _idle && !PeekZone()) hidden = true;
    }

    private void Expand(Presentation target)
    {
        _expanded = true;
        _expandTarget = target;
        _leaveTimer = 0;
        Wake();
    }

    private void ExpandDefault()
    {
        var persistent = Persistent();
        Presentation target = persistent.Count > 0 ? persistent[0]
            : Svc.Media.Current is not null && Settings.ShowMusic ? _music : _home;
        Expand(target);
    }

    public void Collapse()
    {
        _expanded = false;
        _expandTarget = null;
        Wake();
    }

    // ───────────────────────────── per-frame ─────────────────────────────

    /// <summary>True while the render loop should keep running at full rate.</summary>
    public bool NeedsFrames =>
        !Engine.IsSettled || _out is not null || !_inT.Done || _bubbleOut is not null || !_bubbleInT.Done ||
        _main.Animating || _alert.Active || _pressed || _dragging;

    /// <summary>True while low-rate (10 Hz) refresh is needed (timers, expiring HUDs, leave timers).</summary>
    public bool NeedsPeriodic =>
        _expanded || Svc.Timer.Mode != TimerService.Kind.None || CurrentCall is not null || ActiveHud() is not null ||
        (Svc.Media.Current is { IsPlaying: false } && (Clock.UtcNow - _lastPlayingUtc).TotalSeconds < 16) ||
        Settings.HideWhenIdle || _bubble is not null;

    public void Update(double dt)
    {
        dt = M.Clamp(dt, 0, 0.1);
        Time += dt;
        var now = Clock.UtcNow;
        var mode = Settings.Mode;

        Svc.Timer.Tick();
        _alert.Tick(dt);
        if (Svc.Media.Current?.IsPlaying == true) _lastPlayingUtc = now;

        Decide(out var main, out var variant, out var bubble, out var hidden);

        if (!ReferenceEquals(main, _main))
        {
            _out = _main;
            _outT.Restart();
            _main = main;
            _inT.Restart();
            Engine.KickScale(main == _idle ? 0.05 : 0.14);
        }
        _main.Variant = variant;

        if (!ReferenceEquals(bubble, _bubble))
        {
            if (_bubble is not null) { _bubbleOut = _bubble; _bubbleOutT.Restart(); }
            _bubble = bubble;
            _bubbleInT.Restart();
        }

        // engine targets
        var size = main.TargetSize(mode, variant, S);
        Engine.SetTargetSize(size.Width, size.Height, main.TargetRadius(mode, variant, size, S));
        var bubbleD = 34 * S;
        var bubbleGap = 8 * S;
        Engine.OffsetX.Target = bubble is not null ? -(bubbleGap + bubbleD) / 2 : 0;
        if (bubble is not null) Engine.ShowBubble(bubbleD / 2, bubbleGap);
        else Engine.HideBubble();
        Engine.Attach.Target = mode == IslandMode.Notch ? 1 : 0;
        Engine.Reveal.Target = hidden ? 0 : 1;
        var squish = _pressed && _pressButton == MouseButton.Left && _pressId is null && !_pressOnBubble && variant != Variant.Expanded;
        Engine.Scale.Target = squish ? 0.95 : _hover && variant != Variant.Expanded && !hidden ? 1.035 : 1.0;

        Engine.Step(dt);
        _frame = Engine.Frame;

        _hover = _cursorKnown && _frame.HitTest(_cursor, 3 * S);
        var hoverId = _hover && !_pressOnBubble ? HitId(_main, _cursor) : null;
        _hoverId = hoverId;

        // collapse when the pointer leaves an expanded view
        if (_expanded && !_alert.Active)
        {
            if (_hover || _pressed) _leaveTimer = 0;
            else if ((_leaveTimer += dt) > 0.6) Collapse();
        }
        if (!_expanded && Settings.ExpandOnHover && _hover && !_pressed && !_alert.Active && _main != _idle)
        {
            if ((_hoverTimer += dt) > 0.4) ExpandDefault();
        }
        else if (!_hover) _hoverTimer = 0;

        var ctx = new UpdateCtx
        {
            F = _frame, Dt = dt, Time = Time, Now = now, Mode = mode, S = S,
            HoverId = _hoverId, PressId = _pressed ? _pressId : null,
        };
        _main.Update(ctx);
        if (_out is not null)
        {
            _outT.Step(dt);
            ctx.HoverId = null;
            ctx.PressId = null;
            if (!ReferenceEquals(_out, _main)) _out.Update(ctx);
            if (_outT.Done) _out = null;
        }
        _inT.Step(dt);
        _bubbleInT.Step(dt);
        if (_bubbleOut is not null)
        {
            _bubbleOutT.Step(dt);
            if (_bubbleOutT.Done || !_frame.HasBubble) _bubbleOut = null;
        }

        try
        {
            Svc.Audio.VisualizerEnabled = Settings.RealVisualizer && (_main == _music || _out == _music) &&
                                          Svc.Media.Current?.IsPlaying == true && !hidden;
        }
        catch (Exception ex) { Log.Error("visualizer", ex); }

        PrepareRender();
        _wasSettled = Engine.IsSettled;
        View.InvalidateVisual();
        Updated?.Invoke();
    }

    private static string? HitId(Presentation p, Point pt)
    {
        foreach (var h in p.Hits) if (h.Contains(pt)) return h.Id;
        return null;
    }

    private void PrepareRender()
    {
        var f = _frame;
        _shape = f.BuildShape();
        _mainClip = f.BuildMainClip();
        _bubbleClip = f.HasBubble ? ShapeBuilder.Circle(f.BubbleCenter, f.BubbleRadius) : null;

        var tOut = _out is null ? 1 : M.EaseOutCubic(_outT.Progress);
        var tIn = _inT.Done ? 1 : M.EaseOutCubic(_inT.Progress);
        _opOutBase = _out is null ? 0 : 1 - tOut;
        _opOutDetail = _out is null ? 0 : (1 - tOut) * _out.DetailAlpha;
        _opInBase = tIn;
        _opInDetail = tIn * _main.DetailAlpha;
        _outScale = _out is null ? 1 : 1 - 0.08 * tOut;
        _inScale = _inT.Done ? 1 : 0.9 + 0.1 * M.EaseOutBack(_inT.Progress, 1.2);
        _opBubbleOut = _bubbleOut is null ? 0 : 1 - M.EaseOutCubic(_bubbleOutT.Progress);
        _opBubbleIn = M.EaseOutCubic(_bubbleInT.Progress);
    }

    private static readonly IImmutableBrush Black = new ImmutableSolidColorBrush(Colors.Black);

    /// <summary>Draws the whole island (window DIP coordinates). Pure black shape + content, no effects.</summary>
    public void Render(DrawingContext dc)
    {
        if (_shape is null) return;
        var f = _frame;
        dc.DrawGeometry(Black, null, _shape);

        if (_mainClip is not null && f.Visible.Height > 0.5)
        {
            using (dc.PushGeometryClip(_mainClip))
            {
                DrawPres(dc, _out, false, _opOutBase, _outScale);
                DrawPres(dc, _out, true, _opOutDetail, _outScale);
                DrawPres(dc, _main, false, _opInBase, _inScale);
                DrawPres(dc, _main, true, _opInDetail, _inScale);
            }
        }

        if (f.HasBubble && _bubbleClip is not null)
        {
            using (dc.PushGeometryClip(_bubbleClip))
            {
                DrawMinimal(dc, _bubbleOut, _opBubbleOut, f.BubbleRect);
                DrawMinimal(dc, _bubble, _opBubbleIn, f.BubbleRect);
            }
        }
    }

    private void DrawPres(DrawingContext dc, Presentation? p, bool detail, double opacity, double scale)
    {
        if (p is null || opacity <= 0.004) return;
        try
        {
            using (Paint.ScaleAround(dc, _frame.Visible.Center, scale))
            {
                if (opacity >= 0.996)
                {
                    if (detail) p.RenderDetail(dc); else p.RenderBase(dc);
                }
                else
                {
                    using (dc.PushOpacity(opacity))
                    {
                        if (detail) p.RenderDetail(dc); else p.RenderBase(dc);
                    }
                }
            }
        }
        catch (Exception ex) { Log.Error("paint " + p.Key, ex); }
    }

    private static void DrawMinimal(DrawingContext dc, Presentation? p, double opacity, Rect r)
    {
        if (p is null || opacity <= 0.004) return;
        try
        {
            using (dc.PushOpacity(Math.Min(1, opacity)))
                p.RenderMinimal(dc, r);
        }
        catch (Exception ex) { Log.Error("paint minimal " + p.Key, ex); }
    }

    /// <summary>Bounding box of everything that is drawn (window DIPs); empty when hidden.</summary>
    public Rect ContentBounds
    {
        get
        {
            var f = _frame;
            if (_shape is null || f.Visible.Height < 0.5 || f.Visible.Width < 0.5) return default;
            var r = f.Visible;
            if (f.Shoulder > 0.3)
                r = r.Union(new Rect(f.Body.Left - f.Shoulder, 0, f.Body.Width + 2 * f.Shoulder, f.Shoulder));
            if (f.HasBubble) r = r.Union(f.BubbleRect);
            r = r.Inflate(3 * S);
            var w = WindowSize;
            var x0 = Math.Max(0, r.X);
            var y0 = Math.Max(0, r.Y);
            var x1 = Math.Min(w.Width, r.Right);
            var y1 = Math.Min(w.Height, r.Bottom);
            return x1 > x0 && y1 > y0 ? new Rect(x0, y0, x1 - x0, y1 - y0) : default;
        }
    }

    /// <summary>
    /// Draws one of everything (texts in every weight, icons, art, bars, rings, shapes) so the JIT and
    /// font caches are hot before the first real animation.
    /// </summary>
    public void Warmup(DrawingContext dc)
    {
        try
        {
            var y = 20.0;
            foreach (var w in new[] { FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold })
            foreach (var size in new[] { 12.0, 13.0, 13.5, 14.0, 15.0, 16.0, 21.0, 36.0, 38.0 })
            {
                Typo.Draw(dc, "Тест Test 0:00", size * S, w, Colors.White, 10, y, 200);
                Typo.DrawTabular(dc, "12:34 -0:51 100%", size * S, w, Pal.Orange, 300, y, HAlign.Right);
            }
            foreach (var f in typeof(Icons).GetFields())
                if (f.GetValue(null) is Icon icon) Icons.Draw(dc, icon, new Point(40, 40), 24 * S, Colors.White);
            var levels = new float[] { 0.2f, 0.5f, 0.9f, 0.4f, 0.7f, 0.3f };
            Paint.Bars(dc, new Rect(10, 10, 30, 16), levels, Colors.White, Pal.Blue, 2.6, 2.2, 2.6);
            Paint.Bars(dc, new Rect(10, 10, 30, 16), levels, Pal.Green, Pal.Green, 1.8, 1.7, 1.8);
            Paint.Art(dc, null, new Rect(10, 10, 22, 22), 6, Pal.Fill);
            Paint.Ring(dc, new Point(50, 50), 9, 2.6, 0.6, Pal.OrangeDark, Pal.Orange);
            Paint.Battery(dc, new Rect(10, 10, 30, 13), 0.5, Pal.Green, true);
            Paint.RoundButton(dc, new Point(60, 60), 27, Pal.Fill, Icons.Pause, Colors.White, 22, true, false);
            Paint.GlyphButton(dc, new Point(60, 60), Icons.Play, 30, Colors.White, true, true);
            Paint.Capsule(dc, new Rect(10, 10, 88, 36), Pal.OrangeDark);
            using (dc.PushOpacity(0.5))
            using (dc.PushGeometryClip(ShapeBuilder.Squircle(new Rect(0, 0, 420, 174), 44)))
                dc.DrawGeometry(Black, null, ShapeBuilder.Squircle(new Rect(0, 0, 420, 174), 44));
            foreach (var p in _all) p.RenderMinimal(dc, new Rect(0, 0, 34, 34));
        }
        catch (Exception ex) { Log.Error("warmup", ex); }
    }

    // ───────────────────────────── input ─────────────────────────────

    public void OnCursor(Point p)
    {
        var changed = !_cursorKnown || M.Dist(p, _cursor) > 0.3;
        _cursor = p;
        _cursorKnown = true;
        if (_dragging) _main.OnDrag("progress", p, 1);
        if (changed) Wake();
    }

    public void OnCursorLeft()
    {
        if (_dragging || _pressed) return;
        _cursorKnown = false;
        Wake();
    }

    public void OnPress(Point p, MouseButton button)
    {
        _cursor = p;
        _cursorKnown = true;
        if (!_frame.HitTest(p, 3 * S)) return;
        _pressed = true;
        _pressButton = button;
        _pressPoint = p;
        _pressOnBubble = _frame.HitBubble(p, 2 * S) && !_frame.Visible.Contains(p);
        _pressId = button == MouseButton.Left && !_pressOnBubble ? HitId(_main, p) : null;
        if (_pressId == "progress")
        {
            _dragging = true;
            _main.OnDrag("progress", p, 0);
        }
        Wake();
    }

    public void OnRelease(Point p, MouseButton button)
    {
        if (!_pressed) return;
        _pressed = false;
        var pressId = _pressId;
        _pressId = null;
        Wake();

        if (_dragging)
        {
            _dragging = false;
            _main.OnDrag("progress", p, 2);
            return;
        }

        var dx = p.X - _pressPoint.X;
        if (Math.Abs(dx) > 36 * S && Math.Abs(dx) > Math.Abs(p.Y - _pressPoint.Y) * 1.5)
        {
            // swipe → swap the two activities (like the iOS gesture)
            var persistent = Persistent();
            if (persistent.Count >= 2 && !_expanded)
            {
                _promoted = persistent[1];
                Engine.KickShake(Math.Sign(dx) * 220 * S);
            }
            return;
        }
        if (M.Dist(p, _pressPoint) > 10 * S) return;

        switch (button)
        {
            case MouseButton.Right:
                if (_expanded && _expandTarget == _home) Collapse(); else Expand(_home);
                Engine.KickScale(0.2);
                return;
            case MouseButton.Middle:
                Svc.Media.PlayPause();
                Engine.KickScale(0.15);
                return;
        }

        if (_alert.Active)
        {
            _alert.Dismiss();
            return;
        }

        if (_pressOnBubble && _bubble is not null)
        {
            var target = _bubble;
            _promoted = target;
            Expand(target);
            Engine.KickScale(0.25);
            return;
        }

        if (pressId is not null && HitId(_main, p) == pressId)
        {
            _main.OnClick(pressId);
            return;
        }

        if (_expanded)
        {
            if (_main.CollapseOnBodyClick) Collapse();
        }
        else
        {
            ExpandDefault();
        }
    }

    public void OnWheel(double delta, bool ctrl = false)
    {
        if (!_hover || Math.Abs(delta) < 0.01) return;
        if (ctrl)
        {
            // Ctrl + wheel: stretch / shrink the island (saved like the slider in settings)
            var len = Math.Round((Settings.Length + Math.Sign(delta) * 0.05) * 100) / 100;
            Settings.Length = Math.Clamp(len, AppSettings.MinLength, AppSettings.MaxLength);
            Settings.NotifyChanged();
            Wake();
            return;
        }
        var steps = Math.Clamp(delta, -5, 5);
        Svc.Audio.SetVolume(Math.Clamp(Svc.Audio.Volume + steps * 0.02, 0, 1));
        Wake();
    }

    /// <summary>Hit-test in window coordinates (used for click-through).</summary>
    public bool IsOverIsland(Point p) => _frame.HitTest(p, 4 * S);

    public string Describe() =>
        $"main={_main.Key}/{_main.Variant} out={_out?.Key} bubble={_bubble?.Key} expanded={_expanded} hover={_hover} " +
        $"W={Engine.W.Value:0.0} H={Engine.H.Value:0.0} R={Engine.R.Value:0.0} attach={Engine.Attach.Value:0.00}";
}
