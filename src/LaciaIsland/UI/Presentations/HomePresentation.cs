using System.Globalization;
using Avalonia;
using Avalonia.Media;
using LaciaIsland.Core;
using LaciaIsland.UI.Draw;

namespace LaciaIsland.UI.Presentations;

/// <summary>Quick panel opened from an idle island: clock, quick timers, mode switch, settings.</summary>
public sealed class HomePresentation : Presentation
{
    public HomePresentation(IslandController c) : base(c) { }

    public override string Key => "home";
    public override bool Animating => false;

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private double _x0, _bw, _e;
    private readonly Rect[] _chips = new Rect[3];
    private Point _stopwatchC, _modeC, _settingsC;

    protected override Size BaseSize(IslandMode mode, Variant v, double s) =>
        mode == IslandMode.Island ? Z(420, 152, s) : Z(440, 148, s);

    public override double TargetRadius(IslandMode mode, Variant v, Size size, double s) =>
        mode == IslandMode.Island ? 44 * s : 34 * s;

    protected override void OnUpdate()
    {
        _e = M.Clamp01((F.Visible.Height / Math.Max(0.01, F.Scale) - 34 * S) / Math.Max(1, ExpandedSize.Height - 34 * S));
        DetailAlpha = M.SmoothStep(0.35, 0.95, _e);
        DetailBlur = (1 - DetailAlpha) * 9 * S;
        _bw = ExpandedSize.Width;
        _x0 = BlockLeft(_bw);
        var y0 = V.Top;
        var rowY = y0 + 114 * S;
        var chipW = 88 * S;
        for (var i = 0; i < 3; i++)
        {
            _chips[i] = new Rect(_x0 + 24 * S + i * (chipW + 8 * S), rowY - 18 * S, chipW, 36 * S);
            Hit("t" + i, _chips[i]);
        }
        _stopwatchC = new Point(_chips[2].Right + 8 * S + 18 * S, rowY);
        HitCircle("stopwatch", _stopwatchC, 18 * S);
        _settingsC = new Point(_x0 + _bw - 24 * S - 18 * S, y0 + 40 * S);
        _modeC = new Point(_settingsC.X - 44 * S, _settingsC.Y);
        HitCircle("settings", _settingsC, 18 * S);
        HitCircle("mode", _modeC, 18 * S);
    }

    public override void RenderDetail(DrawingContext dc)
    {
        var local = Now.ToLocalTime();
        var y0 = V.Top;
        Typo.DrawTabular(dc, local.ToString("HH:mm", Ru), 36 * S, FontWeight.SemiBold, Colors.White, _x0 + 24 * S, y0 + 40 * S);
        var date = local.ToString("dddd, d MMMM", Ru);
        Typo.Draw(dc, date, 13.5 * S, FontWeight.Medium, Pal.Gray, _x0 + 25 * S, y0 + 68 * S);

        var presets = C.Settings.TimerPresets;
        for (var i = 0; i < 3; i++)
        {
            var r = _chips[i];
            if (IsPress("t" + i)) r = r.Deflate(2 * S);
            var bg = IsHover("t" + i) ? Color.FromRgb(0x5A, 0x3B, 0x0C) : Pal.OrangeDark;
            Paint.Capsule(dc, r, bg);
            Icons.Draw(dc, Icons.Timer, new Point(r.X + 20 * S, r.Center.Y), 15 * S, Pal.Orange);
            Typo.Draw(dc, $"{presets[i]} мин", 13.5 * S, FontWeight.SemiBold, Pal.Orange, r.X + 32 * S, r.Center.Y);
        }
        Paint.RoundButton(dc, _stopwatchC, 18 * S, Pal.OrangeDark, Icons.Stopwatch, Pal.Orange, 17 * S, IsHover("stopwatch"), IsPress("stopwatch"));

        var modeIcon = C.Settings.Mode == IslandMode.Island ? Icons.NotchMode : Icons.IslandMode;
        Paint.RoundButton(dc, _modeC, 18 * S, Pal.Fill, modeIcon, Colors.White, 19 * S, IsHover("mode"), IsPress("mode"));
        Paint.RoundButton(dc, _settingsC, 18 * S, Pal.Fill, Icons.Gear, Colors.White, 18 * S, IsHover("settings"), IsPress("settings"));
    }

    public override void OnClick(string id)
    {
        switch (id)
        {
            case "t0": case "t1": case "t2":
                C.StartTimer(TimeSpan.FromMinutes(C.Settings.TimerPresets[id[1] - '0']));
                break;
            case "stopwatch": C.StartStopwatch(); break;
            case "mode": C.ToggleMode(); break;
            case "settings": C.RequestSettings(); break;
        }
    }
}
