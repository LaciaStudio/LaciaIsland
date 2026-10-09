using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using LaciaIsland.Model;
using LaciaIsland.Services;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace LaciaIsland.Platform.Windows;

/// <summary>
/// Core Audio via NAudio. All COM work happens on one dedicated MTA thread (Core Audio objects are not
/// happy being called across apartments). Provides: master volume + notifications, default device
/// changes, WASAPI loopback spectrum for the visualizer, microphone level and mute.
/// </summary>
internal sealed class WinAudioService : IAudioService
{
    private readonly MmNotificationSink _sink;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private MMDeviceEnumerator? _enum;
    private MMDevice? _render;
    private MMDevice? _capture;
    private WasapiLoopbackCapture? _loop;
    private readonly SpectrumAnalyzer _spectrum = new();
    private volatile bool _loopRunning;
    private volatile bool _wantLoop;
    private volatile bool _wantMic;
    private float _volume;
    private volatile bool _muted;
    private float _micLevel;
    private volatile bool _micMuted;
    private bool _firstDevice = true;
    private string? _renderId;

    public event Action? VolumeChanged;
    public event Action<AudioDeviceInfo>? DefaultDeviceChanged;

    public WinAudioService()
    {
        _sink = new MmNotificationSink(OnDefaultDeviceChanged);
        _thread = new Thread(Loop) { IsBackground = true, Name = "LaciaIsland.Audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Post(Action a)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(a);
    }

    private static void Ui(Action a) => Dispatcher.UIThread.Post(a);

    private void Loop()
    {
        try
        {
            _enum = new MMDeviceEnumerator();
            _enum.RegisterEndpointNotificationCallback(_sink);
            BindRender(announce: false);
            BindCapture();
        }
        catch (Exception ex) { Log.Error("audio init", ex); }

        while (!_queue.IsCompleted)
        {
            try
            {
                if (_queue.TryTake(out var job, 40)) job();
                if (_wantMic && _capture is not null)
                {
                    try { Volatile.Write(ref _micLevel, _capture.AudioMeterInformation.MasterPeakValue); }
                    catch { Volatile.Write(ref _micLevel, 0f); }
                }
            }
            catch (InvalidOperationException) { break; }
            catch (Exception ex) { Log.Error("audio loop", ex); }
        }

        try
        {
            StopLoop();
            _enum?.UnregisterEndpointNotificationCallback(_sink);
            _render?.Dispose();
            _capture?.Dispose();
            _enum?.Dispose();
        }
        catch { /* shutting down */ }
    }

    // ───────────── render device ─────────────

    private void BindRender(bool announce)
    {
        if (_enum is null) return;
        StopLoop();
        try
        {
            if (_render is not null)
            {
                try { _render.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { /* ignore */ }
                _render.Dispose();
            }
            _render = null;
            if (!_enum.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return;
            _render = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _renderId = _render.ID;
            var vol = _render.AudioEndpointVolume;
            Volatile.Write(ref _volume, vol.MasterVolumeLevelScalar);
            _muted = vol.Mute;
            vol.OnVolumeNotification += OnVolume;
            var info = Describe(_render);
            Device = info;
            Log.Info($"render device: {_render.FriendlyName} headphones={info.IsHeadphones}");
            if (announce && !_firstDevice)
            {
                Ui(() => DefaultDeviceChanged?.Invoke(info));
                if (info.IsHeadphones) _ = Task.Run(() => LookupBattery(info));
            }
            _firstDevice = false;
        }
        catch (Exception ex) { Log.Error("bind render", ex); }
        if (_wantLoop) StartLoop();
    }

    private void LookupBattery(AudioDeviceInfo info)
    {
        var level = BluetoothBattery.TryGet(info.Model);
        if (level is null) return;
        var updated = info with { Battery = level };
        Device = updated;
        Ui(() => DefaultDeviceChanged?.Invoke(updated));
    }

    private static AudioDeviceInfo Describe(MMDevice d)
    {
        var name = d.FriendlyName ?? "";
        var model = name;
        var open = name.IndexOf('(');
        var close = name.LastIndexOf(')');
        if (open >= 0 && close > open + 1) model = name.Substring(open + 1, close - open - 1).Trim();
        var lower = name.ToLowerInvariant();
        var headphones = new[] { "наушник", "headphone", "headset", "гарнитур", "airpods", "buds", "earphone", "earbud", "hands-free", "wh-", "wf-" }
            .Any(lower.Contains);
        try
        {
            // PKEY_AudioEndpoint_FormFactor: 3 = headphones, 5 = headset
            var ff = d.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor];
            if (ff?.Value is uint v && (v == 3 || v == 5)) headphones = true;
        }
        catch { /* property missing */ }
        return new AudioDeviceInfo(name, model, headphones, null);
    }

    private void OnVolume(AudioVolumeNotificationData data)
    {
        Volatile.Write(ref _volume, data.MasterVolume);
        _muted = data.Muted;
        Ui(() => VolumeChanged?.Invoke());
    }

    public double Volume => Volatile.Read(ref _volume);
    public bool Muted => _muted;
    public AudioDeviceInfo? Device { get; private set; }

    public void SetVolume(double v)
    {
        var target = (float)Math.Clamp(v, 0, 1);
        Volatile.Write(ref _volume, target);
        Post(() =>
        {
            if (_render is null) return;
            var ev = _render.AudioEndpointVolume;
            ev.MasterVolumeLevelScalar = target;
            if (ev.Mute && target > 0) ev.Mute = false;
        });
    }

    public void ToggleMute() => Post(() =>
    {
        if (_render is null) return;
        _render.AudioEndpointVolume.Mute = !_render.AudioEndpointVolume.Mute;
    });

    // ───────────── loopback spectrum ─────────────

    public bool VisualizerEnabled
    {
        get => _wantLoop;
        set
        {
            if (value == _wantLoop) return;
            _wantLoop = value;
            Post(() => { if (_wantLoop) StartLoop(); else StopLoop(); });
        }
    }

    public bool HasSpectrum => _loopRunning;

    public bool ReadBands(float[] bands)
    {
        if (!_loopRunning) return false;
        if (!_spectrum.IsFresh)
        {
            Array.Clear(bands);
            return true;
        }
        _spectrum.Compute(bands);
        return true;
    }

    private void StartLoop()
    {
        if (_loop is not null || _render is null) return;
        try
        {
            var cap = new WasapiLoopbackCapture(_render);
            var fmt = cap.WaveFormat;
            var channels = Math.Max(1, fmt.Channels);
            var bits = fmt.BitsPerSample;
            var isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                          (fmt.Encoding == WaveFormatEncoding.Extensible && bits == 32);
            _spectrum.SampleRate = fmt.SampleRate;
            var scratch = Array.Empty<float>();
            cap.DataAvailable += (_, e) =>
            {
                try
                {
                    if (e.BytesRecorded <= 0) return;
                    var span = e.Buffer.AsSpan(0, e.BytesRecorded);
                    if (isFloat)
                    {
                        _spectrum.Push(MemoryMarshal.Cast<byte, float>(span), channels);
                    }
                    else if (bits == 16)
                    {
                        var shorts = MemoryMarshal.Cast<byte, short>(span);
                        if (scratch.Length < shorts.Length) scratch = new float[shorts.Length];
                        for (var i = 0; i < shorts.Length; i++) scratch[i] = shorts[i] / 32768f;
                        _spectrum.Push(scratch.AsSpan(0, shorts.Length), channels);
                    }
                    else if (bits == 24)
                    {
                        var n = span.Length / 3;
                        if (scratch.Length < n) scratch = new float[n];
                        for (var i = 0; i < n; i++)
                        {
                            var v = span[i * 3] | (span[i * 3 + 1] << 8) | ((sbyte)span[i * 3 + 2] << 16);
                            scratch[i] = v / 8388608f;
                        }
                        _spectrum.Push(scratch.AsSpan(0, n), channels);
                    }
                }
                catch { /* never throw on the capture thread */ }
            };
            cap.RecordingStopped += (_, e) =>
            {
                _loopRunning = false;
                if (e.Exception is not null) Log.Error("loopback stopped", e.Exception);
            };
            cap.StartRecording();
            _loop = cap;
            _loopRunning = true;
        }
        catch (Exception ex)
        {
            Log.Error("loopback start", ex);
            _loop = null;
            _loopRunning = false;
        }
    }

    private void StopLoop()
    {
        var l = _loop;
        _loop = null;
        _loopRunning = false;
        if (l is null) return;
        try { l.StopRecording(); } catch { /* ignore */ }
        try { l.Dispose(); } catch { /* ignore */ }
    }

    // ───────────── microphone ─────────────

    private void BindCapture()
    {
        if (_enum is null) return;
        try
        {
            if (_capture is not null)
            {
                try { _capture.AudioEndpointVolume.OnVolumeNotification -= OnMicVolume; } catch { /* ignore */ }
                _capture.Dispose();
            }
            _capture = null;
            if (!_enum.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return;
            _capture = _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            _micMuted = _capture.AudioEndpointVolume.Mute;
            _capture.AudioEndpointVolume.OnVolumeNotification += OnMicVolume;
        }
        catch (Exception ex) { Log.Error("bind capture", ex); }
    }

    private void OnMicVolume(AudioVolumeNotificationData data) => _micMuted = data.Muted;

    public bool MicMonitoring
    {
        get => _wantMic;
        set => _wantMic = value;
    }

    public double MicLevel => _wantMic ? Volatile.Read(ref _micLevel) : 0;
    public bool MicMuted => _micMuted;

    public void ToggleMicMute() => Post(() =>
    {
        if (_capture is null) return;
        var ev = _capture.AudioEndpointVolume;
        ev.Mute = !ev.Mute;
        _micMuted = ev.Mute;
    });

    public void OpenSoundSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("sound settings", ex); }
    }

    // ───────────── default device notifications (called on COM threads) ─────────────

    private void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            if (defaultDeviceId == _renderId) return;
            Post(() => BindRender(announce: true));
        }
        else if (flow == DataFlow.Capture && role == Role.Communications)
        {
            Post(BindCapture);
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(1500);
    }
}

/// <summary>Public COM-visible sink for Core Audio endpoint notifications.</summary>
[ComVisible(true)]
public sealed class MmNotificationSink : IMMNotificationClient
{
    private readonly Action<DataFlow, Role, string> _onDefault;
    public MmNotificationSink(Action<DataFlow, Role, string> onDefault) => _onDefault = onDefault;
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        try { _onDefault(flow, role, defaultDeviceId); } catch (Exception ex) { Log.Error("mm notify", ex); }
    }
}
