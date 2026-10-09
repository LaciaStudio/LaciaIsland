using Avalonia;
using Avalonia.Media;

namespace LaciaIsland.Core;

/// <summary>
/// Vector shapes of the island. All figures are emitted clockwise (on screen) so that a single
/// NonZero-filled geometry renders their union without seams.
/// </summary>
public static class ShapeBuilder
{
    /// <summary>iOS-like continuous ("squircle") corner smoothing.</summary>
    public const double IosSmoothing = 0.6;

    private readonly record struct Corner(double A, double B, double C, double D, double P, double L, double R);

    private static double Rad(double deg) => deg * Math.PI / 180.0;

    // Corner smoothing math from Figma's "squircle" article (as implemented by figma-squircle, MIT).
    private static Corner CornerParams(double r, double smoothing, double budget)
    {
        var p = (1 + smoothing) * r;
        var maxSmoothing = budget / r - 1;
        smoothing = Math.Max(0, Math.Min(smoothing, maxSmoothing));
        p = Math.Min(p, budget);

        var arcMeasure = 90 * (1 - smoothing);
        var arcSection = Math.Sin(Rad(arcMeasure / 2)) * r * Math.Sqrt(2);
        var alpha = (90 - arcMeasure) / 2;
        var p3ToP4 = r * Math.Tan(Rad(alpha / 2));
        var beta = 45 * smoothing;
        var c = p3ToP4 * Math.Cos(Rad(beta));
        var d = c * Math.Tan(Rad(beta));
        var b = (p - arcSection - c - d) / 3;
        var a = 2 * b;
        return new Corner(a, b, c, d, p, arcSection, r);
    }

    public static void AddSquircle(StreamGeometryContext ctx, Rect rect, double radius, double smoothing = IosSmoothing)
    {
        var w = rect.Width;
        var h = rect.Height;
        if (w < 0.5 || h < 0.5) return;
        var budget = Math.Min(w, h) / 2;
        radius = Math.Min(Math.Max(0, radius), budget);
        var x = rect.X;
        var y = rect.Y;

        if (radius < 0.05)
        {
            ctx.BeginFigure(new Point(x, y), true);
            ctx.LineTo(new Point(x + w, y));
            ctx.LineTo(new Point(x + w, y + h));
            ctx.LineTo(new Point(x, y + h));
            ctx.EndFigure(true);
            return;
        }

        var k = CornerParams(radius, smoothing, budget);
        double a = k.A, b = k.B, c = k.C, d = k.D, p = k.P, l = k.L, r = k.R;
        var arc = new Size(r, r);

        // top-right
        var s = new Point(x + w - p, y);
        ctx.BeginFigure(s, true);
        var e1 = new Point(s.X + a + b + c, s.Y + d);
        ctx.CubicBezierTo(new Point(s.X + a, s.Y), new Point(s.X + a + b, s.Y), e1);
        var e2 = new Point(e1.X + l, e1.Y + l);
        ArcOrLine(ctx, e2, arc, SweepDirection.Clockwise);
        ctx.CubicBezierTo(new Point(e2.X + d, e2.Y + c), new Point(e2.X + d, e2.Y + b + c), new Point(x + w, y + p));

        // bottom-right
        ctx.LineTo(new Point(x + w, y + h - p));
        s = new Point(x + w, y + h - p);
        e1 = new Point(s.X - d, s.Y + a + b + c);
        ctx.CubicBezierTo(new Point(s.X, s.Y + a), new Point(s.X, s.Y + a + b), e1);
        e2 = new Point(e1.X - l, e1.Y + l);
        ArcOrLine(ctx, e2, arc, SweepDirection.Clockwise);
        ctx.CubicBezierTo(new Point(e2.X - c, e2.Y + d), new Point(e2.X - (b + c), e2.Y + d), new Point(x + w - p, y + h));

        // bottom-left
        ctx.LineTo(new Point(x + p, y + h));
        s = new Point(x + p, y + h);
        e1 = new Point(s.X - (a + b + c), s.Y - d);
        ctx.CubicBezierTo(new Point(s.X - a, s.Y), new Point(s.X - (a + b), s.Y), e1);
        e2 = new Point(e1.X - l, e1.Y - l);
        ArcOrLine(ctx, e2, arc, SweepDirection.Clockwise);
        ctx.CubicBezierTo(new Point(e2.X - d, e2.Y - c), new Point(e2.X - d, e2.Y - (b + c)), new Point(x, y + h - p));

        // top-left
        ctx.LineTo(new Point(x, y + p));
        s = new Point(x, y + p);
        e1 = new Point(s.X + d, s.Y - (a + b + c));
        ctx.CubicBezierTo(new Point(s.X, s.Y - a), new Point(s.X, s.Y - (a + b)), e1);
        e2 = new Point(e1.X + l, e1.Y - l);
        ArcOrLine(ctx, e2, arc, SweepDirection.Clockwise);
        ctx.CubicBezierTo(new Point(e2.X + c, e2.Y - d), new Point(e2.X + b + c, e2.Y - d), new Point(x + p, y));

        ctx.EndFigure(true);
    }

    private static void ArcOrLine(StreamGeometryContext ctx, Point to, Size r, SweepDirection dir)
    {
        if (r.Width < 0.05) ctx.LineTo(to);
        else ctx.ArcTo(to, r, 0, false, dir);
    }

    public static void AddCircle(StreamGeometryContext ctx, Point c, double r)
    {
        if (r < 0.2) return;
        ctx.BeginFigure(new Point(c.X + r, c.Y), true);
        ctx.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
    }

    /// <summary>
    /// Concave "shoulders" that melt the notch into the top edge of the screen.
    /// Uses cubic curves slightly smoother than a circular fillet (like the iPhone X notch ears).
    /// </summary>
    public static void AddShoulders(StreamGeometryContext ctx, double edgeY, double x0, double x1, double rs)
    {
        if (rs < 0.3) return;
        const double k = 0.56; // handle length factor (0.5523 = circle)
        // right
        ctx.BeginFigure(new Point(x1 - 1, edgeY), true);
        ctx.LineTo(new Point(x1 + rs, edgeY));
        ctx.CubicBezierTo(new Point(x1 + rs - rs * k, edgeY), new Point(x1, edgeY + rs - rs * k), new Point(x1, edgeY + rs));
        ctx.LineTo(new Point(x1 - 1, edgeY + rs));
        ctx.EndFigure(true);
        // left
        ctx.BeginFigure(new Point(x0 - rs, edgeY), true);
        ctx.LineTo(new Point(x0 + 1, edgeY));
        ctx.LineTo(new Point(x0 + 1, edgeY + rs));
        ctx.LineTo(new Point(x0, edgeY + rs));
        ctx.CubicBezierTo(new Point(x0, edgeY + rs - rs * k), new Point(x0 - rs + rs * k, edgeY), new Point(x0 - rs, edgeY));
        ctx.EndFigure(true);
    }

    /// <summary>
    /// Gooey "metaball" bridge between two circles (after Hiroyuki Sato's metaball construction).
    /// The neck thins out smoothly before it snaps so the separation looks like surface tension.
    /// Returns true if a bridge was emitted.
    /// </summary>
    public static bool AddMetaball(StreamGeometryContext ctx, Point c1, double r1, Point c2, double r2,
        double maxDist, double spread = 0.5, double handleSize = 2.4)
    {
        var d = M.Dist(c1, c2);
        if (r1 < 0.5 || r2 < 0.5 || d > maxDist || d <= Math.Abs(r1 - r2) + 0.01) return false;

        // thin the neck down to nothing before it disappears
        var v = spread * (1 - M.SmoothStep(Math.Min(r1 + r2 * 0.75, maxDist - 1), maxDist, d));
        if (v < 0.045) return false;

        double u1 = 0, u2 = 0;
        if (d < r1 + r2)
        {
            u1 = Math.Acos(M.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(M.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }

        var between = Math.Atan2(c2.Y - c1.Y, c2.X - c1.X);
        var maxSpread = Math.Acos(M.Clamp((r1 - r2) / d, -1, 1));

        var a1 = between + u1 + (maxSpread - u1) * v;
        var a2 = between - u1 - (maxSpread - u1) * v;
        var a3 = between + Math.PI - u2 - (Math.PI - u2 - maxSpread) * v;
        var a4 = between - Math.PI + u2 + (Math.PI - u2 - maxSpread) * v;

        var p1 = Vec(c1, a1, r1);
        var p2 = Vec(c1, a2, r1);
        var p3 = Vec(c2, a3, r2);
        var p4 = Vec(c2, a4, r2);

        var total = r1 + r2;
        var d2Base = Math.Min(v * handleSize, M.Dist(p1, p3) / total);
        var d2 = d2Base * Math.Min(1, d * 2 / (r1 + r2));
        var hr1 = r1 * d2;
        var hr2 = r2 * d2;

        var h1 = Vec(p1, a1 - Math.PI / 2, hr1);
        var h2 = Vec(p2, a2 + Math.PI / 2, hr1);
        var h3 = Vec(p3, a3 + Math.PI / 2, hr2);
        var h4 = Vec(p4, a4 - Math.PI / 2, hr2);

        // orientation: we need clockwise on screen (positive shoelace with y down)
        var area = Cross(p1, p3) + Cross(p3, p4) + Cross(p4, p2) + Cross(p2, p1);
        if (area >= 0)
        {
            ctx.BeginFigure(p1, true);
            ctx.CubicBezierTo(h1, h3, p3);
            ctx.LineTo(p4);
            ctx.CubicBezierTo(h4, h2, p2);
            ctx.EndFigure(true);
        }
        else
        {
            ctx.BeginFigure(p2, true);
            ctx.CubicBezierTo(h2, h4, p4);
            ctx.LineTo(p3);
            ctx.CubicBezierTo(h3, h1, p1);
            ctx.EndFigure(true);
        }
        return true;
    }

    private static double Cross(Point a, Point b) => a.X * b.Y - b.X * a.Y;
    private static Point Vec(Point c, double angle, double r) => new(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));

    public static StreamGeometry Squircle(Rect rect, double radius, double smoothing = IosSmoothing)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);
            AddSquircle(ctx, rect, radius, smoothing);
        }
        return g;
    }

    public static StreamGeometry Circle(Point c, double r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);
            AddCircle(ctx, c, r);
        }
        return g;
    }
}
