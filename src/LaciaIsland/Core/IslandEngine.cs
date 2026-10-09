using Avalonia;
using Avalonia.Media;

namespace LaciaIsland.Core;

public enum IslandMode { Island, Notch }

/// <summary>Snapshot of the island geometry for one rendered frame (window DIP coordinates).</summary>
public readonly struct IslandFrame
{
    /// <summary>Full body rect. In notch mode it extends above the screen edge.</summary>
    public Rect Body { get; init; }
    /// <summary>The visible part of the body (content area).</summary>
    public Rect Visible { get; init; }
    public double Radius { get; init; }
    public double Shoulder { get; init; }
    public double Attach { get; init; }
    public bool HasBubble { get; init; }
    public Point BubbleCenter { get; init; }
    public double BubbleRadius { get; init; }
    /// <summary>0 = compact height, 1 = fully expanded (may overshoot slightly).</summary>
    public double Expansion { get; init; }
    public double Reveal { get; init; }
    public double Scale { get; init; }
    public double S { get; init; }
    public Point MainCapCenter { get; init; }
    public double MainCapRadius { get; init; }
    /// <summary>Max centre distance at which the gooey neck still exists (0 = broken).</summary>
    public double NeckMax { get; init; }

    public Rect BubbleRect => new(BubbleCenter.X - BubbleRadius, BubbleCenter.Y - BubbleRadius, BubbleRadius * 2, BubbleRadius * 2);

    public StreamGeometry BuildShape(bool includeBubble = true)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.SetFillRule(FillRule.NonZero);
        if (Body.Width > 0.5 && Body.Height > 0.5 && Visible.Height > 0.01)
        {
            ShapeBuilder.AddSquircle(ctx, Body, Radius);
            if (Shoulder > 0.3)
                ShapeBuilder.AddShoulders(ctx, 0, Body.Left, Body.Right, Shoulder);
        }
        if (includeBubble && HasBubble && BubbleRadius > 0.4)
        {
            ShapeBuilder.AddCircle(ctx, BubbleCenter, BubbleRadius);
            if (Body.Width > 1 && NeckMax > 0)
                ShapeBuilder.AddMetaball(ctx, MainCapCenter, MainCapRadius, BubbleCenter, BubbleRadius, NeckMax);
        }
        return g;
    }

    public StreamGeometry BuildMainClip() => ShapeBuilder.Squircle(Body, Radius);

    public bool HitTest(Point p, double pad)
    {
        if (Reveal < 0.3) return false;
        var v = Visible.Inflate(pad);
        if (v.ContainsInclusive(p)) return true;
        if (HasBubble && BubbleRadius > 2 && M.Dist(p, BubbleCenter) <= BubbleRadius + pad) return true;
        return false;
    }

    public bool HitBubble(Point p, double pad) =>
        HasBubble && BubbleRadius > 2 && M.Dist(p, BubbleCenter) <= BubbleRadius + pad;
}

/// <summary>
/// The jelly physics of the island: springs for size, corner radius, the island→notch morph,
/// hover/press scale, the detachable bubble and a horizontal "shake". Also produces squash &amp; stretch
/// from the spring velocities so fast changes look like an incompressible blob of jelly.
/// </summary>
public sealed class IslandEngine
{
    public readonly Spring W = new(150, 0.45, 0.75);
    public readonly Spring H = new(34, 0.45, 0.75);
    public readonly Spring R = new(17, 0.45, 0.8);
    public readonly Spring Attach = new(0, 0.6, 0.72);
    public readonly Spring Reveal = new(1, 0.5, 0.82);
    public readonly Spring Scale = new(1, 0.34, 0.55);
    public readonly Spring OffsetX = new(0, 0.5, 0.74);
    public readonly Spring Shake = new(0, 0.11, 0.12);
    public readonly Spring BubbleX = new(0, 0.5, 0.62);
    public readonly Spring BubbleR = new(0, 0.42, 0.55);

    /// <summary>Horizontal centre of the island in window coordinates.</summary>
    public double CenterX = 320;
    /// <summary>Global UI scale (settings).</summary>
    public double S = 1;
    /// <summary>Distance from the top screen edge in island mode.</summary>
    public double Gap = 8;
    public double CompactHeight = 34;
    public double ShoulderBase = 9;
    public double NotchHiddenTop = 90;
    public double SquashK = 0.025;

    public bool BubbleAlive { get; private set; }
    public bool BubbleWanted { get; private set; }
    /// <summary>Gap (DIP) at which the neck snaps while the bubble flies out.</summary>
    public double NeckBreakGap = 6;
    private bool _neckBroken;
    private bool _bridged;

    public IslandFrame Frame { get; private set; }

    private Spring[] All => new[] { W, H, R, Attach, Reveal, Scale, OffsetX, Shake, BubbleX, BubbleR };

    public bool IsSettled
    {
        get
        {
            foreach (var s in All) if (!s.IsSettled) return false;
            if (BubbleAlive && !BubbleWanted) return false;
            return true;
        }
    }

    public void SnapAll()
    {
        foreach (var s in All) s.Snap(s.Target);
        if (!BubbleWanted) BubbleAlive = false;
        Frame = Compute();
    }

    /// <summary>Set the target box of the main body. Picks bouncier springs for growing.</summary>
    public void SetTargetSize(double w, double h, double r)
    {
        bool grow = h > H.Target + 2 || w > W.Target + 30;
        bool shrink = h < H.Target - 2 || w < W.Target - 30;
        if (grow)
        {
            W.Response = 0.42; W.Damping = 0.64;
            H.Response = 0.48; H.Damping = 0.60;
            R.Response = 0.40; R.Damping = 0.78;
        }
        else if (shrink)
        {
            W.Response = 0.42; W.Damping = 0.70;
            H.Response = 0.38; H.Damping = 0.70;
            R.Response = 0.36; R.Damping = 0.85;
        }
        W.Target = w; H.Target = h; R.Target = r;
    }

    private double MainRightCapRel()
    {
        var w = W.Value * Scale.Value;
        var capR = Math.Min(R.Value, H.Value / 2) * Scale.Value;
        return OffsetX.Value + w / 2 - capR;
    }

    public void ShowBubble(double radius, double gap)
    {
        var targetX = OffsetX.Target + W.Target / 2 + gap + radius;
        if (!BubbleAlive)
        {
            BubbleAlive = true;
            _bridged = true;
            _neckBroken = false;
            var capR = Math.Min(R.Value, H.Value / 2);
            BubbleX.Snap(MainRightCapRel());
            BubbleR.Snap(Math.Max(2, capR * 0.6));
            BubbleX.Velocity = 0;
        }
        BubbleWanted = true;
        BubbleX.Set(targetX, 0.55, 0.6);
        BubbleR.Set(radius, 0.42, 0.55);
    }

    public void HideBubble()
    {
        if (!BubbleAlive) { BubbleWanted = false; return; }
        if (BubbleWanted)
        {
            BubbleX.Response = 0.38; BubbleX.Damping = 0.9;
            BubbleR.Response = 0.4; BubbleR.Damping = 0.9;
        }
        BubbleWanted = false;
    }

    public void KickScale(double v) => Scale.Kick(v);
    public void KickShake(double v) => Shake.Kick(v);

    public void Step(double dt)
    {
        // sub-step the bubble absorb target so it follows the moving body
        if (BubbleAlive && !BubbleWanted)
        {
            var capR = Math.Min(R.Value, H.Value / 2);
            BubbleX.Target = MainRightCapRel();
            BubbleR.Target = Math.Max(1, capR * 0.45);
        }

        foreach (var s in All) s.Step(dt);

        if (BubbleAlive && !BubbleWanted)
        {
            var capR = Math.Min(R.Value, H.Value / 2);
            if (Math.Abs(BubbleX.Value - MainRightCapRel()) < 1.5 * S || BubbleR.Value < capR * 0.5)
            {
                BubbleAlive = false;
                BubbleR.Snap(0);
            }
        }

        Frame = Compute();

        // surface tension: the neck stretches, snaps (with a wobble on both blobs) and does not re-form
        // while the bubble settles; it only re-forms when the bubble is being absorbed back.
        if (Frame.HasBubble)
        {
            var d = M.Dist(Frame.MainCapCenter, Frame.BubbleCenter);
            var maxDist = Frame.MainCapRadius + Frame.BubbleRadius + NeckBreakGap * S;
            if (BubbleWanted)
            {
                if (!_neckBroken && d > maxDist)
                {
                    _neckBroken = true;
                    BubbleR.Kick(-60 * S);
                    BubbleX.Kick(70 * S);
                    W.Kick(-70 * S);
                    H.Kick(-25 * S);
                }
            }
            else
            {
                var bridged = d < maxDist;
                if (!_bridged && bridged) H.Kick(28 * S);
                _bridged = bridged;
                _neckBroken = false;
            }
            Frame = Compute();
        }
    }

    private IslandFrame Compute()
    {
        var s = Scale.Value;
        var w = Math.Max(0, W.Value);
        var h = Math.Max(0, H.Value);

        // squash & stretch: when the body grows fast in one direction it thins in the other
        var sqx = M.Clamp(-H.Velocity * SquashK, -0.08 * w, 0.06 * w);
        var sqy = M.Clamp(-W.Velocity * SquashK * 0.45, -0.12 * h, 0.08 * h);
        var wv = Math.Max(0, w + sqx);
        var hv = Math.Max(0, h + sqy);

        var reveal = M.Clamp01(Reveal.Value);
        var revealW = M.Lerp(0.5, 1, reveal);
        var ws = wv * s * revealW;
        var hs = hv * s * M.Lerp(0.7, 1, reveal);

        var a = M.Clamp(Attach.Value, -0.25, 1.25);
        var a01 = M.Clamp01(a);

        var islandTop = Gap - (hs - hv) / 2;
        var islandBottom = islandTop + hs;
        var bottom = M.Lerp(islandBottom, hs, a);
        var top = M.Lerp(islandTop, -NotchHiddenTop * S, a);

        var hide = (1 - reveal) * (bottom + 8 * S);
        bottom -= hide;
        top -= hide;

        var cx = CenterX + OffsetX.Value + Shake.Value;
        var body = new Rect(cx - ws / 2, top, ws, Math.Max(0, bottom - top));
        var radius = Math.Max(0, R.Value * s);
        var radiusEff = Math.Min(radius, Math.Min(body.Width, body.Height) / 2);

        // concave shoulders only once the top corners are above the screen edge
        var straight = -(top + radiusEff);
        var shoulder = ShoulderBase * S * a01 * M.Clamp01(straight / (6 * S));
        shoulder = Math.Min(shoulder, Math.Max(0, bottom - radiusEff - 0.5));
        shoulder = Math.Min(shoulder, body.Width / 4);

        var visTop = Math.Max(top, 0);
        var visible = new Rect(body.X, visTop, body.Width, Math.Max(0, bottom - visTop));

        var midY = (top + bottom) / 2;
        if (a01 > 0.001) midY = M.Lerp(midY, (visTop + bottom) / 2, a01);
        var capR = Math.Min(radiusEff, body.Height / 2);
        var capCenter = new Point(body.Right - capR, midY);

        var hasBubble = false;
        var bubbleCenter = new Point();
        var br = 0.0;
        if (BubbleAlive)
        {
            br = Math.Max(0, BubbleR.Value) * s * (1 - a01) * revealW;
            if (br > 0.4)
            {
                hasBubble = true;
                bubbleCenter = new Point(CenterX + BubbleX.Value + Shake.Value * 0.6, midY);
            }
        }

        var expansion = (h - CompactHeight * S) / Math.Max(1, 100 * S);
        var neckMax = hasBubble && !_neckBroken ? capR + br + NeckBreakGap * S : 0;

        return new IslandFrame
        {
            Body = body,
            Visible = visible,
            Radius = radiusEff,
            Shoulder = shoulder,
            Attach = a01,
            HasBubble = hasBubble,
            BubbleCenter = bubbleCenter,
            BubbleRadius = br,
            Expansion = expansion,
            Reveal = reveal,
            Scale = s,
            S = S,
            MainCapCenter = capCenter,
            MainCapRadius = capR,
            NeckMax = neckMax,
        };
    }
}
