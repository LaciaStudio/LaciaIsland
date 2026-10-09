using Avalonia;
using Avalonia.Media;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.UI.Draw;

namespace LaciaIsland.UI.Presentations;

/// <summary>
/// "Call" live activity. Windows has no public call API, so this follows microphone usage:
/// when an app (Discord, Telegram, Zoom, a browser…) uses the mic, the island shows a green call
/// with a timer and the live voice level.
/// </summary>
public sealed class CallPresentation : Presentation
{
    public CallPresentation(IslandController c) : base(c) { }

    public override string Key => "call";
    public override bool Animating => true;

    private const int BarsN = 13;
    private readonly float[] _levels = new float[BarsN];
    private readonly float[] _history = new float[BarsN];
    private double _histTimer;
    private double _muteMorph;
    private CaptureUsage? _last;

    private CaptureUsage? Usage => C.CurrentCall ?? _last;

    protected override Size BaseSize(IslandMode mode, Variant v, double s) => (mode, v) switch
    {
        (IslandMode.Island, Variant.Compact) => Z(286, 34, s),
        (IslandMode.Island, Variant.Minimal) => Z(190, 34, s),
        (IslandMode.Island, Variant.Expanded) => Z(420, 88, s),
        (IslandMode.Notch, Variant.Compact) => Z(310, 32, s),
        (IslandMode.Notch, Variant.Minimal) => Z(300, 32, s),
        _ => Z(440, 88, s),
    };

    private double _e;
    private Point _iconC, _muteC, _openC;
    private double _x0, _bw;

    protected override void OnUpdate()
    {
        if (C.CurrentCall is not null) _last = C.CurrentCall;
        var level = C.Svc.Audio.MicMuted ? 0 : C.Svc.Audio.MicLevel;
        _histTimer += Dt;
        while (_histTimer > 0.055)
        {
            _histTimer -= 0.055;
            Array.Copy(_history, 1, _history, 0, BarsN - 1);
            _history[BarsN - 1] = (float)level;
        }
        for (var i = 0; i < BarsN; i++)
        {
            var env = 0.45 + 0.55 * Math.Sin(Math.PI * (i + 0.5) / BarsN);
            var idx = (i * 7 + 3) % BarsN; // scatter so it looks like a waveform, not a scroller
            var target = (float)M.Clamp(_history[idx] * env * 1.35, 0, 1);
            var rate = target > _levels[i] ? 30.0 : 10.0;
            _levels[i] = (float)M.Approach(_levels[i], target, rate, Dt);
        }
        _muteMorph = M.Approach(_muteMorph, C.Svc.Audio.MicMuted ? 1 : 0, 16, Dt);

        _e = ExpandT();
        DetailAlpha = M.SmoothStep(0.45, 1.0, _e);
        DetailBlur = (1 - DetailAlpha) * 9 * S;

        _bw = ExpandedSize.Width;
        _x0 = BlockLeft(_bw);
        _iconC = new Point(_x0 + 16 * S + 28 * S, Cy);
        _openC = new Point(_x0 + _bw - 16 * S - 27 * S, Cy);
        _muteC = new Point(_openC.X - 64 * S, Cy);
        if (Variant == Variant.Expanded && _e > 0.7)
        {
            HitCircle("mute", _muteC, 27 * S);
            HitCircle("open", _openC, 27 * S);
        }
    }

    private string Elapsed()
    {
        var u = Usage;
        if (u is null) return "0:00";
        return Typo.FormatTime(Now - u.StartUtc);
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        var compactAlpha = 1 - M.SmoothStep(0.05, 0.45, _e);
        if (compactAlpha <= 0.01) return;
        using var _ = dc.PushOpacity(compactAlpha);
        var pad = Mode == IslandMode.Island ? 14 * S : 16 * S;
        Icons.Draw(dc, Icons.Phone, new Point(V.Left + pad + 7 * S, Cy), 15 * S, Pal.Green);
        Typo.DrawTabular(dc, Elapsed(), 14 * S, FontWeight.SemiBold, Pal.Green, V.Left + pad + 19 * S, Cy);

        if (Variant != Variant.Minimal)
        {
            var w = BarsN * 1.8 * S + (BarsN - 1) * 1.7 * S;
            var area = new Rect(V.Right - pad - w, Cy - 8.5 * S, w, 17 * S);
            Paint.Bars(dc, area, _levels, Pal.Green, Pal.Green, 1.8 * S, 1.7 * S, 1.8 * S);
        }
    }

    public override void RenderDetail(DrawingContext dc)
    {
        var u = Usage;
        if (u is null || DetailAlpha <= 0.01) return;

        // app avatar
        var r = 28 * S;
        if (u.Icon is not null)
        {
            Paint.Circle(dc, _iconC, r, Color.FromRgb(0x24, 0x24, 0x28));
            var ir = r * 0.62;
            dc.DrawImage(u.Icon, new Rect(0, 0, u.Icon.Size.Width, u.Icon.Size.Height), M.CenteredRect(_iconC, ir * 2, ir * 2));
        }
        else
        {
            Paint.Circle(dc, _iconC, r, Pal.GreenDark);
            Icons.Draw(dc, u.Camera ? Icons.Video : Icons.Mic, _iconC, 26 * S, Pal.Green);
        }

        var textX = _iconC.X + r + 14 * S;
        var maxW = _muteC.X - 27 * S - 12 * S - textX;
        Typo.Draw(dc, u.AppName, 16 * S, FontWeight.SemiBold, Colors.White, textX, Cy - 10 * S, maxW);
        var what = u.Camera ? "Камера и микрофон" : "Микрофон";
        var sub = $"{what} · {Elapsed()}";
        Typo.Draw(dc, sub, 13 * S, FontWeight.Normal, Pal.Gray, textX, Cy + 11 * S, maxW);

        // mute toggle (white when muted, like iOS)
        var bg = M.Lerp(Color.FromRgb(0x3A, 0x3A, 0x3E), Colors.White, _muteMorph);
        var fg = M.Lerp(Colors.White, Colors.Black, _muteMorph);
        Paint.RoundButton(dc, _muteC, 27 * S, bg, Icons.MicSlash, fg, 24 * S, IsHover("mute"), IsPress("mute"));
        Paint.RoundButton(dc, _openC, 27 * S, Pal.Green, Icons.OpenApp, Colors.White, 22 * S, IsHover("open"), IsPress("open"));
    }

    public override void RenderMinimal(DrawingContext dc, Rect r)
    {
        Icons.Draw(dc, Icons.Phone, r.Center, Math.Min(r.Width, r.Height) * 0.5, Pal.Green);
    }

    public override void OnClick(string id)
    {
        switch (id)
        {
            case "mute": C.Svc.Audio.ToggleMicMute(); C.Engine.KickScale(0.12); break;
            case "open": C.Svc.System.BringToFront(Usage?.ExePath ?? Usage?.AppName); break;
        }
    }
}
