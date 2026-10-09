namespace LaciaIsland.Core;

/// <summary>
/// Damped harmonic oscillator integrated analytically (exact for a constant target,
/// independent of the frame rate). Parametrised like SwiftUI springs:
/// <see cref="Response"/> is the period of the undamped oscillation in seconds,
/// <see cref="Damping"/> is the damping fraction (1 = critically damped, &lt;1 = bouncy).
/// </summary>
public sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public double Response = 0.45;
    public double Damping = 0.8;
    public double Epsilon = 0.005;

    public Spring(double value, double response = 0.45, double damping = 0.8)
    {
        Value = Target = value;
        Response = response;
        Damping = damping;
    }

    public bool IsSettled => Math.Abs(Value - Target) <= Epsilon && Math.Abs(Velocity) <= Epsilon * 8;

    public void Snap(double v) { Value = Target = v; Velocity = 0; }

    public void Set(double target, double response, double damping)
    {
        Target = target;
        Response = response;
        Damping = damping;
    }

    public void Kick(double velocity) => Velocity += velocity;

    public void Step(double dt)
    {
        if (dt <= 0) return;
        if (IsSettled) { Value = Target; Velocity = 0; return; }

        var w0 = 2 * Math.PI / Math.Max(0.02, Response);
        var z = Math.Max(0.01, Damping);
        var x0 = Value - Target;
        var v0 = Velocity;
        double x, v;

        if (Math.Abs(z - 1) < 1e-4)
        {
            var e = Math.Exp(-w0 * dt);
            var c = v0 + w0 * x0;
            x = (x0 + c * dt) * e;
            v = (v0 - w0 * dt * c) * e;
        }
        else if (z < 1)
        {
            var wd = w0 * Math.Sqrt(1 - z * z);
            var e = Math.Exp(-z * w0 * dt);
            var a = x0;
            var b = (v0 + z * w0 * x0) / wd;
            var cos = Math.Cos(wd * dt);
            var sin = Math.Sin(wd * dt);
            x = e * (a * cos + b * sin);
            v = e * ((b * wd - z * w0 * a) * cos - (a * wd + z * w0 * b) * sin);
        }
        else
        {
            var s = Math.Sqrt(z * z - 1);
            var r1 = -w0 * (z - s);
            var r2 = -w0 * (z + s);
            var c2 = (v0 - r1 * x0) / (r2 - r1);
            var c1 = x0 - c2;
            var e1 = Math.Exp(r1 * dt);
            var e2 = Math.Exp(r2 * dt);
            x = c1 * e1 + c2 * e2;
            v = c1 * r1 * e1 + c2 * r2 * e2;
        }

        Value = Target + x;
        Velocity = v;
        if (double.IsNaN(Value) || double.IsInfinity(Value)) { Value = Target; Velocity = 0; }
    }
}

/// <summary>Linear progress 0→1 over a duration, with an optional delay.</summary>
public sealed class Tween
{
    public double Duration;
    public double Delay;
    public double Elapsed;
    public Tween(double duration, double delay = 0) { Duration = duration; Delay = delay; }
    public double Progress => M.Clamp01((Elapsed - Delay) / Math.Max(1e-6, Duration));
    public bool Done => Elapsed >= Delay + Duration;
    public void Step(double dt) => Elapsed += dt;
    public void Restart() => Elapsed = 0;
    public void Finish() => Elapsed = Delay + Duration;
}
