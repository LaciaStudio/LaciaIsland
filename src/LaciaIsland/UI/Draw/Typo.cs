using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LaciaIsland.UI.Draw;

public enum HAlign { Left, Center, Right }

/// <summary>Text drawing with caching, cap-height centring and tabular digits.</summary>
public static class Typo
{
    public static readonly FontFamily Family = new("fonts:Inter#Inter, $Default");
    private const double CapHeight = 0.727;

    private static readonly Dictionary<(string, double, FontWeight, uint, double), FormattedText> Cache = new();
    private static readonly Dictionary<(double, FontWeight), double> DigitWidth = new();
    private static readonly Dictionary<uint, IImmutableSolidColorBrush> Brushes = new();

    public static IImmutableSolidColorBrush Brush(Color c)
    {
        var k = c.ToUInt32();
        if (!Brushes.TryGetValue(k, out var b))
        {
            b = new ImmutableSolidColorBrush(c);
            if (Brushes.Count > 400) Brushes.Clear();
            Brushes[k] = b;
        }
        return b;
    }

    public static FormattedText Get(string text, double size, FontWeight weight, Color color, double maxWidth = 0)
    {
        size = Math.Round(size * 4) / 4;
        maxWidth = maxWidth > 0 ? Math.Round(maxWidth) : 0;
        var key = (text, size, weight, color.ToUInt32(), maxWidth);
        if (Cache.TryGetValue(key, out var ft)) return ft;
        ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Family, FontStyle.Normal, weight), Math.Max(1, size), Brush(color));
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        if (Cache.Count > 600) Cache.Clear();
        Cache[key] = ft;
        return ft;
    }

    public static double Width(string text, double size, FontWeight weight, double maxWidth = 0)
        => Get(text, size, weight, Colors.White, maxWidth).Width;

    /// <summary>Draws a single line vertically centred on the cap height.</summary>
    public static double Draw(DrawingContext dc, string text, double size, FontWeight weight, Color color,
        double x, double centerY, double maxWidth = 0, HAlign align = HAlign.Left)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return 0;
        var ft = Get(text, size, weight, color, maxWidth);
        var w = ft.Width;
        var ox = align switch { HAlign.Center => x - w / 2, HAlign.Right => x - w, _ => x };
        var oy = centerY + size * CapHeight / 2 - ft.Baseline;
        dc.DrawText(ft, new Point(ox, oy));
        return w;
    }

    private static double DigitCell(double size, FontWeight weight)
    {
        var key = (Math.Round(size * 4) / 4, weight);
        if (DigitWidth.TryGetValue(key, out var w)) return w;
        w = 0;
        for (var c = '0'; c <= '9'; c++) w = Math.Max(w, Width(c.ToString(), size, weight));
        DigitWidth[key] = w;
        return w;
    }

    public static double TabularWidth(string text, double size, FontWeight weight)
    {
        var cell = DigitCell(size, weight);
        double w = 0;
        foreach (var ch in text) w += char.IsDigit(ch) ? cell : Width(ch.ToString(), size, weight);
        return w;
    }

    /// <summary>Draws text with fixed-width digits so counters do not wobble.</summary>
    public static double DrawTabular(DrawingContext dc, string text, double size, FontWeight weight, Color color,
        double x, double centerY, HAlign align = HAlign.Left)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return 0;
        var cell = DigitCell(size, weight);
        var total = TabularWidth(text, size, weight);
        var cx = align switch { HAlign.Center => x - total / 2, HAlign.Right => x - total, _ => x };
        foreach (var ch in text)
        {
            var s = ch.ToString();
            var ft = Get(s, size, weight, color);
            var oy = centerY + size * CapHeight / 2 - ft.Baseline;
            if (char.IsDigit(ch))
            {
                dc.DrawText(ft, new Point(cx + (cell - ft.Width) / 2, oy));
                cx += cell;
            }
            else
            {
                dc.DrawText(ft, new Point(cx, oy));
                cx += ft.Width;
            }
        }
        return total;
    }

    public static string FormatTime(TimeSpan t, bool forceHours = false)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var total = (long)Math.Floor(t.TotalSeconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 || forceHours ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }
}
