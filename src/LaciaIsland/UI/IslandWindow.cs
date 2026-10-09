using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using LaciaIsland.Services;

namespace LaciaIsland.UI;

/// <summary>Something that puts the island on screen and drives its frame loop.</summary>
public interface IIslandHost : IDisposable
{
    void Start();
}

/// <summary>
/// Avalonia-window host (Linux and fallback). Transparent always-on-top window at the top centre of the
/// screen; full frame rate while something animates, low-rate polling otherwise, nothing when idle.
/// The Windows build uses <c>LayeredIslandHost</c> instead.
/// </summary>
public sealed class IslandWindow : Window, IIslandHost
{
    private readonly IslandController _c;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _slow;
    private double _last;
    private bool _loop;
    private int _tick;

    public IslandWindow(IslandController controller)
    {
        _c = controller;
        Title = "Lacia Island";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        Content = _c.View;
        ApplySize();

        _c.FrameRequested += StartLoop;
        _c.Settings.Changed += () =>
        {
            ApplySize();
            PlaceOnScreen();
            StartLoop();
        };
        Opened += (_, _) =>
        {
            PlaceOnScreen();
            StartLoop();
        };
        _slow = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => SlowTick());
    }

    public void Start()
    {
        Show();
        _slow.Start();
    }

    public void Dispose()
    {
        _slow.Stop();
        Close();
    }

    private void ApplySize()
    {
        var size = _c.WindowSize;
        Width = size.Width;
        Height = size.Height;
        _c.Engine.CenterX = size.Width / 2;
    }

    private void PlaceOnScreen()
    {
        try
        {
            var screens = ScreenOrder.Sort(Screens.All);
            if (screens.Count == 0) return;
            Screen screen = Screens.Primary ?? screens[0];
            if (int.TryParse(_c.Settings.Monitor, out var idx) && idx >= 0 && idx < screens.Count) screen = screens[idx];
            var b = screen.Bounds;
            var wpx = (int)Math.Round(Width * screen.Scaling);
            Position = new PixelPoint(b.X + (b.Width - wpx) / 2, b.Y);
        }
        catch (Exception ex) { Log.Error("place", ex); }
    }

    private void StartLoop()
    {
        if (_loop) return;
        _loop = true;
        _last = _clock.Elapsed.TotalSeconds;
        RequestAnimationFrame(OnFrame);
    }

    private double TakeDt()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = now - _last;
        _last = now;
        return dt;
    }

    private void OnFrame(TimeSpan _)
    {
        try { _c.Update(TakeDt()); }
        catch (Exception ex) { Log.Error("frame", ex); }
        if (_c.NeedsFrames) RequestAnimationFrame(OnFrame);
        else _loop = false;
    }

    private void SlowTick()
    {
        _tick++;
        if (_loop) return;
        if (_c.NeedsPeriodic || _c.NeedsFrames)
        {
            try { _c.Update(TakeDt()); }
            catch (Exception ex) { Log.Error("tick", ex); }
            if (_c.NeedsFrames) StartLoop();
        }
        else
        {
            _last = _clock.Elapsed.TotalSeconds;
        }
    }
}
