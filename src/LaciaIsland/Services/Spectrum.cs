namespace LaciaIsland.Services;

/// <summary>
/// Lock-free-ish ring buffer of mono samples + FFT → log-spaced band levels in [0,1].
/// Fed from the WASAPI loopback thread, read from the UI thread.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int FftSize = 2048;
    private readonly float[] _ring = new float[FftSize * 2];
    private int _write;
    private readonly object _gate = new();
    private long _lastWriteTicks;

    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly double[] _window = new double[FftSize];
    public int SampleRate { get; set; } = 48000;

    public SpectrumAnalyzer()
    {
        for (var i = 0; i < FftSize; i++) _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
    }

    public bool IsFresh => Environment.TickCount64 - Interlocked.Read(ref _lastWriteTicks) < 250;

    public void Push(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels < 1) return;
        lock (_gate)
        {
            for (var i = 0; i + channels - 1 < interleaved.Length; i += channels)
            {
                float s = 0;
                for (var c = 0; c < channels; c++) s += interleaved[i + c];
                _ring[_write] = s / channels;
                _write = (_write + 1) % _ring.Length;
            }
        }
        Interlocked.Exchange(ref _lastWriteTicks, Environment.TickCount64);
    }

    /// <summary>Computes <paramref name="bands"/>.Length log-spaced bands between 50 Hz and 12 kHz.</summary>
    public void Compute(float[] bands)
    {
        lock (_gate)
        {
            var start = (_write - FftSize + _ring.Length) % _ring.Length;
            for (var i = 0; i < FftSize; i++)
            {
                _re[i] = _ring[(start + i) % _ring.Length] * _window[i];
                _im[i] = 0;
            }
        }
        Fft(_re, _im);

        var n = bands.Length;
        const double fMin = 50, fMax = 12000;
        var binHz = (double)SampleRate / FftSize;
        for (var b = 0; b < n; b++)
        {
            var f0 = fMin * Math.Pow(fMax / fMin, (double)b / n);
            var f1 = fMin * Math.Pow(fMax / fMin, (double)(b + 1) / n);
            var i0 = Math.Max(1, (int)(f0 / binHz));
            var i1 = Math.Min(FftSize / 2 - 1, Math.Max(i0 + 1, (int)(f1 / binHz)));
            double peak = 0;
            for (var i = i0; i <= i1; i++)
            {
                var m = Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]);
                if (m > peak) peak = m;
            }
            // normalise: FFT magnitude of a full-scale sine with hann window ≈ N/4
            var db = 20 * Math.Log10(peak / (FftSize / 4.0) + 1e-9);
            // gentle tilt so the highs are not always dead
            db += 2.2 * b / Math.Max(1, n - 1) * 3;
            bands[b] = (float)Math.Clamp((db + 62) / 52, 0, 1);
        }
    }

    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wr = Math.Cos(ang);
            var wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var ur = re[i + k];
                    var ui = im[i + k];
                    var vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci;
                    var vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                    re[i + k] = ur + vr;
                    im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr;
                    im[i + k + len / 2] = ui - vi;
                    var t = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = t;
                }
            }
        }
    }
}

/// <summary>
/// Turns raw band levels (real or synthetic) into smooth bar heights with fast attack / slow release.
/// </summary>
public sealed class BarAnimator
{
    private readonly float[] _raw;
    private readonly float[] _smooth;
    private readonly int[] _order;
    private double _t;
    private readonly double _seed;

    public float[] Levels { get; }

    public BarAnimator(int bars, double seed = 0)
    {
        _raw = new float[bars];
        _smooth = new float[bars];
        Levels = new float[bars];
        _seed = seed;
        // spread the bass into the middle bars: looks more like the iOS glyph
        _order = Enumerable.Range(0, bars)
            .OrderBy(i => Math.Abs(i - (bars - 1) / 2.0))
            .ToArray();
    }

    /// <param name="active">playing; when false bars relax to dots.</param>
    /// <param name="real">optional real band data (already 0..1).</param>
    public void Step(double dt, bool active, float[]? real)
    {
        _t += dt;
        var n = Levels.Length;
        if (active && real is not null)
        {
            // map sorted-by-frequency bands → centre-out positions
            for (var i = 0; i < n; i++)
            {
                var src = real[Math.Min(real.Length - 1, i * real.Length / n)];
                _raw[_order[i]] = src;
            }
        }
        else if (active)
        {
            var beat = Math.Pow(0.5 + 0.5 * Math.Sin(_t * Math.PI * 2 * 1.9), 3);
            for (var i = 0; i < n; i++)
            {
                var a = Core.M.Noise(_t * (2.3 + i * 0.37) + i * 17.3 + _seed);
                var b = Core.M.Noise(_t * 6.1 + i * 5.1 + _seed * 3);
                var center = 1 - Math.Abs(i - (n - 1) / 2.0) / Math.Max(1, n / 2.0) * 0.35;
                _raw[i] = (float)Math.Clamp((0.18 + 0.55 * a + 0.25 * b + 0.25 * beat) * center, 0.08, 1);
            }
        }
        else
        {
            for (var i = 0; i < n; i++) _raw[i] = 0;
        }

        for (var i = 0; i < n; i++)
        {
            var target = _raw[i];
            var rate = target > _smooth[i] ? 28.0 : 9.0;
            _smooth[i] = (float)(target + (_smooth[i] - target) * Math.Exp(-rate * dt));
            Levels[i] = _smooth[i];
        }
    }
}
