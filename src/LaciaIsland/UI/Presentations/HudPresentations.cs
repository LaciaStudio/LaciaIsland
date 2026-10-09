using Avalonia;
using Avalonia.Media;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.UI.Draw;

namespace LaciaIsland.UI.Presentations;

/// <summary>Short-lived compact indicator (volume, keyboard layout, charging, device).</summary>
public abstract class HudPresentation : Presentation
{
    protected HudPresentation(IslandController c) : base(c) { }
    private double _until;
    public double ShownAt { get; private set; } = double.NegativeInfinity;
    public bool Active => C.Time < _until;
    public override bool Animating => Active;

    protected void Touch(double seconds)
    {
        if (!Active) ShownAt = C.Time;
        _until = C.Time + seconds;
    }

    public void Hide() => _until = 0;

    protected override Size BaseSize(IslandMode mode, Variant v, double s) =>
        mode == IslandMode.Island ? Z(CompactW, 34, s) : Z(CompactW + 24, 32, s);

    protected abstract double CompactW { get; }

    protected double Pad => Mode == IslandMode.Island ? 14 * S : 18 * S;
}

public sealed class VolumeHud : HudPresentation
{
    public VolumeHud(IslandController c) : base(c) { }
    public override string Key => "hud.volume";
    protected override double CompactW => 300;

    private double _target, _shown = -1;
    private bool _muted;

    public void Show(double volume, bool muted)
    {
        _target = volume;
        _muted = muted;
        if (_shown < 0) _shown = volume;
        Touch(1.8);
    }

    protected override void OnUpdate() => _shown = M.Approach(_shown < 0 ? _target : _shown, _muted ? 0 : _target, 20, Dt);

    protected override void OnRenderBase(DrawingContext dc)
    {
        var icon = _muted || _target <= 0.001 ? Icons.SpeakerMute : _target < 0.5 ? Icons.SpeakerLow : Icons.Speaker;
        Icons.Draw(dc, icon, new Point(V.Left + Pad + 8 * S, Cy), 17 * S, Colors.White);

        var pct = (int)Math.Round((_muted ? 0 : _target) * 100);
        var textW = Typo.TabularWidth("100", 13 * S, FontWeight.SemiBold);
        Typo.DrawTabular(dc, pct.ToString(), 13 * S, FontWeight.SemiBold, Colors.White, V.Right - Pad, Cy, HAlign.Right);

        var barRight = V.Right - Pad - textW - 10 * S;
        var barW = Math.Clamp(barRight - (V.Left + Pad + 30 * S), 24 * S, 132 * S * Math.Max(1, C.Settings.Length));
        var bar = new Rect(barRight - barW, Cy - 3 * S, barW, 6 * S);
        dc.DrawRectangle(Typo.Brush(Pal.Track), null, new RoundedRect(bar, 3 * S));
        var fw = Math.Max(0, bar.Width * M.Clamp01(_shown));
        if (fw > 0.5)
            dc.DrawRectangle(Typo.Brush(_muted ? Pal.Gray : Colors.White), null, new RoundedRect(new Rect(bar.X, bar.Y, Math.Max(fw, bar.Height), bar.Height), 3 * S));
    }
}

public sealed class LayoutHud : HudPresentation
{
    public LayoutHud(IslandController c) : base(c) { }
    public override string Key => "hud.layout";
    protected override double CompactW => 214;
    private string _code = "";

    public void Show(string code)
    {
        if (Active && code != _code) C.Engine.KickScale(0.14);
        _code = code;
        Touch(1.3);
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        Icons.Draw(dc, Icons.Globe, new Point(V.Left + Pad + 8 * S, Cy), 17 * S, Colors.White);
        Typo.Draw(dc, _code, 15 * S, FontWeight.SemiBold, Colors.White, V.Right - Pad - 2 * S, Cy, 0, HAlign.Right);
    }
}

public sealed class ChargeHud : HudPresentation
{
    public ChargeHud(IslandController c) : base(c) { }
    public override string Key => "hud.charge";
    protected override double CompactW => 292;
    private PowerEvent? _evt;

    public void Show(PowerEvent e)
    {
        _evt = e;
        Touch(e.Kind == PowerEventKind.Low ? 4 : 2.6);
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        if (_evt is null) return;
        var charging = _evt.Kind == PowerEventKind.PluggedIn;
        var low = _evt.Kind == PowerEventKind.Low;
        var title = charging ? "Зарядка" : low ? "Низкий заряд" : "Батарея";
        Typo.Draw(dc, title, 14 * S, FontWeight.SemiBold, Colors.White, V.Left + Pad + 4 * S, Cy);
        var col = charging ? Pal.Green : low ? (_evt.Percent <= 10 ? Pal.Red : Pal.Yellow) : Colors.White;
        var bat = new Rect(V.Right - Pad - 30 * S, Cy - 6.5 * S, 30 * S, 13 * S);
        Paint.Battery(dc, bat, _evt.Percent / 100.0, col, charging);
        Typo.DrawTabular(dc, $"{_evt.Percent}%", 14 * S, FontWeight.SemiBold, col, bat.X - 7 * S, Cy, HAlign.Right);
    }
}

public sealed class DeviceHud : HudPresentation
{
    public DeviceHud(IslandController c) : base(c) { }
    public override string Key => "hud.device";
    protected override double CompactW => 300;
    private AudioDeviceInfo? _info;

    public void Show(AudioDeviceInfo info)
    {
        _info = info;
        Touch(3);
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        if (_info is null) return;
        Icons.Draw(dc, _info.IsHeadphones ? Icons.Headphones : Icons.Speaker, new Point(V.Left + Pad + 8 * S, Cy), 18 * S, Colors.White);
        var right = V.Right - Pad;
        if (_info.Battery is { } b)
        {
            var c = new Point(right - 10 * S, Cy);
            Paint.Ring(dc, c, 9 * S, 2.6 * S, b / 100.0, Color.FromRgb(0x1F, 0x3A, 0x26), b <= 20 ? Pal.Red : Pal.Green);
            right -= 28 * S;
        }
        var maxW = right - (V.Left + Pad + 26 * S);
        Typo.Draw(dc, _info.Model, 13.5 * S, FontWeight.Medium, Colors.White, right, Cy, maxW, HAlign.Right);
    }
}
