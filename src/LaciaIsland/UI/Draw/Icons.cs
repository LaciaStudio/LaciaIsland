using Avalonia;
using Avalonia.Media;
using LaciaIsland.Core;

namespace LaciaIsland.UI.Draw;

/// <summary>One layer of an icon: a geometry in a 24×24 box, either filled or stroked.</summary>
public sealed record IconPart(Geometry Geometry, bool Fill, double Stroke = 0);

public sealed class Icon
{
    public IconPart[] Parts { get; }
    public Icon(params IconPart[] parts) => Parts = parts;
}

/// <summary>Hand-made glyphs in the spirit of rounded system symbols (24×24 grid).</summary>
public static class Icons
{
    private static IconPart F(string path) => new(Geometry.Parse(path), true);
    private static IconPart F(Geometry g) => new(g, true);
    private static IconPart S(string path, double w = 2) => new(Geometry.Parse(path), false, w);

    public static Geometry RoundedPolygon(double radius, params Point[] pts)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.SetFillRule(FillRule.NonZero);
        var n = pts.Length;
        Point Toward(Point from, Point to, double d)
        {
            var len = M.Dist(from, to);
            var t = len < 1e-6 ? 0 : Math.Min(d, len / 2) / len;
            return new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }
        for (var i = 0; i < n; i++)
        {
            var prev = pts[(i - 1 + n) % n];
            var cur = pts[i];
            var next = pts[(i + 1) % n];
            var a = Toward(cur, prev, radius);
            var b = Toward(cur, next, radius);
            if (i == 0) ctx.BeginFigure(a, true); else ctx.LineTo(a);
            ctx.QuadraticBezierTo(cur, b);
        }
        ctx.EndFigure(true);
        return g;
    }

    private static Geometry RoundRect(double x, double y, double w, double h, double r)
        => new RectangleGeometry(new Rect(x, y, w, h), r, r);

    private static Geometry Circle(double cx, double cy, double r) => new EllipseGeometry(new Rect(cx - r, cy - r, 2 * r, 2 * r));

    public static readonly Icon Play = new(F(RoundedPolygon(2.4, new(7.6, 4.0), new(20.6, 12), new(7.6, 20.0))));
    public static readonly Icon Pause = new(F(RoundRect(5.8, 4.4, 4.4, 15.2, 1.5)), F(RoundRect(13.8, 4.4, 4.4, 15.2, 1.5)));
    public static readonly Icon Forward = new(
        F(RoundedPolygon(1.7, new(2.6, 6.3), new(12.2, 12), new(2.6, 17.7))),
        F(RoundedPolygon(1.7, new(11.8, 6.3), new(21.4, 12), new(11.8, 17.7))));
    public static readonly Icon Backward = new(
        F(RoundedPolygon(1.7, new(21.4, 6.3), new(11.8, 12), new(21.4, 17.7))),
        F(RoundedPolygon(1.7, new(12.2, 6.3), new(2.6, 12), new(12.2, 17.7))));

    // Shape adapted from the "phone" glyph of Google's Material Icons (Apache License 2.0).
    public static readonly Icon Phone = new(F(
        "M6.62 10.79a15.05 15.05 0 0 0 6.59 6.59l2.2-2.2a1 1 0 0 1 1.02-.24c1.12.37 2.33.57 3.57.57a1 1 0 0 1 1 1V20a1 1 0 0 1-1 1A17 17 0 0 1 3 4a1 1 0 0 1 1-1h3.5a1 1 0 0 1 1 1c0 1.25.2 2.45.57 3.57a1 1 0 0 1-.25 1.02l-2.2 2.2z"));

    public static readonly Icon Mic = new(
        F(RoundRect(8.4, 2.2, 7.2, 12.6, 3.6)),
        S("M5.2 11.2a6.8 6.8 0 0 0 13.6 0", 1.9),
        S("M12 18.2v3.3", 1.9));

    public static readonly Icon MicSlash = new(
        F(RoundRect(8.4, 2.2, 7.2, 12.6, 3.6)),
        S("M5.2 11.2a6.8 6.8 0 0 0 13.6 0", 1.9),
        S("M12 18.2v3.3", 1.9),
        S("M3.6 3.4L20.6 20.4", 2.0));

    public static readonly Icon Video = new(
        F(RoundRect(2.2, 6.4, 13.4, 11.2, 3)),
        F(RoundedPolygon(1.2, new(16.6, 10.2), new(21.8, 7.0), new(21.8, 17.0), new(16.6, 13.8))));

    public static readonly Icon Timer = new(
        S("M12 5.6a7.9 7.9 0 1 1 0 15.8a7.9 7.9 0 1 1 0-15.8z", 2.0),
        F(RoundRect(9.6, 1.6, 4.8, 2.4, 1.1)),
        S("M12 13.5V9.2", 2.2),
        S("M18.4 6.4l1.4-1.4", 2.0));

    public static readonly Icon Speaker = new(
        F(RoundedPolygon(1.0, new(2.6, 9.0), new(7.0, 9.0), new(12.4, 4.6), new(12.4, 19.4), new(7.0, 15.0), new(2.6, 15.0))),
        S("M15.6 8.9a4.4 4.4 0 0 1 0 6.2", 1.9),
        S("M18.3 6.3a8.1 8.1 0 0 1 0 11.4", 1.9));

    public static readonly Icon SpeakerLow = new(
        F(RoundedPolygon(1.0, new(2.6, 9.0), new(7.0, 9.0), new(12.4, 4.6), new(12.4, 19.4), new(7.0, 15.0), new(2.6, 15.0))),
        S("M15.6 8.9a4.4 4.4 0 0 1 0 6.2", 1.9));

    public static readonly Icon SpeakerMute = new(
        F(RoundedPolygon(1.0, new(2.6, 9.0), new(7.0, 9.0), new(12.4, 4.6), new(12.4, 19.4), new(7.0, 15.0), new(2.6, 15.0))),
        S("M15.8 9.4l5.2 5.2M21 9.4l-5.2 5.2", 1.9));

    public static readonly Icon Globe = new(
        S("M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18z", 1.6),
        S("M12 3c-2.5 2.5-3.8 5.5-3.8 9s1.3 6.5 3.8 9", 1.5),
        S("M12 3c2.5 2.5 3.8 5.5 3.8 9s-1.3 6.5-3.8 9", 1.5),
        S("M3.2 12h17.6", 1.5),
        S("M5 7.5h14M5 16.5h14", 1.4));

    public static readonly Icon Bolt = new(F(RoundedPolygon(0.8, new(13.4, 2.0), new(5.0, 13.4), new(11.0, 13.4), new(10.2, 22.0), new(19.0, 10.4), new(12.8, 10.4))));

    public static readonly Icon Headphones = new(
        S("M4.6 15.6V12a7.4 7.4 0 0 1 14.8 0v3.6", 2.0),
        F(RoundRect(2.8, 12.8, 5.0, 8.2, 2.0)),
        F(RoundRect(16.2, 12.8, 5.0, 8.2, 2.0)));

    public static readonly Icon XMark = new(S("M6.8 6.8l10.4 10.4M17.2 6.8L6.8 17.2", 2.6));

    public static readonly Icon Bell = new(
        F("M12 3.2c-3.3 0-5.6 2.5-5.6 5.9v3.6l-1.7 2.9c-.3.5.1 1.1.7 1.1h13.2c.6 0 1-.6.7-1.1l-1.7-2.9V9.1c0-3.4-2.3-5.9-5.6-5.9z"),
        F("M9.5 18.2a2.5 2.5 0 0 0 5 0z"));

    public static readonly Icon Plus = new(S("M12 5.5v13M5.5 12h13", 2.4));

    public static readonly Icon Stop = new(F(RoundRect(6, 6, 12, 12, 2.5)));

    public static readonly Icon Reset = new(
        S("M5.2 12a6.8 6.8 0 1 0 2-4.8", 2.1),
        F(RoundedPolygon(0.8, new(3.6, 4.6), new(9.0, 6.0), new(4.6, 10.0))));

    public static readonly Icon OpenApp = new(
        S("M13.5 4.5h6v6M19.5 4.5l-8 8", 2.0),
        S("M18 14v3.5a2.5 2.5 0 0 1-2.5 2.5h-9A2.5 2.5 0 0 1 4 17.5v-9A2.5 2.5 0 0 1 6.5 6H10", 2.0));

    public static readonly Icon Output = new(
        S("M7.05 16.95a7 7 0 1 1 9.9 0", 1.8),
        S("M9.17 14.83a4 4 0 1 1 5.66 0", 1.8),
        F(RoundedPolygon(1.2, new(12, 14.0), new(16.4, 20.8), new(7.6, 20.8))));

    public static readonly Icon MusicNote = new(
        F(Circle(7.4, 17.6, 3.0)),
        F(Circle(17.4, 15.6, 3.0)),
        F(RoundedPolygon(0.6, new(9.1, 17.6), new(9.1, 5.6), new(20.2, 3.2), new(20.2, 15.6), new(18.4, 15.6), new(18.4, 7.0), new(10.9, 8.6), new(10.9, 17.6))));

    public static readonly Icon Stopwatch = Timer;

    public static readonly Icon Gear = new(F(BuildGear()));

    public static readonly Icon IslandMode = new(
        S("M5.5 3.5h13a3 3 0 0 1 3 3v11a3 3 0 0 1-3 3h-13a3 3 0 0 1-3-3v-11a3 3 0 0 1 3-3z", 1.6),
        F(RoundRect(8.2, 6.0, 7.6, 2.8, 1.4)));

    public static readonly Icon NotchMode = new(
        S("M5.5 3.5h13a3 3 0 0 1 3 3v11a3 3 0 0 1-3 3h-13a3 3 0 0 1-3-3v-11a3 3 0 0 1 3-3z", 1.6),
        F("M7.6 3.4h8.8v2.4a1.6 1.6 0 0 1-1.6 1.6h-5.6a1.6 1.6 0 0 1-1.6-1.6z"));

    private static Geometry BuildGear()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.SetFillRule(FillRule.EvenOdd);
            const int teeth = 8;
            const double ro = 10.2, ri = 7.9;
            var pts = new List<Point>();
            for (var i = 0; i < teeth; i++)
            {
                var b = i * 360.0 / teeth;
                foreach (var (r, a) in new[] { (ri, b - 15.5), (ro, b - 8.5), (ro, b + 8.5), (ri, b + 15.5) })
                {
                    var rad = a * Math.PI / 180;
                    pts.Add(new Point(12 + r * Math.Cos(rad), 12 + r * Math.Sin(rad)));
                }
            }
            ctx.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) ctx.LineTo(p);
            ctx.EndFigure(true);
            const double hole = 3.3;
            ctx.BeginFigure(new Point(12 + hole, 12), true);
            ctx.ArcTo(new Point(12 - hole, 12), new Size(hole, hole), 0, false, SweepDirection.Clockwise);
            ctx.ArcTo(new Point(12 + hole, 12), new Size(hole, hole), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(true);
        }
        return g;
    }

    private static readonly Dictionary<(uint, double), IPen> Pens = new();

    private static IPen Pen(Color c, double w)
    {
        var key = (c.ToUInt32(), Math.Round(w * 100) / 100);
        if (!Pens.TryGetValue(key, out var p))
        {
            p = new Avalonia.Media.Immutable.ImmutablePen(Typo.Brush(c), w, null, PenLineCap.Round, PenLineJoin.Round);
            if (Pens.Count > 300) Pens.Clear();
            Pens[key] = p;
        }
        return p;
    }

    /// <summary>Draws an icon centred in <paramref name="box"/> (square of the given size).</summary>
    public static void Draw(DrawingContext dc, Icon icon, Point center, double size, Color color)
    {
        if (color.A == 0 || size <= 0.5) return;
        var k = size / 24.0;
        var m = Matrix.CreateScale(k, k) * Matrix.CreateTranslation(center.X - size / 2, center.Y - size / 2);
        using (dc.PushTransform(m))
        {
            var brush = Typo.Brush(color);
            foreach (var part in icon.Parts)
            {
                if (part.Fill) dc.DrawGeometry(brush, null, part.Geometry);
                else dc.DrawGeometry(null, Pen(color, part.Stroke), part.Geometry);
            }
        }
    }
}
