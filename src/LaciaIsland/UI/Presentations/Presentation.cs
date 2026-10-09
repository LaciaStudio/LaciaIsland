using Avalonia;
using Avalonia.Media;
using LaciaIsland.Core;

namespace LaciaIsland.UI.Presentations;

public enum Variant { Compact, Expanded, Minimal }

public readonly record struct HitRegion(string Id, Rect Rect, bool Circle = false)
{
    public bool Contains(Point p) => Circle ? M.Dist(p, Rect.Center) <= Rect.Width / 2 : Rect.ContainsInclusive(p);
}

public sealed class UpdateCtx
{
    public IslandFrame F;
    public double Dt;
    public double Time;
    public DateTime Now;
    public IslandMode Mode;
    public double S;
    public string? HoverId;
    public string? PressId;
}

/// <summary>
/// Something the island can show: idle, a live activity (music, call, timer), a HUD or a panel.
/// It declares its target size per variant and draws itself relative to the current (jelly) frame,
/// so the content moves together with the springing shape.
/// </summary>
public abstract class Presentation
{
    protected readonly IslandController C;
    protected Presentation(IslandController c) => C = c;

    public abstract string Key { get; }
    public Variant Variant { get; set; } = Variant.Compact;

    public IslandFrame F { get; private set; }
    protected double S = 1;
    protected double Time;
    protected double Dt;
    protected DateTime Now;
    protected IslandMode Mode;
    protected string? HoverId;
    protected string? PressId;

    /// <summary>Opacity of the "expanded-only" layer (texts, controls).</summary>
    public double DetailAlpha { get; protected set; } = 1;
    /// <summary>Blur of the "expanded-only" layer while it morphs in.</summary>
    public double DetailBlur { get; protected set; }
    public List<HitRegion> Hits { get; } = new();

    /// <summary>Optional second activity drawn in the trailing slot (notch mode has no bubble).</summary>
    public Presentation? TrailingGuest { get; set; }

    /// <summary>Requests continuous frames (e.g. animated waveform).</summary>
    public virtual bool Animating => false;
    /// <summary>Whether a click on an empty area of the expanded view collapses it.</summary>
    public virtual bool CollapseOnBodyClick => true;

    protected Rect V => F.Visible;
    protected double Cy => F.Visible.Center.Y;

    protected static Size Z(double w, double h, double s) => new(w * s, h * s);

    /// <summary>Designed size of a variant (before the user's length setting).</summary>
    protected abstract Size BaseSize(IslandMode mode, Variant v, double s);

    /// <summary>
    /// Target size with the user's "length" applied: idle/compact/minimal stretch horizontally,
    /// expanded views keep their designed width but never get narrower than the stretched compact one.
    /// </summary>
    public Size TargetSize(IslandMode mode, Variant v, double s)
    {
        var size = BaseSize(mode, v, s);
        var length = C.Settings.Length;
        if (v == Variant.Expanded)
        {
            var compactW = BaseSize(mode, Variant.Compact, s).Width * length;
            return new Size(Math.Max(size.Width, compactW), size.Height);
        }
        return new Size(size.Width * length, size.Height);
    }

    public virtual double TargetRadius(IslandMode mode, Variant v, Size size, double s) =>
        mode == IslandMode.Island
            ? size.Height / 2
            : Math.Min(size.Height / 2, (v == Variant.Expanded ? 34 : 13) * s);

    protected Size CompactSize => TargetSize(Mode, Variant.Compact, S);
    protected Size ExpandedSize => TargetSize(Mode, Variant.Expanded, S);

    /// <summary>0 at compact height, 1 at expanded height (from the live frame).</summary>
    protected double ExpandT()
    {
        var hc = CompactSize.Height;
        var he = ExpandedSize.Height;
        if (he - hc < 1) return Variant == Variant.Expanded ? 1 : 0;
        var h = F.Visible.Height / Math.Max(0.01, F.Scale);
        return M.Clamp((h - hc) / (he - hc), 0, 1.12);
    }

    /// <summary>Left edge of a content block of the given width centred in the frame.</summary>
    protected double BlockLeft(double width) => F.Visible.Center.X - width / 2;

    public void Update(UpdateCtx u)
    {
        F = u.F;
        S = u.S;
        Time = u.Time;
        Dt = u.Dt;
        Now = u.Now;
        Mode = u.Mode;
        HoverId = u.HoverId;
        PressId = u.PressId;
        Hits.Clear();
        OnUpdate();
    }

    protected virtual void OnUpdate() { }

    public void RenderBase(DrawingContext dc)
    {
        OnRenderBase(dc);
        if (TrailingGuest is not null && Variant == Variant.Minimal)
        {
            var d = V.Height - 8 * S;
            var r = new Rect(V.Right - 10 * S - d, Cy - d / 2, d, d);
            TrailingGuest.RenderMinimal(dc, r);
        }
    }

    protected virtual void OnRenderBase(DrawingContext dc) { }
    public virtual void RenderDetail(DrawingContext dc) { }

    /// <summary>Tiny glyph used inside the detached bubble (or the notch trailing slot).</summary>
    public virtual void RenderMinimal(DrawingContext dc, Rect r) { }

    public virtual void OnClick(string id) { }
    public virtual void OnDrag(string id, Point p, int phase) { }

    protected bool IsHover(string id) => HoverId == id;
    protected bool IsPress(string id) => PressId == id;

    protected void Hit(string id, Rect r, bool circle = false) => Hits.Add(new HitRegion(id, r, circle));
    protected void HitCircle(string id, Point c, double r) => Hits.Add(new HitRegion(id, new Rect(c.X - r, c.Y - r, r * 2, r * 2), true));
}

public sealed class IdlePresentation : Presentation
{
    public IdlePresentation(IslandController c) : base(c) { }
    public override string Key => "idle";
    protected override Size BaseSize(IslandMode mode, Variant v, double s) =>
        mode == IslandMode.Island ? Z(132, 34, s) : Z(184, 30, s);
    public override double TargetRadius(IslandMode mode, Variant v, Size size, double s) =>
        mode == IslandMode.Island ? size.Height / 2 : 11 * s;
}
