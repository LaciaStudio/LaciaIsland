using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LaciaIsland.Services;
using LaciaIsland.UI;

namespace LaciaIsland.Platform.Windows;

/// <summary>
/// Puts the island on screen as a classic per-pixel-alpha layered window (UpdateLayeredWindow).
///  • the window is only as big as the island, and fully transparent pixels let clicks through natively;
///  • frames are drawn in software into a small bitmap and handed to DWM — no dependency on any GPU
///    compositor clock, so animations start instantly;
///  • its own high-resolution frame clock runs only while something moves.
/// </summary>
internal sealed class LayeredIslandHost : IIslandHost
{
    private const string ClassName = "LaciaIsland.Layered";
    private static Native.WndProcDelegate? s_proc;
    private static LayeredIslandHost? s_instance;

    private readonly IslandController _c;
    private readonly FrameClock _clock;
    private readonly DispatcherTimer _poll;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private IntPtr _hwnd;
    private double _last;

    private RenderTargetBitmap? _rtb;
    private int _rtbW, _rtbH;
    private IntPtr _memDc, _dib, _oldBmp, _bits;
    private int _dibW, _dibH;

    private double _scale = 1;
    private int _originX, _originY;
    private int _refresh = 60;
    private string _placement = "";
    private bool _shown;
    private int _winX, _winY;
    private bool _tracking;
    private bool _captured;
    private Point? _lastCursor;
    private int _tick;
    private bool _presentFailedLogged;

    // frame timing diagnostics (hitches are always logged, everything with --diag)
    private static readonly bool Diag = Environment.GetCommandLineArgs().Contains("--diag");
    private double _wakeAt = -1;
    private double _burstStart, _burstMaxDt, _burstMaxWork;
    private int _burstFrames;
    private double _statFrom, _statWork, _statMaxWork, _statMaxGap;
    private int _statFrames;

    public LayeredIslandHost(IslandController controller)
    {
        _c = controller;
        _clock = new FrameClock(OnClockTick);
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => Poll());
        CreateNativeWindow();
    }

    // ───────────────────────────── lifetime ─────────────────────────────

    private void CreateNativeWindow()
    {
        s_instance = this;
        var hInstance = Native.GetModuleHandle(null);
        if (s_proc is null)
        {
            s_proc = StaticWndProc;
            var wc = new Native.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc),
                hInstance = hInstance,
                hCursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)), // IDC_ARROW
                lpszClassName = ClassName,
            };
            if (Native.RegisterClassEx(ref wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        var ex = (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE);
        _hwnd = Native.CreateWindowEx(ex, ClassName, "Lacia Island", Native.WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        _memDc = Native.CreateCompatibleDC(IntPtr.Zero);
        Log.Info($"layered host created hwnd=0x{_hwnd.ToInt64():X}");
    }

    public void Start()
    {
        Place();
        _c.FrameRequested += Wake;
        _c.Settings.Changed += () =>
        {
            _placement = "";
            Place();
            Wake();
        };
        Dispatcher.UIThread.Post(Warmup, DispatcherPriority.Background);
        _poll.Start();
        Wake();
    }

    public void Dispose()
    {
        _poll.Stop();
        _clock.Dispose();
        if (_hwnd != IntPtr.Zero)
        {
            Native.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        FreeDib();
        if (_memDc != IntPtr.Zero) Native.DeleteDC(_memDc);
        _memDc = IntPtr.Zero;
        _rtb?.Dispose();
        _rtb = null;
    }

    /// <summary>Renders every kind of content once, off-screen, so the first real animation is not a JIT stall.</summary>
    private void Warmup()
    {
        try
        {
            var t = Stopwatch.StartNew();
            using var rtb = new RenderTargetBitmap(new PixelSize(720, 320), new Vector(96, 96));
            using (var dc = rtb.CreateDrawingContext(true)) _c.Warmup(dc);
            var buf = Marshal.AllocHGlobal(16 * 16 * 4);
            try { rtb.CopyPixels(new PixelRect(0, 0, 16, 16), buf, 16 * 16 * 4, 16 * 4); }
            finally { Marshal.FreeHGlobal(buf); }
            Log.Info($"warm-up {t.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { Log.Error("warmup", ex); }
    }

    // ───────────────────────────── frame loop ─────────────────────────────

    private void Wake()
    {
        if (_clock.Running) return;
        _last = _sw.Elapsed.TotalSeconds;
        _wakeAt = _last;
        _burstFrames = 0;
        _burstMaxDt = 0;
        _burstMaxWork = 0;
        _clock.Hz = _refresh;
        _clock.Start();
    }

    private void OnClockTick()
    {
        if (_burstFrames == 0 && _wakeAt >= 0)
        {
            _burstStart = _sw.Elapsed.TotalSeconds;
            var startDelay = (_burstStart - _wakeAt) * 1000;
            if (startDelay > 60 || Diag) Log.Info($"anim start delay {startDelay:0} ms");
        }
        var dt = Step();
        if (Diag) DiagStats(dt);
        _burstFrames++;
        if (_burstFrames > 1) _burstMaxDt = Math.Max(_burstMaxDt, dt);
        if (!_c.NeedsFrames)
        {
            _clock.Stop();
            var dur = (_sw.Elapsed.TotalSeconds - _burstStart) * 1000;
            if (_burstMaxDt * 1000 > 70 || Diag)
                Log.Info($"anim burst {dur:0} ms, {_burstFrames} frames, worst gap {_burstMaxDt * 1000:0} ms, worst frame work {_burstMaxWork:0.0} ms");
            _wakeAt = -1;
        }
        else _clock.Hz = _c.Engine.IsSettled ? Math.Min(50, _refresh) : Math.Min(144, _refresh);
    }

    private void DiagStats(double dt)
    {
        var now = _sw.Elapsed.TotalSeconds;
        if (_statFrames == 0) _statFrom = now;
        _statFrames++;
        _statMaxGap = Math.Max(_statMaxGap, dt * 1000);
        _statWork += _lastWork;
        _statMaxWork = Math.Max(_statMaxWork, _lastWork);
        if (now - _statFrom >= 5)
        {
            Log.Info($"stats: {_statFrames / (now - _statFrom):0} fps, worst gap {_statMaxGap:0} ms, work avg {_statWork / _statFrames:0.0} / max {_statMaxWork:0.0} ms");
            _statFrames = 0;
            _statWork = _statMaxWork = _statMaxGap = 0;
        }
    }

    private double _lastWork;

    private double Step()
    {
        var now = _sw.Elapsed.TotalSeconds;
        var dt = now - _last;
        _last = now;
        try
        {
            _c.Update(dt);
            Present();
        }
        catch (Exception ex) { Log.Error("frame", ex); }
        _lastWork = (_sw.Elapsed.TotalSeconds - now) * 1000;
        _burstMaxWork = Math.Max(_burstMaxWork, _lastWork);
        return dt;
    }

    private void Poll()
    {
        _tick++;
        try
        {
            if (Native.GetCursorPos(out var p))
            {
                var dip = new Point((p.X - _originX) / _scale, (p.Y - _originY) / _scale);
                var ws = _c.WindowSize;
                var near = dip.X > -40 && dip.Y > -40 && dip.X < ws.Width + 40 && dip.Y < ws.Height + 40;
                if (near)
                {
                    if (_lastCursor is not { } lc || Math.Abs(lc.X - dip.X) + Math.Abs(lc.Y - dip.Y) > 0.4)
                        _c.OnCursor(dip);
                    _lastCursor = dip;
                }
                else if (_lastCursor is not null)
                {
                    _lastCursor = null;
                    if (!_captured) _c.OnCursorLeft();
                }
            }

            if (_tick % 33 == 0)
            {
                EnsureTopmost();
                var fs = WinSystemService.DetectFullscreen(_hwnd);
                if (fs != _c.FullscreenActive)
                {
                    _c.FullscreenActive = fs;
                    Wake();
                }
            }
            if (_tick % 100 == 0) Place();
        }
        catch (Exception ex) { Log.Error("poll", ex); }

        if (_clock.Running) return;
        if (_c.NeedsFrames) Wake();
        else if (_c.NeedsPeriodic && _tick % 3 == 0)
        {
            Step();
            if (_c.NeedsFrames) Wake();
        }
        else _last = _sw.Elapsed.TotalSeconds;
    }

    // ───────────────────────────── placement ─────────────────────────────

    private sealed record MonitorEntry(IntPtr Handle, Native.RECT Bounds, bool Primary, string Device);

    private static List<MonitorEntry> Monitors()
    {
        var list = new List<MonitorEntry>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(), szDevice = "" };
            if (Native.GetMonitorInfoEx(h, ref mi))
                list.Add(new MonitorEntry(h, mi.rcMonitor, (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0, mi.szDevice ?? ""));
            return true;
        }, IntPtr.Zero);
        // primary first, then left-to-right (matches what people call "monitor 1, 2…")
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    private void Place()
    {
        try
        {
            var monitors = Monitors();
            if (monitors.Count == 0) return;
            var mon = monitors[0];
            if (int.TryParse(_c.Settings.Monitor, out var idx) && idx >= 0 && idx < monitors.Count) mon = monitors[idx];

            uint dpi = 96;
            try
            {
                if (Native.GetDpiForMonitor(mon.Handle, 0, out var dx, out _) == 0 && dx > 0) dpi = dx;
            }
            catch { /* shcore missing → 96 */ }
            var scale = dpi / 96.0;
            var ws = _c.WindowSize;
            var vw = (int)Math.Round(ws.Width * scale);
            var b = mon.Bounds;
            var ox = b.Left + (b.Right - b.Left - vw) / 2;
            var oy = b.Top;
            var key = $"{ox}|{oy}|{scale:0.000}|{vw}|{mon.Device}";
            if (key == _placement) return;

            _placement = key;
            _originX = ox;
            _originY = oy;
            _scale = scale;
            _refresh = RefreshRate(mon.Device);
            _c.Engine.CenterX = ws.Width / 2;
            Log.Info($"placed on {mon.Device} origin=({ox},{oy}) scale={scale:0.00} refresh={_refresh}Hz");
            Wake();
        }
        catch (Exception ex) { Log.Error("place", ex); }
    }

    private static int RefreshRate(string device)
    {
        try
        {
            if (string.IsNullOrEmpty(device)) return 60;
            var dc = Native.CreateDC(null, device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return 60;
            var hz = Native.GetDeviceCaps(dc, Native.VREFRESH);
            Native.DeleteDC(dc);
            return hz is >= 30 and <= 360 ? hz : 60;
        }
        catch
        {
            return 60;
        }
    }

    private void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero || !_shown) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    // ───────────────────────────── presenting ─────────────────────────────

    private void Present()
    {
        if (_hwnd == IntPtr.Zero) return;
        var b = _c.ContentBounds;
        if (b.Width < 0.5 || b.Height < 0.5)
        {
            if (_shown)
            {
                Native.ShowWindow(_hwnd, Native.SW_HIDE);
                _shown = false;
            }
            return;
        }

        var x0 = (int)Math.Floor(b.X * _scale);
        var y0 = (int)Math.Floor(b.Y * _scale);
        var x1 = (int)Math.Ceiling(b.Right * _scale);
        var y1 = (int)Math.Ceiling(b.Bottom * _scale);
        var w = Math.Max(1, x1 - x0);
        var h = Math.Max(1, y1 - y0);
        EnsureSurfaces(w, h);

        using (var dc = _rtb!.CreateDrawingContext(true))
        using (dc.PushTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(-x0, -y0)))
        {
            _c.Render(dc);
        }

        var stride = _dibW * 4;
        _rtb.CopyPixels(new PixelRect(0, 0, w, h), _bits, stride * _dibH, stride);

        var dst = new Native.POINT { X = _originX + x0, Y = _originY + y0 };
        var size = new Native.SIZE { cx = w, cy = h };
        var src = new Native.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!Native.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
        {
            if (!_presentFailedLogged)
            {
                _presentFailedLogged = true;
                Log.Warn($"UpdateLayeredWindow failed: {Marshal.GetLastWin32Error()}");
            }
            return;
        }
        _winX = dst.X;
        _winY = dst.Y;
        if (!_shown)
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            _shown = true;
            EnsureTopmost();
        }
    }

    private static int RoundUp(int v, int step) => (v + step - 1) / step * step;

    private void EnsureSurfaces(int w, int h)
    {
        if (_rtb is null || w > _rtbW || h > _rtbH)
        {
            _rtb?.Dispose();
            _rtbW = RoundUp(Math.Max(w, _rtbW), 64);
            _rtbH = RoundUp(Math.Max(h, _rtbH), 32);
            _rtb = new RenderTargetBitmap(new PixelSize(_rtbW, _rtbH), new Vector(96, 96));
        }
        if (_dib == IntPtr.Zero || w > _dibW || h > _dibH)
        {
            FreeDib();
            _dibW = RoundUp(Math.Max(w, _dibW), 64);
            _dibH = RoundUp(Math.Max(h, _dibH), 32);
            var bmi = new Native.BITMAPINFO
            {
                bmiHeader = new Native.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                    biWidth = _dibW,
                    biHeight = -_dibH, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                },
            };
            _dib = Native.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            _oldBmp = Native.SelectObject(_memDc, _dib);
        }
    }

    private void FreeDib()
    {
        if (_dib == IntPtr.Zero) return;
        if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero) Native.SelectObject(_memDc, _oldBmp);
        Native.DeleteObject(_dib);
        _dib = IntPtr.Zero;
        _bits = IntPtr.Zero;
    }

    // ───────────────────────────── input ─────────────────────────────

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var host = s_instance;
        if (host is not null && hwnd == host._hwnd)
        {
            try
            {
                if (host.Handle(msg, wParam, lParam, out var result)) return result;
            }
            catch (Exception ex) { Log.Error("wndproc", ex); }
        }
        return Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private Point ClientToDip(IntPtr lParam)
    {
        var v = lParam.ToInt64();
        var x = (short)(v & 0xFFFF);
        var y = (short)((v >> 16) & 0xFFFF);
        return new Point((_winX + x - _originX) / _scale, (_winY + y - _originY) / _scale);
    }

    private bool Handle(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                result = new IntPtr(Native.MA_NOACTIVATE);
                return true;
            case Native.WM_MOUSEMOVE:
                if (!_tracking)
                {
                    var tme = new Native.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<Native.TRACKMOUSEEVENT>(), dwFlags = Native.TME_LEAVE, hwndTrack = _hwnd,
                    };
                    _tracking = Native.TrackMouseEvent(ref tme);
                }
                var mp = ClientToDip(lParam);
                _lastCursor = mp;
                _c.OnCursor(mp);
                return true;
            case Native.WM_MOUSELEAVE:
                _tracking = false;
                return true;
            case Native.WM_LBUTTONDOWN: Down(lParam, MouseButton.Left); return true;
            case Native.WM_RBUTTONDOWN: Down(lParam, MouseButton.Right); return true;
            case Native.WM_MBUTTONDOWN: Down(lParam, MouseButton.Middle); return true;
            case Native.WM_LBUTTONUP: Up(lParam, MouseButton.Left); return true;
            case Native.WM_RBUTTONUP: Up(lParam, MouseButton.Right); return true;
            case Native.WM_MBUTTONUP: Up(lParam, MouseButton.Middle); return true;
            case Native.WM_MOUSEWHEEL:
                // low word = key state, MK_CONTROL = 0x0008
                _c.OnWheel((short)((wParam.ToInt64() >> 16) & 0xFFFF) / 120.0, (wParam.ToInt64() & 0x0008) != 0);
                return true;
            case Native.WM_CAPTURECHANGED:
                _captured = false;
                return false;
            case Native.WM_DISPLAYCHANGE:
            case Native.WM_DPICHANGED:
            case Native.WM_SETTINGCHANGE:
                Dispatcher.UIThread.Post(() =>
                {
                    _placement = "";
                    Place();
                });
                return false;
        }
        return false;
    }

    private void Down(IntPtr lParam, MouseButton button)
    {
        Native.SetCapture(_hwnd);
        _captured = true;
        _c.OnPress(ClientToDip(lParam), button);
    }

    private void Up(IntPtr lParam, MouseButton button)
    {
        var p = ClientToDip(lParam);
        if (_captured)
        {
            _captured = false;
            Native.ReleaseCapture();
        }
        _c.OnRelease(p, button);
    }
}

/// <summary>
/// Frame clock on a background thread (high-resolution waitable timer, falls back to timeBeginPeriod).
/// Posts at most one pending tick to the UI thread; sleeps completely while stopped.
/// </summary>
internal sealed class FrameClock : IDisposable
{
    private readonly Action _onTick;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _running;
    private volatile bool _disposed;
    private volatile int _hz = 60;
    private int _pending;

    public FrameClock(Action onTick)
    {
        _onTick = onTick;
        var thread = new Thread(Loop) { IsBackground = true, Name = "LaciaIsland.FrameClock", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public bool Running => _running;

    public int Hz
    {
        get => _hz;
        set => _hz = Math.Clamp(value, 20, 240);
    }

    public void Start()
    {
        _running = true;
        _wake.Set();
    }

    public void Stop() => _running = false;

    private void Loop()
    {
        var timer = IntPtr.Zero;
        try { timer = Native.CreateWaitableTimerExW(IntPtr.Zero, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS); }
        catch { timer = IntPtr.Zero; }
        var periodRaised = false;
        var sw = Stopwatch.StartNew();
        var next = 0.0;
        try
        {
            while (!_disposed)
            {
                if (!_running)
                {
                    if (periodRaised)
                    {
                        Native.timeEndPeriod(1);
                        periodRaised = false;
                    }
                    _wake.WaitOne();
                    next = sw.Elapsed.TotalMilliseconds;
                    continue;
                }
                if (timer == IntPtr.Zero && !periodRaised)
                {
                    Native.timeBeginPeriod(1);
                    periodRaised = true;
                }

                var period = 1000.0 / _hz;
                next += period;
                var now = sw.Elapsed.TotalMilliseconds;
                if (next < now - period) next = now; // fell behind → resync, no catch-up burst
                var wait = next - now;
                if (wait > 0.25)
                {
                    if (timer != IntPtr.Zero)
                    {
                        var due = -(long)(wait * 10_000);
                        if (Native.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                            Native.WaitForSingleObject(timer, 1000);
                        else
                            Thread.Sleep((int)Math.Max(1, wait));
                    }
                    else
                    {
                        Thread.Sleep((int)Math.Max(1, Math.Round(wait)));
                    }
                }

                if (_running && Interlocked.Exchange(ref _pending, 1) == 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Interlocked.Exchange(ref _pending, 0);
                        if (_running && !_disposed) _onTick();
                    }, DispatcherPriority.Render);
                }
            }
        }
        catch (Exception ex) { Log.Error("frame clock", ex); }
        finally
        {
            if (timer != IntPtr.Zero) Native.CloseHandle(timer);
            if (periodRaised) Native.timeEndPeriod(1);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _running = false;
        _wake.Set();
    }
}
