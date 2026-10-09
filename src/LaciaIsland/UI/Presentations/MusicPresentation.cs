using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.Services;
using LaciaIsland.UI.Draw;

namespace LaciaIsland.UI.Presentations;

/// <summary>Now playing: album art + waveform in compact, full player when expanded (shared-element morph).</summary>
public sealed class MusicPresentation : Presentation
{
    public MusicPresentation(IslandController c) : base(c) { }

    public override string Key => "music";

    private MediaInfo? Info => C.Svc.Media.Current;
    private readonly BarAnimator _bars = new(6, 1.7);
    private readonly float[] _bands = new float[12];

    private string _trackKey = "";
    private Bitmap? _art;
    private Bitmap? _prevArt;
    private Color _accent = Colors.White;
    private double _artFade = 1;
    private double _playMorph;
    private double _trailingAlpha = 1;
    private double _e;
    private double _barHover;
    private double? _seekFraction;
    private double _seekHoldUntil;

    // layout (computed per frame)
    private Rect _artRect, _waveRect, _barRect;
    private double _artRadius, _barW, _barGap;
    private double _x0, _y0, _bw, _textX, _textMax;
    private Point _prevC, _playC, _nextC, _outC;

    public override bool Animating => Info?.IsPlaying == true || _artFade < 1 || Math.Abs(_playMorph - (Info?.IsPlaying == true ? 1 : 0)) > 0.01;

    protected override Size BaseSize(IslandMode mode, Variant v, double s) => (mode, v) switch
    {
        (IslandMode.Island, Variant.Compact) => Z(300, 34, s),
        (IslandMode.Island, Variant.Minimal) => Z(190, 34, s),
        (IslandMode.Island, Variant.Expanded) => Z(420, 174, s),
        (IslandMode.Notch, Variant.Compact) => Z(320, 32, s),
        (IslandMode.Notch, Variant.Minimal) => Z(300, 32, s),
        _ => Z(440, 172, s),
    };

    public override double TargetRadius(IslandMode mode, Variant v, Size size, double s) =>
        v == Variant.Expanded ? (mode == IslandMode.Island ? 44 * s : 36 * s) : base.TargetRadius(mode, v, size, s);

    protected override void OnUpdate()
    {
        var info = Info;
        if (info is not null && info.TrackKey != _trackKey)
        {
            if (_trackKey.Length > 0 && _art is not null) { _prevArt = _art; _artFade = 0; }
            _trackKey = info.TrackKey;
        }
        if (info is not null)
        {
            if (!ReferenceEquals(_art, info.Art) && _artFade >= 1 && _prevArt is null && _art is not null && info.Art is not null && _trackKey == info.TrackKey)
            {
                // same track, artwork arrived later → quick crossfade
                _prevArt = _art;
                _artFade = 0;
            }
            _art = info.Art;
            _accent = info.Accent;
        }
        _artFade = Math.Min(1, _artFade + Dt / 0.4);
        if (_artFade >= 1) _prevArt = null;

        var playing = info?.IsPlaying == true;
        _playMorph = M.Approach(_playMorph, playing ? 1 : 0, 16, Dt);

        float[]? real = null;
        if (playing && C.Settings.RealVisualizer && C.Svc.Audio.ReadBands(_bands)) real = _bands;
        _bars.Step(Dt, playing, real);

        _trailingAlpha = M.Approach(_trailingAlpha, Variant == Variant.Minimal ? 0 : 1, 14, Dt);
        _barHover = M.Approach(_barHover, IsHover("progress") || _seekFraction is not null ? 1 : 0, 18, Dt);

        _e = ExpandT();
        DetailAlpha = M.SmoothStep(0.5, 1.0, _e);
        DetailBlur = (1 - DetailAlpha) * 9 * S;

        Layout();
    }

    private void Layout()
    {
        var e = M.Clamp(_e, 0, 1.06);
        var pad = Mode == IslandMode.Island ? 6 * S : 10 * S;

        // compact positions (follow the live frame edges)
        var artC = new Rect(V.Left + pad, Cy - 11 * S, 22 * S, 22 * S);
        var waveWc = 6 * 2.6 * S + 5 * 2.2 * S;
        var waveC = new Rect(V.Right - 12 * S - waveWc, Cy - 8 * S, waveWc, 16 * S);

        // expanded positions (a block centred in the frame)
        _bw = ExpandedSize.Width;
        _x0 = BlockLeft(_bw);
        _y0 = V.Top;
        var artE = new Rect(_x0 + 22 * S, _y0 + 22 * S, 58 * S, 58 * S);
        var waveWe = 6 * 3 * S + 5 * 2.6 * S;
        var waveE = new Rect(_x0 + _bw - 22 * S - waveWe, _y0 + 40 * S, waveWe, 22 * S);

        _artRect = M.Lerp(artC, artE, e);
        _artRadius = M.Lerp(6 * S, 13 * S, M.Clamp01(e));
        _waveRect = M.Lerp(waveC, waveE, e);
        _barW = M.Lerp(2.6 * S, 3 * S, M.Clamp01(e));
        _barGap = M.Lerp(2.2 * S, 2.6 * S, M.Clamp01(e));

        _textX = _x0 + 94 * S;
        _textMax = _bw - 94 * S - 22 * S - waveWe - 12 * S;
        var thick = M.Lerp(6, 9, _barHover) * S;
        _barRect = new Rect(_x0 + 62 * S, _y0 + 106 * S - thick / 2, _bw - 124 * S, thick);
        var cy = _y0 + 145 * S;
        _playC = new Point(_x0 + _bw / 2, cy);
        _prevC = new Point(_playC.X - 66 * S, cy);
        _nextC = new Point(_playC.X + 66 * S, cy);
        _outC = new Point(_x0 + _bw - 38 * S, cy);

        if (Variant == Variant.Expanded && _e > 0.7)
        {
            HitCircle("prev", _prevC, 22 * S);
            HitCircle("play", _playC, 24 * S);
            HitCircle("next", _nextC, 22 * S);
            HitCircle("output", _outC, 18 * S);
            Hit("progress", new Rect(_barRect.X - 6 * S, _y0 + 92 * S, _barRect.Width + 12 * S, 28 * S));
            Hit("art", artE);
        }
    }

    protected override void OnRenderBase(DrawingContext dc)
    {
        var info = Info;
        if (info is null && _art is null) return;

        // album art (shared element between compact and expanded)
        var placeholder = Color.FromRgb(0x2C, 0x2C, 0x30);
        if (_prevArt is not null && _artFade < 1)
        {
            Paint.Art(dc, _prevArt, _artRect, _artRadius, placeholder, 1 - _artFade);
            using (Paint.ScaleAround(dc, _artRect.Center, M.Lerp(0.82, 1, M.EaseOutBack(_artFade))))
                Paint.Art(dc, _art, _artRect, _artRadius, placeholder, _artFade);
        }
        else
        {
            Paint.Art(dc, _art, _artRect, _artRadius, placeholder);
        }

        // waveform tinted with the artwork colour
        if (_trailingAlpha > 0.01)
        {
            var top = M.Lerp(_accent, Colors.White, 0.3);
            Paint.Bars(dc, _waveRect, _bars.Levels, top, _accent, _barW, _barGap, _barW, _trailingAlpha);
        }
    }

    public override void RenderDetail(DrawingContext dc)
    {
        var info = Info;
        if (info is null || DetailAlpha <= 0.01) return;

        Typo.Draw(dc, string.IsNullOrWhiteSpace(info.Title) ? "Без названия" : info.Title, 16 * S, FontWeight.SemiBold, Colors.White,
            _textX, _y0 + 40 * S, _textMax);
        Typo.Draw(dc, info.Artist, 14 * S, FontWeight.Normal, Pal.Gray, _textX, _y0 + 62 * S, _textMax);

        // progress
        var duration = info.Duration;
        var pos = info.LivePosition(Now);
        double frac;
        if (_seekFraction is { } sf) frac = sf;
        else if (Time < _seekHoldUntil && _heldFraction is { } hf) frac = hf;
        else frac = duration.TotalSeconds > 0 ? pos.TotalSeconds / duration.TotalSeconds : 0;
        frac = M.Clamp01(frac);
        if (_seekFraction is not null || Time < _seekHoldUntil) pos = TimeSpan.FromSeconds(duration.TotalSeconds * frac);

        var hasTimeline = duration.TotalSeconds > 0.5;
        var tSize = 12 * S;
        var labelCy = _y0 + 106 * S;
        Typo.DrawTabular(dc, hasTimeline ? Typo.FormatTime(pos) : "--:--", tSize, FontWeight.Medium, Pal.Gray, _x0 + 22 * S, labelCy);
        Typo.DrawTabular(dc, hasTimeline ? "-" + Typo.FormatTime(duration - pos) : "--:--", tSize, FontWeight.Medium, Pal.Gray,
            _x0 + _bw - 22 * S, labelCy, HAlign.Right);

        var track = new RoundedRect(_barRect, _barRect.Height / 2);
        dc.DrawRectangle(Typo.Brush(Pal.Track), null, track);
        if (hasTimeline && frac > 0)
        {
            var fill = new Rect(_barRect.X, _barRect.Y, Math.Max(_barRect.Height, _barRect.Width * frac), _barRect.Height);
            dc.DrawRectangle(Typo.Brush(_barHover > 0.5 ? Colors.White : Color.FromRgb(0xF2, 0xF2, 0xF5)), null, new RoundedRect(fill, _barRect.Height / 2));
        }

        // transport controls
        Paint.GlyphButton(dc, _prevC, Icons.Backward, 30 * S, info.CanPrev ? Colors.White : Pal.Gray2, IsHover("prev"), IsPress("prev"));
        Paint.GlyphButton(dc, _nextC, Icons.Forward, 30 * S, info.CanNext ? Colors.White : Pal.Gray2, IsHover("next"), IsPress("next"));
        var press = IsPress("play") ? 0.88 : 1.0;
        if (IsHover("play") || IsPress("play"))
            Paint.Circle(dc, _playC, Paint.HighlightRadius(34 * S), Paint.HighlightColor(IsPress("play")));
        if (_playMorph < 0.99)
            Icons.Draw(dc, Icons.Play, _playC, 34 * S * press * M.Lerp(1, 0.5, _playMorph), Colors.White.WithAlpha(1 - _playMorph));
        if (_playMorph > 0.01)
            Icons.Draw(dc, Icons.Pause, _playC, 34 * S * press * M.Lerp(0.5, 1, _playMorph), Colors.White.WithAlpha(_playMorph));
        Paint.GlyphButton(dc, _outC, Icons.Output, 24 * S, Color.FromRgb(0xC8, 0xC8, 0xCE), IsHover("output"), IsPress("output"));
    }

    private double? _heldFraction;

    public override void RenderMinimal(DrawingContext dc, Rect r)
    {
        var d = Math.Min(r.Width, r.Height) * 0.72;
        var rect = M.CenteredRect(r.Center, d, d);
        Paint.Art(dc, Info?.Art ?? _art, rect, d / 2, Color.FromRgb(0x2C, 0x2C, 0x30));
    }

    public override void OnClick(string id)
    {
        var media = C.Svc.Media;
        switch (id)
        {
            case "play": media.PlayPause(); break;
            case "next": media.Next(); C.Engine.KickScale(0.18); break;
            case "prev": media.Previous(); C.Engine.KickScale(0.18); break;
            case "output": C.Svc.Audio.OpenSoundSettings(); break;
            case "art": media.ActivateSource(); break;
        }
    }

    public override void OnDrag(string id, Point p, int phase)
    {
        if (id != "progress") return;
        var info = Info;
        if (info is null || info.Duration.TotalSeconds < 0.5 || !info.CanSeek) return;
        var frac = M.Clamp01((p.X - _barRect.X) / Math.Max(1, _barRect.Width));
        if (phase < 2)
        {
            _seekFraction = frac;
        }
        else
        {
            _seekFraction = null;
            _heldFraction = frac;
            _seekHoldUntil = Time + 1.5;
            C.Svc.Media.Seek(TimeSpan.FromSeconds(info.Duration.TotalSeconds * frac));
        }
    }
}
