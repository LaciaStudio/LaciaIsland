using System.Diagnostics;
using System.Text;
using LaciaIsland.Services;
using Microsoft.Win32;

namespace LaciaIsland.Platform.Windows;

internal sealed class WinSystemService : ISystemService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "LaciaIsland";
    private static byte[]? _chime;

    public bool IsFullscreenAppActive() => DetectFullscreen(IntPtr.Zero);

    /// <summary>Fullscreen games / video / presentations (exclusive or borderless).</summary>
    public static bool DetectFullscreen(IntPtr own)
    {
        try
        {
            if (Native.SHQueryUserNotificationState(out var state) == 0)
            {
                // 2 = busy (fullscreen app), 3 = D3D fullscreen, 4 = presentation mode
                if (state is 3 or 4) return true;
            }

            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == own || fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
            var cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, cls.Capacity);
            var c = cls.ToString();
            if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
            if (Native.IsZoomed(fg)) return false; // a maximised window is not "fullscreen" (auto-hide taskbar case)
            if (!Native.GetWindowRect(fg, out var r)) return false;
            var mon = Native.MonitorFromWindow(fg, 2);
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;
            var m = mi.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }
        catch
        {
            return false;
        }
    }

    public void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("open uri", ex); }
    }

    public void PlayAlert()
    {
        try
        {
            _chime ??= BuildChime();
            Native.PlaySound(_chime, IntPtr.Zero, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
        }
        catch (Exception ex) { Log.Error("sound", ex); }
    }

    /// <summary>A soft three-note marimba-ish chime, synthesised so no sound files are needed.</summary>
    private static byte[] BuildChime()
    {
        const int rate = 44100;
        var notes = new[] { (f: 1046.5, t: 0.0), (f: 1318.5, t: 0.16), (f: 1568.0, t: 0.32) };
        var len = (int)(rate * 1.4);
        var samples = new float[len];
        foreach (var (f, t) in notes)
        {
            var start = (int)(t * rate);
            for (var i = 0; i < len - start; i++)
            {
                var x = i / (double)rate;
                var env = Math.Exp(-x * 5.5) * Math.Min(1, x * 400);
                var v = Math.Sin(2 * Math.PI * f * x) * 0.6 + Math.Sin(2 * Math.PI * f * 2.0 * x) * 0.25 * Math.Exp(-x * 9) +
                        Math.Sin(2 * Math.PI * f * 3.01 * x) * 0.08 * Math.Exp(-x * 14);
                samples[start + i] += (float)(v * env * 0.32);
            }
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataBytes = len * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * 32767, -32768, 32767));
        w.Flush();
        return ms.ToArray();
    }

    public bool AutostartEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) is string v && v.Length > 0;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (exe is not null) k.SetValue(RunName, $"\"{exe}\"");
            }
            else
            {
                k.DeleteValue(RunName, false);
            }
        }
    }

    public void BringToFront(string? exePathOrApp)
    {
        if (string.IsNullOrWhiteSpace(exePathOrApp)) return;
        try
        {
            if (exePathOrApp.Contains('!'))
            {
                // packaged app (AUMID)
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{exePathOrApp}") { UseShellExecute = true });
                return;
            }

            var name = Path.GetFileNameWithoutExtension(exePathOrApp);
            if (string.IsNullOrEmpty(name)) name = exePathOrApp;
            if (name.Length == 16 && name.All(Uri.IsHexDigit)) name = "firefox"; // Firefox registers a hashed AUMID
            var candidates = Process.GetProcessesByName(name)
                .Where(p => p.MainWindowHandle != IntPtr.Zero)
                .ToList();
            if (candidates.Count == 0 && exePathOrApp.Contains(' '))
                candidates = Process.GetProcesses()
                    .Where(p => p.MainWindowHandle != IntPtr.Zero &&
                                p.MainWindowTitle.Contains(exePathOrApp, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            var target = candidates.FirstOrDefault();
            if (target is null) return;
            var h = target.MainWindowHandle;
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            // Alt tap lifts the foreground lock so SetForegroundWindow is honoured.
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(h);
        }
        catch (Exception ex) { Log.Error("bring to front", ex); }
    }
}
