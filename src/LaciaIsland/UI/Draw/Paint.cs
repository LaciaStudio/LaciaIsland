using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LaciaIsland.Core;

namespace LaciaIsland.UI.Draw;

/// <summary>iOS-ish palette.</summary>
public static class Pal
{
    public static readonly Color White = Colors.White;
    public static readonly Color Gray = Color.FromRgb(0x98, 0x98, 0x9F);
    public static readonly Color Gray2 = Color.FromRgb(0x63, 0x63, 0x6A);
    public static readonly Color Track = Color.FromRgb(0x3A, 0x3A, 0x3E);
    public static readonly Color Fill = Color.FromRgb(0x2A, 0x2A, 0x2E);
    public static readonly Color FillHover = Color.FromRgb(0x38, 0x38, 0x3D);
    public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color GreenDark = Color.FromRgb(0x1F, 0x5F, 0x2E);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color OrangeDark = Color.FromRgb(0x4A, 0x31, 0x08);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);
    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
    public static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color Lens = Color.FromRgb(0x14, 0x16, 0x2A);
}

public static class Paint
{
    /// <summary>Symmetric rounded bars (the "waveform" glyph).</summary>
    public static void Bars(DrawingContext dc, Rect area, ReadOnlySpan<float> levels, Color top, Color bottom,
        double barW, double gap, double minH, double alpha = 1)
    {
        var n = levels.Length;
        if (n == 0 || alpha <= 0.01) return;
        var total = n * barW + (n - 1) * gap;
        var x = area.Center.X - total / 2;
        var cy = area.Center.Y;
        IBrush brush = top == bottom
            ? Typo.Brush(top.WithAlpha(alpha * top.A / 255.0))
            : new ImmutableLinearGradientBrush(new[]
                {
                    new ImmutableGradientStop(0, top.WithAlpha(alpha * top.A / 255.0)),
                    new ImmutableGradientStop(1, bottom.WithAlpha(alpha * bottom.A / 255.0)),
                }, opacity: 1,
                startPoint: new RelativePoint(0, area.Top, RelativeUnit.Absolute),
                endPoint: new RelativePoint(0, area.Bottom, RelativeUnit.Absolute));
        for (var i = 0; i < n; i++)
        {
            var h = Math.Max(minH, Math.Min(area.Height, levels[i] * area.Height));
            var r = new Rect(x + i * (barW + gap), cy - h / 2, barW, h);
            dc.DrawRectangle(brush, null, new RoundedRect(r, barW / 2));
        }
    }

    /// <summary>Album art (or a placeholder) clipped to a rounded rect.</summary>
    public static void Art(DrawingContext dc, Bitmap? art, Rect rect, double radius, Color placeholder, double alpha = 1)
    {
        if (rect.Width < 1 || alpha <= 0.01) return;
        using (dc.PushOpacity(alpha))
        {
            if (art is null)
            {
                dc.DrawRectangle(Typo.Brush(placeholder), null, new RoundedRect(rect, radius));
                Icons.Draw(dc, Icons.MusicNote, rect.Center, rect.Width * 0.55, Color.FromArgb(200, 255, 255, 255));
                return;
            }
            using (dc.PushClip(new RoundedRect(rect, radius)))
            {
                var src = new Rect(0, 0, art.Size.Width, art.Size.Height);
                dc.DrawImage(art, src, rect);
            }
        }
    }

    public static void Circle(DrawingContext dc, Point c, double r, Color color)
    {
        if (r <= 0 || color.A == 0) return;
        dc.DrawEllipse(Typo.Brush(color), null, c, r, r);
    }

    /// <summary>Progress ring (starts at 12 o'clock, clockwise).</summary>
    public static void Ring(DrawingContext dc, Point c, double r, double thickness, double progress, Color track, Color fill)
    {
        if (r <= 0) return;
        if (track.A > 0)
            dc.DrawEllipse(null, new ImmutablePen(Typo.Brush(track), thickness), c, r, r);
        progress = M.Clamp01(progress);
        if (progress <= 0.001) return;
        var pen = new ImmutablePen(Typo.Brush(fill), thickness, null, PenLineCap.Round);
        if (progress >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        var a0 = -Math.PI / 2;
        var a1 = a0 + progress * Math.PI * 2;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, progress > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, g);
    }

    /// <summary>Battery glyph with fill level.</summary>
    public static void Battery(DrawingContext dc, Rect r, double level, Color fill, bool bolt, double alpha = 1)
    {
        using var _ = dc.PushOpacity(alpha);
        var bodyW = r.Width * 0.9;
        var body = new Rect(r.X, r.Y, bodyW, r.Height);
        var rad = r.Height * 0.3;
        dc.DrawRectangle(null, new ImmutablePen(Typo.Brush(Color.FromArgb(110, 255, 255, 255)), Math.Max(1, r.Height * 0.11)), new RoundedRect(body, rad));
        var nub = new Rect(body.Right + r.Width * 0.025, r.Y + r.Height * 0.32, r.Width * 0.06, r.Height * 0.36);
        dc.DrawRectangle(Typo.Brush(Color.FromArgb(110, 255, 255, 255)), null, new RoundedRect(nub, nub.Width / 2));
        var inset = r.Height * 0.19;
        var inner = new Rect(body.X + inset, body.Y + inset, Math.Max(0, (bodyW - 2 * inset) * M.Clamp01(level)), body.Height - 2 * inset);
        if (inner.Width > 0.5)
            dc.DrawRectangle(Typo.Brush(fill), null, new RoundedRect(inner, Math.Max(0.5, rad - inset * 0.8)));
        if (bolt)
            Icons.Draw(dc, Icons.Bolt, new Point(body.Center.X, body.Center.Y), r.Height * 0.95, Colors.White);
    }

    /// <summary>Round glass button with hover/press feedback.</summary>
    public static void RoundButton(DrawingContext dc, Point c, double r, Color bg, Icon? icon, Color iconColor, double iconSize,
        bool hover, bool pressed)
    {
        var scale = pressed ? 0.9 : 1.0;
        var rr = r * scale;
        var b = bg;
        if (hover) b = M.Lerp(bg, Colors.White, 0.12);
        Circle(dc, c, rr, b);
        if (icon is not null) Icons.Draw(dc, icon, c, iconSize * scale, iconColor);
    }

    /// <summary>Radius of the soft hover circle behind a glyph button — snug around the glyph.</summary>
    public static double HighlightRadius(double glyphSize) => glyphSize * 0.66;

    public static Color HighlightColor(bool pressed) => Color.FromArgb((byte)(pressed ? 54 : 30), 255, 255, 255);

    /// <summary>Plain icon button: a soft circular highlight appears on hover.</summary>
    public static void GlyphButton(DrawingContext dc, Point c, Icon icon, double size, Color color, bool hover, bool pressed)
    {
        if (hover || pressed)
            Circle(dc, c, HighlightRadius(size), HighlightColor(pressed));
        Icons.Draw(dc, icon, c, size * (pressed ? 0.88 : 1), color);
    }

    public static void Capsule(DrawingContext dc, Rect r, Color bg) =>
        dc.DrawRectangle(Typo.Brush(bg), null, new RoundedRect(r, r.Height / 2));

    /// <summary>Runs a drawing action scaled around a point.</summary>
    public static IDisposable ScaleAround(DrawingContext dc, Point c, double s)
    {
        if (Math.Abs(s - 1) < 1e-4) return Nop.Instance;
        var m = Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(c.X, c.Y);
        return dc.PushTransform(m);
    }

    private sealed class Nop : IDisposable
    {
        public static readonly Nop Instance = new();
        public void Dispose() { }
    }
}
