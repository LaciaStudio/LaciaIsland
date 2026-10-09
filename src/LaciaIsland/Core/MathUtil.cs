using Avalonia;
using Avalonia.Media;

namespace LaciaIsland.Core;

/// <summary>Small math helpers used everywhere in the animation code.</summary>
public static class M
{
    public const double Tau = Math.PI * 2;

    public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    public static double Clamp(double x, double a, double b) => x < a ? a : x > b ? b : x;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double InvLerp(double a, double b, double v) => Math.Abs(b - a) < 1e-9 ? 0 : (v - a) / (b - a);

    public static double SmoothStep(double e0, double e1, double x)
    {
        var t = Clamp01((x - e0) / (e1 - e0));
        return t * t * (3 - 2 * t);
    }

    public static double EaseOutCubic(double t) { t = Clamp01(t); var u = 1 - t; return 1 - u * u * u; }
    public static double EaseInCubic(double t) { t = Clamp01(t); return t * t * t; }
    public static double EaseInOutCubic(double t)
    {
        t = Clamp01(t);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
    public static double EaseOutBack(double t, double s = 1.4)
    {
        t = Clamp01(t) - 1;
        return t * t * ((s + 1) * t + s) + 1;
    }

    /// <summary>Frame-rate independent exponential approach.</summary>
    public static double Approach(double current, double target, double rate, double dt)
        => target + (current - target) * Math.Exp(-rate * dt);

    public static Point Lerp(Point a, Point b, double t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t));

    public static Rect Lerp(Rect a, Rect b, double t) => new(
        Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Math.Max(0, Lerp(a.Width, b.Width, t)), Math.Max(0, Lerp(a.Height, b.Height, t)));

    public static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(Lerp(a.A, b.A, t)), (byte)Math.Round(Lerp(a.R, b.R, t)),
        (byte)Math.Round(Lerp(a.G, b.G, t)), (byte)Math.Round(Lerp(a.B, b.B, t)));

    public static Color WithAlpha(this Color c, double a) => Color.FromArgb((byte)Math.Round(Clamp01(a) * 255), c.R, c.G, c.B);

    public static double Dist(Point a, Point b) { var dx = a.X - b.X; var dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }

    public static Rect CenteredRect(Point c, double w, double h) => new(c.X - w / 2, c.Y - h / 2, w, h);

    public static Rect Inflate(this Rect r, double d) => new(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);

    public static bool ContainsInclusive(this Rect r, Point p) =>
        p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom;

    /// <summary>Deterministic hash-noise in [0,1).</summary>
    public static double Hash(double x)
    {
        var s = Math.Sin(x * 127.1 + 311.7) * 43758.5453123;
        return s - Math.Floor(s);
    }

    /// <summary>Smooth 1D value noise in [0,1].</summary>
    public static double Noise(double x)
    {
        var i = Math.Floor(x);
        var f = x - i;
        var u = f * f * (3 - 2 * f);
        return Lerp(Hash(i), Hash(i + 1), u);
    }
}
