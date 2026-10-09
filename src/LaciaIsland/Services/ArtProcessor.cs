using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace LaciaIsland.Services;

/// <summary>Square-crops album art and extracts an accent colour for the waveform (like iOS does).</summary>
public static class ArtProcessor
{
    public sealed record Result(Bitmap Bitmap, Color Accent);

    public static Result? Process(byte[] data, int size = 192)
    {
        try
        {
            using var src = SKBitmap.Decode(data);
            if (src is null || src.Width < 2 || src.Height < 2) return null;
            return Process(src, size);
        }
        catch (Exception ex)
        {
            Log.Error("art decode", ex);
            return null;
        }
    }

    public static Result Process(SKBitmap src, int size = 192)
    {
        var side = Math.Min(src.Width, src.Height);
        var crop = new SKRectI((src.Width - side) / 2, (src.Height - side) / 2, (src.Width - side) / 2 + side, (src.Height - side) / 2 + side);
        using var square = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(square))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(src, crop, new SKRect(0, 0, size, size), paint);
        }

        var accent = ExtractAccent(square);

        using var image = SKImage.FromBitmap(square);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(png.ToArray());
        return new Result(new Bitmap(ms), accent);
    }

    public static Color ExtractAccent(SKBitmap bmp)
    {
        using var small = bmp.Resize(new SKImageInfo(24, 24), SKFilterQuality.Medium) ?? bmp;
        const int bins = 18;
        var weight = new double[bins];
        var sumR = new double[bins];
        var sumG = new double[bins];
        var sumB = new double[bins];
        double satTotal = 0;
        var count = 0;
        for (var y = 0; y < small.Height; y++)
        for (var x = 0; x < small.Width; x++)
        {
            var c = small.GetPixel(x, y);
            c.ToHsv(out var h, out var s, out var v);
            s /= 100f;
            v /= 100f;
            satTotal += s;
            count++;
            var w = s * s * (0.25 + v) * (v < 0.15 ? 0.1 : 1);
            var bin = Math.Clamp((int)(h / 360f * bins), 0, bins - 1);
            weight[bin] += w;
            sumR[bin] += c.Red * w;
            sumG[bin] += c.Green * w;
            sumB[bin] += c.Blue * w;
        }

        var best = 0;
        for (var i = 1; i < bins; i++) if (weight[i] > weight[best]) best = i;
        if (count == 0 || satTotal / count < 0.08 || weight[best] < 0.5)
            return Color.FromRgb(0xE8, 0xE8, 0xED);

        var r = sumR[best] / weight[best];
        var g = sumG[best] / weight[best];
        var b = sumB[best] / weight[best];
        var col = new SKColor((byte)r, (byte)g, (byte)b);
        col.ToHsv(out var hh, out var ss, out var vv);
        ss = Math.Clamp(ss, 35f, 85f);
        vv = Math.Max(vv, 88f);
        var o = SKColor.FromHsv(hh, ss, vv);
        return Color.FromRgb(o.Red, o.Green, o.Blue);
    }
}
