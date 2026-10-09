using Avalonia;
using Avalonia.Media;
using LaciaIsland.Core;
using LaciaIsland.Services;
using LaciaIsland.UI.Draw;

namespace LaciaIsland.UI.Presentations;

/// <summary>Countdown timer / stopwatch live activity (orange, like the iOS Clock app).</summary>
public sealed class TimerPresentation : Presentation
{
    public TimerPresentation(IslandController c) : base(c) { }

    public override string Key => "timer";
    public override bool Animating => true;

    private TimerService T => C.Svc.Timer;
    private double _e;
    private double _x0, _bw;
    private Point _pauseC, _cancelC;
    private double _runMorph;

    protected override Size BaseSize(IslandMode mode, Variant v, double s) => (mode, v) switch
    {
        (IslandMode.Island, Variant.Compact) => Z(270, 34, s),
        (IslandMode.Island, Variant.Minimal) => Z(190, 34, s),
        (IslandMode.Island, Variant.Expanded) => Z(400, 98, s),
        (IslandMode.Notch, Variant.Compact) => Z(300, 32, s),
        (IslandMode.Notch, Variant.Minimal) => Z(300, 32, s),
        _ => Z(420, 96, s),
    };

    private string Label => T.Mode == TimerService.Kind.Stopwatch ? "Секундомер" : "Таймер";

    private string _lastText = "0:00";
    private double _lastProgress = 1;

    private string TimeText()
    {
        if (T.Mode == TimerService.Kind.None) return _lastText;
        if (T.Mode == TimerService.Kind.Stopwatch) return _lastText = Typo.FormatTime(T.Elapsed(Now));
        // count down like iOS: show the ceiling of the remaining seconds
        var rem = T.Remaining(Now);
        return _lastText = Typo.FormatTime(TimeSpan.FromSeconds(Math.Ceiling(rem.TotalSeconds - 1e-6)));
    }

    private double Progress
    {
        get
        {
            if (T.Mode == TimerService.Kind.None) return _lastProgress;
            return _lastProgress = T.Mode == TimerService.Kind.Countdown
                ? 1 - T.Progress(Now)
                : (T.Elapsed(Now).TotalSeconds % 60) / 60.0;
        }
    }

    protected override void OnUpdate()
    {
        _e = ExpandT();
        DetailAlpha = M.SmoothStep(0.45, 1.0, _e);
        DetailBlur = (1 - DetailAlpha) * 9 * S;
        _runMorph = M.Approach(_runMorph, T.Running ? 1 : 0, 16, Dt);
        _bw = ExpandedSize.Width;
        _x0 = BlockLeft(_bw);
        _pauseC = new Point(_x0 + 20 * S + 27 * S, Cy);
        _cancelC = new Point(_pauseC.X + 64 * S, Cy);
        if (Variant == Variant.Expanded && _e > 0.7)
        {
            HitCircle("toggle", _pauseC, 27 * S);
            HitCircle("cancel", _cancelC, 27 * S);
        }
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        var compactAlpha = 1 - M.SmoothStep(0.05, 0.45, _e);
        if (compactAlpha <= 0.01) return;
        using var _ = dc.PushOpacity(compactAlpha);
        var pad = Mode == IslandMode.Island ? 12 * S : 16 * S;
        var c = new Point(V.Left + pad + 9 * S, Cy);
        Paint.Ring(dc, c, 8.5 * S, 2.6 * S, Progress, Pal.OrangeDark, Pal.Orange);
        if (Variant != Variant.Minimal)
        {
            var col = T.Running ? Pal.Orange : M.Lerp(Pal.Orange, Pal.Gray, 0.35);
            Typo.DrawTabular(dc, TimeText(), 15 * S, FontWeight.SemiBold, col, V.Right - pad - 4 * S, Cy, HAlign.Right);
        }
    }

    public override void RenderDetail(DrawingContext dc)
    {
        if (DetailAlpha <= 0.01) return;
        var pauseIcon = T.Running ? Icons.Pause : Icons.Play;
        Paint.RoundButton(dc, _pauseC, 27 * S, Pal.OrangeDark, pauseIcon, Pal.Orange, 22 * S, IsHover("toggle"), IsPress("toggle"));
        Paint.RoundButton(dc, _cancelC, 27 * S, Color.FromRgb(0x33, 0x33, 0x37), T.Mode == TimerService.Kind.Stopwatch ? Icons.Reset : Icons.XMark,
            Colors.White, 20 * S, IsHover("cancel"), IsPress("cancel"));

        var right = _x0 + _bw - 30 * S;
        Typo.Draw(dc, Label, 13 * S, FontWeight.Medium, Pal.Orange, right, Cy - 19 * S, 0, HAlign.Right);
        Typo.DrawTabular(dc, TimeText(), 38 * S, FontWeight.Medium, T.Running ? Pal.Orange : M.Lerp(Pal.Orange, Pal.Gray, 0.4), right, Cy + 9 * S, HAlign.Right);
    }

    public override void RenderMinimal(DrawingContext dc, Rect r)
    {
        var rr = Math.Min(r.Width, r.Height) / 2;
        Paint.Ring(dc, r.Center, rr * 0.62, 2.6 * S, Progress, Pal.OrangeDark, Pal.Orange);
        var a = -Math.PI / 2 + Progress * Math.PI * 2;
        var hand = rr * 0.3;
        dc.DrawLine(new Avalonia.Media.Immutable.ImmutablePen(Typo.Brush(Pal.Orange), 2 * S, null, PenLineCap.Round),
            r.Center, new Point(r.Center.X + hand * Math.Cos(a), r.Center.Y + hand * Math.Sin(a)));
    }

    public override void OnClick(string id)
    {
        switch (id)
        {
            case "toggle": T.Toggle(); C.Engine.KickScale(0.12); break;
            case "cancel":
                if (T.Mode == TimerService.Kind.Stopwatch && T.Running) T.StartStopwatch();
                else T.Cancel();
                break;
        }
    }
}

/// <summary>"Timer done" alert: rings, shakes, waits for a click.</summary>
public sealed class AlertPresentation : Presentation
{
    public AlertPresentation(IslandController c) : base(c) { }

    public override string Key => "alert";
    public override bool Animating => true;
    public override bool CollapseOnBodyClick => false;

    public bool Active { get; private set; }
    private double _since;
    private double _shakeTimer;
    private double _soundTimer;
    private int _rings;
    private Point _bellC;
    private Rect _stop;
    private double _x0, _bw;

    public void Show()
    {
        Active = true;
        _since = 0;
        _shakeTimer = 0.25;
        _soundTimer = 0;
        _rings = 0;
    }

    public void Dismiss() => Active = false;

    public void Tick(double dt)
    {
        if (!Active) return;
        _since += dt;
        _shakeTimer -= dt;
        _soundTimer -= dt;
        if (_shakeTimer <= 0)
        {
            C.Engine.KickShake(170 * C.S);
            C.Engine.KickScale(0.25);
            _shakeTimer = 1.3;
        }
        if (_soundTimer <= 0 && _rings < 6 && C.Settings.TimerSound)
        {
            C.Svc.System.PlayAlert();
            _rings++;
            _soundTimer = 2.6;
        }
        if (_since > 30) Active = false;
    }

    protected override Size BaseSize(IslandMode mode, Variant v, double s) =>
        mode == IslandMode.Island ? Z(400, 98, s) : Z(420, 96, s);

    protected override void OnUpdate()
    {
        DetailAlpha = 1;
        DetailBlur = 0;
        _bw = TargetSize(Mode, Variant.Expanded, S).Width;
        _x0 = BlockLeft(_bw);
        _bellC = new Point(_x0 + 20 * S + 27 * S, Cy);
        _stop = new Rect(_x0 + _bw - 20 * S - 100 * S, Cy - 23 * S, 100 * S, 46 * S);
        Hit("stop", _stop);
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        Paint.Circle(dc, _bellC, 27 * S, Pal.OrangeDark);
        var swing = Math.Sin(_since * 22) * 18 * Math.Exp(-((_since % 1.3) * 2.2));
        var m = Matrix.CreateTranslation(-_bellC.X, -(_bellC.Y - 9 * S)) * Matrix.CreateRotation(swing * Math.PI / 180)
                * Matrix.CreateTranslation(_bellC.X, _bellC.Y - 9 * S);
        using (dc.PushTransform(m))
            Icons.Draw(dc, Icons.Bell, _bellC, 26 * S, Pal.Orange);

        var tx = _bellC.X + 27 * S + 14 * S;
        Typo.Draw(dc, "Таймер", 13 * S, FontWeight.Medium, Pal.Gray, tx, Cy - 11 * S);
        Typo.Draw(dc, "Готово", 21 * S, FontWeight.SemiBold, Colors.White, tx, Cy + 11 * S);

        var hover = IsHover("stop");
        var press = IsPress("stop");
        var r = press ? _stop.Deflate(3 * S) : _stop;
        Paint.Capsule(dc, r, hover ? M.Lerp(Pal.Orange, Colors.White, 0.15) : Pal.Orange);
        Typo.Draw(dc, "Стоп", 16 * S, FontWeight.SemiBold, Colors.Black, r.Center.X, r.Center.Y, 0, HAlign.Center);
    }

    public override void OnClick(string id) => Dismiss();
}
