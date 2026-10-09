using LaciaIsland.Model;

namespace LaciaIsland.Services;

/// <summary>Built-in countdown timer / stopwatch shown as a live activity.</summary>
public sealed class TimerService
{
    public enum Kind { None, Countdown, Stopwatch }

    public Kind Mode { get; private set; }
    public bool Running { get; private set; }
    public TimeSpan Duration { get; private set; }

    private DateTime _anchor;
    private TimeSpan _accumulated;

    public event Action? Changed;
    public event Action? Finished;

    public TimeSpan Elapsed(DateTime now) => _accumulated + (Running ? now - _anchor : TimeSpan.Zero);

    public TimeSpan Remaining(DateTime now)
    {
        var r = Duration - Elapsed(now);
        return r < TimeSpan.Zero ? TimeSpan.Zero : r;
    }

    public double Progress(DateTime now) =>
        Mode == Kind.Countdown && Duration.TotalSeconds > 0 ? Math.Clamp(Elapsed(now).TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;

    public void StartCountdown(TimeSpan duration)
    {
        Mode = Kind.Countdown;
        Duration = duration;
        _accumulated = TimeSpan.Zero;
        _anchor = Clock.UtcNow;
        Running = true;
        Changed?.Invoke();
    }

    public void StartStopwatch()
    {
        Mode = Kind.Stopwatch;
        Duration = TimeSpan.Zero;
        _accumulated = TimeSpan.Zero;
        _anchor = Clock.UtcNow;
        Running = true;
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!Running) return;
        _accumulated += Clock.UtcNow - _anchor;
        Running = false;
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (Running || Mode == Kind.None) return;
        _anchor = Clock.UtcNow;
        Running = true;
        Changed?.Invoke();
    }

    public void Toggle() { if (Running) Pause(); else Resume(); }

    public void Cancel()
    {
        Mode = Kind.None;
        Running = false;
        _accumulated = TimeSpan.Zero;
        Changed?.Invoke();
    }

    /// <summary>Call regularly; fires <see cref="Finished"/> once when a countdown ends.</summary>
    public void Tick()
    {
        if (Mode == Kind.Countdown && Running && Remaining(Clock.UtcNow) <= TimeSpan.Zero)
        {
            Mode = Kind.None;
            Running = false;
            _accumulated = TimeSpan.Zero;
            Changed?.Invoke();
            Finished?.Invoke();
        }
    }
}
