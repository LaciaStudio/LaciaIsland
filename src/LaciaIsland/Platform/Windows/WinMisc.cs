using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LaciaIsland.Model;
using LaciaIsland.Services;
using Microsoft.Win32;
using Windows.Devices.Enumeration;

namespace LaciaIsland.Platform.Windows;

public static class WinServices
{
    public static ServiceHub Create() => new()
    {
        Media = Safe<IMediaService>(() => new WinMediaService(), () => new DemoMedia()),
        Audio = Safe<IAudioService>(() => new WinAudioService(), () => new DemoAudio()),
        Privacy = Safe<IPrivacyService>(() => new WinPrivacyService(), () => new DemoPrivacy()),
        Power = Safe<IPowerService>(() => new WinPowerService(), () => new DemoPower()),
        Keyboard = Safe<IKeyboardService>(() => new WinKeyboardService(), () => new DemoKeyboard()),
        System = new WinSystemService(),
    };

    private static T Safe<T>(Func<T> make, Func<T> fallback)
    {
        try { return make(); }
        catch (Exception ex)
        {
            Log.Error("service " + typeof(T).Name, ex);
            return fallback();
        }
    }
}

/// <summary>
/// Microphone / camera in use, read from the same registry store that drives the Windows privacy
/// indicator (CapabilityAccessManager\ConsentStore). LastUsedTimeStop == 0 means "in use right now".
/// </summary>
internal sealed class WinPrivacyService : IPrivacyService
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private readonly Timer _timer;
    private List<CaptureUsage> _active = new();
    private string _signature = "";
    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _self = Environment.ProcessPath ?? "";

    public IReadOnlyList<CaptureUsage> Active => _active;
    public event Action? Changed;

    public WinPrivacyService()
    {
        _timer = new Timer(_ => Scan(), null, 500, 1000);
    }

    private sealed record Raw(string Key, string Name, string? Path, DateTime Start, bool Mic, bool Cam);

    private void Scan()
    {
        try
        {
            var found = new Dictionary<string, Raw>(StringComparer.OrdinalIgnoreCase);
            foreach (var cap in new[] { "microphone", "webcam" })
            {
                using var root = Registry.CurrentUser.OpenSubKey(Root + cap);
                if (root is null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    if (sub == "NonPackaged")
                    {
                        using var np = root.OpenSubKey(sub);
                        if (np is null) continue;
                        foreach (var app in np.GetSubKeyNames()) Check(np, app, true, cap == "microphone", found);
                    }
                    else
                    {
                        Check(root, sub, false, cap == "microphone", found);
                    }
                }
            }

            var sig = string.Join("|", found.Values.OrderBy(r => r.Key).Select(r => $"{r.Key}:{r.Mic}:{r.Cam}:{r.Start.Ticks}"));
            if (sig == _signature) return;
            _signature = sig;
            var raws = found.Values.ToList();
            Dispatcher.UIThread.Post(() =>
            {
                _active = raws.Select(r => new CaptureUsage
                {
                    AppName = r.Name,
                    ExePath = r.Path,
                    StartUtc = r.Start,
                    Mic = r.Mic,
                    Camera = r.Cam,
                    Icon = IconFor(r.Path),
                }).ToList();
                Changed?.Invoke();
            });
        }
        catch (Exception ex) { Log.Error("privacy scan", ex); }
    }

    private void Check(RegistryKey parent, string name, bool nonPackaged, bool mic, Dictionary<string, Raw> found)
    {
        using var k = parent.OpenSubKey(name);
        if (k is null) return;
        var start = k.GetValue("LastUsedTimeStart") is long s ? s : 0;
        var stop = k.GetValue("LastUsedTimeStop") is long e ? e : -1;
        if (start <= 0 || stop != 0) return;

        string? path = nonPackaged ? name.Replace('#', '\\') : null;
        if (path is not null && string.Equals(path, _self, StringComparison.OrdinalIgnoreCase)) return;
        var key = (path ?? name).ToLowerInvariant();
        var display = nonPackaged ? DisplayNameForExe(path!) : DisplayNameForPackage(name);
        DateTime startUtc;
        try { startUtc = DateTime.FromFileTimeUtc(start); } catch { startUtc = DateTime.UtcNow; }

        if (found.TryGetValue(key, out var prev))
            found[key] = prev with { Mic = prev.Mic || mic, Cam = prev.Cam || !mic, Start = startUtc < prev.Start ? startUtc : prev.Start };
        else
            found[key] = new Raw(key, display, path, startUtc, mic, !mic);
    }

    private static string DisplayNameForExe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                var d = vi.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(d) && d.Length <= 40) return d;
                if (!string.IsNullOrEmpty(vi.ProductName)) return vi.ProductName.Trim();
            }
        }
        catch { /* ignore */ }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string DisplayNameForPackage(string family)
    {
        var n = family.Split('_')[0];
        if (n.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)) n = n[10..];
        n = n.Replace("Windows", "Windows ");
        n = Regex.Replace(n, "(?<=[a-z])(?=[A-Z])", " ");
        return n.Replace('.', ' ').Trim();
    }

    private Bitmap? IconFor(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_icons.TryGetValue(path, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractIcon(path, 0, 64) ?? System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    using var gdi = icon.ToBitmap();
                    using var ms = new MemoryStream();
                    gdi.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    bmp = new Bitmap(ms);
                }
            }
        }
        catch (Exception ex) { Log.Error("icon", ex); }
        _icons[path] = bmp;
        return bmp;
    }

    public void Dispose() => _timer.Dispose();
}

internal sealed class WinPowerService : IPowerService
{
    private readonly Timer _timer;
    private bool? _ac;
    private int _lastLowBucket = 100;

    public bool HasBattery { get; private set; }
    public int Percent { get; private set; }
    public bool Charging { get; private set; }
    public event Action<PowerEvent>? Event;

    public WinPowerService()
    {
        _timer = new Timer(_ => Poll(), null, 300, 2000);
    }

    private void Poll()
    {
        try
        {
            if (!Native.GetSystemPowerStatus(out var st)) return;
            HasBattery = (st.BatteryFlag & 128) == 0 && st.BatteryLifePercent <= 100;
            if (!HasBattery) return;
            Percent = st.BatteryLifePercent;
            var ac = st.ACLineStatus == 1;
            Charging = ac;
            if (_ac is { } prev && prev != ac)
            {
                var e = new PowerEvent(ac ? PowerEventKind.PluggedIn : PowerEventKind.Unplugged, Percent);
                Dispatcher.UIThread.Post(() => Event?.Invoke(e));
            }
            _ac = ac;
            if (!ac)
            {
                var bucket = Percent <= 10 ? 10 : Percent <= 20 ? 20 : 100;
                if (bucket < _lastLowBucket)
                {
                    var e = new PowerEvent(PowerEventKind.Low, Percent);
                    Dispatcher.UIThread.Post(() => Event?.Invoke(e));
                }
                _lastLowBucket = bucket;
            }
            else
            {
                _lastLowBucket = 100;
            }
        }
        catch (Exception ex) { Log.Error("power", ex); }
    }

    public void Dispose() => _timer.Dispose();
}

/// <summary>Detects RU/EN switches (only when the layout changes inside the same window).</summary>
internal sealed class WinKeyboardService : IKeyboardService
{
    private readonly Timer _timer;
    private IntPtr _lastWindow;
    private int _lastLang = -1;

    public string Current { get; private set; } = "";
    public event Action<string>? LayoutChanged;

    public WinKeyboardService()
    {
        _timer = new Timer(_ => Poll(), null, 500, 150);
    }

    private void Poll()
    {
        try
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            var tid = Native.GetWindowThreadProcessId(fg, out _);
            var hkl = Native.GetKeyboardLayout(tid);
            var lang = (int)(hkl.ToInt64() & 0xFFFF);
            if (lang == 0) return;
            var changed = fg == _lastWindow && _lastLang != -1 && lang != _lastLang;
            _lastWindow = fg;
            _lastLang = lang;
            string code;
            try { code = CultureInfo.GetCultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); }
            catch { code = $"{lang:X4}"; }
            Current = code;
            if (changed) Dispatcher.UIThread.Post(() => LayoutChanged?.Invoke(code));
        }
        catch (Exception ex) { Log.Error("keyboard", ex); }
    }

    public void Dispose() => _timer.Dispose();
}

/// <summary>Best-effort Bluetooth headset battery (DEVPKEY_Bluetooth_Battery on the device node).</summary>
internal static class BluetoothBattery
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public static int? TryGet(string model)
    {
        try
        {
            var op = DeviceInformation.FindAllAsync("", new[] { BatteryKey }, DeviceInformationKind.Device);
            var task = op.AsTask();
            if (!task.Wait(5000)) return null;
            var want = Normalize(model);
            if (want.Length < 2) return null;
            foreach (var d in task.Result)
            {
                if (!d.Properties.TryGetValue(BatteryKey, out var v) || v is null) continue;
                var name = Normalize(d.Name ?? "");
                if (name.Length < 2) continue;
                if (name.Contains(want) || want.Contains(name))
                {
                    var level = Convert.ToInt32(v);
                    if (level is >= 0 and <= 100) return level;
                }
            }
        }
        catch (Exception ex) { Log.Error("bt battery", ex); }
        return null;
    }

    private static string Normalize(string s)
    {
        s = s.ToLowerInvariant();
        foreach (var junk in new[] { "stereo", "hands-free", "handsfree", "ag audio", "avrcp", "transport", "headset", "headphones", "наушники" })
            s = s.Replace(junk, "");
        return Regex.Replace(s, "[^a-z0-9а-я]+", "");
    }
}
