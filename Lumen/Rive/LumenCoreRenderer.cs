using Lumen.Domain;
using Lumen.Presentation;
using Lumen.Simulation;
using SkiaSharp;

namespace Lumen.Rive;

/// <summary>
/// Procedural stand-in for the `LumenCore` Rive artboard. It honours the same inputs, triggers and events
/// (spec-kit/rive/RIVE_CORE_SPEC.md) and only interpolates: every threshold and transition lives in C#.
/// Replace this class with a Rive state machine when `lumen-core.riv` exists.
/// </summary>
public sealed class LumenCoreRenderer : IDisposable
{
    /// <summary>Core radius as a fraction of the stage's short side. XAML readouts use the same geometry.</summary>
    public const double RadiusFactor = 0.25;

    private const int ParticleCount = 240;
    private const double TwoPi = Math.PI * 2;

    // Subsystem node angles (degrees): Environment top, Navigation bottom, Power left, Communications right.
    private static readonly double[] NodeAngles = [-150, -90, 150, 90, -30, 30];

    private static readonly SKColor Warm = new(0xF1, 0xEB, 0xDD);
    private static readonly SKColor Amber = new(0xE8, 0xB0, 0x70);
    private static readonly SKColor Hairline = new(0xF1, 0xF2, 0xEF);
    private static readonly SKColor NominalAccent = new(0x7F, 0xB8, 0x9A);
    private static readonly SKColor InfoAccent = new(0x8F, 0xB4, 0xD8);
    private static readonly SKColor ElevatedAccent = new(0xD6, 0xA5, 0x5C);
    private static readonly SKColor CriticalAccent = new(0xD8, 0x65, 0x3F);

    private readonly Particle[] _particles = new Particle[ParticleCount];
    private readonly double[] _stateWeights = new double[7];
    private readonly double[] _nodeFocus = new double[6];
    private readonly SKPoint[] _nodePositions = new SKPoint[6];
    private readonly List<Pulse> _pulses = [];

    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glow = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _text = new() { IsAntialias = true };
    private readonly SKMaskFilter _blurSmall = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
    private readonly SKMaskFilter _blurLarge = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10);
    private readonly SKPath _path = new();
    private SKFont _font = new(SKTypeface.Default, 10);

    // Targets (written by the presenter on the UI thread).
    private LumenVisualState _target = new(SystemState.Offline, 0, 0, 0, 0, 0, 0, null);
    private CoreInteraction _interaction;
    private CoreMode _mode = CoreMode.Core;
    private SubsystemId? _focus;
    private double _revealTarget = 1;
    private bool _reducedMotion;
    private IReadOnlyList<NetworkNode> _network = [];

    // Interpolated values (the renderer's own motion).
    private double _health, _power, _temperature, _load, _latency, _netFlow;
    private double _px, _py, _pd, _force, _gx, _gy, _cx, _cy;
    private double _explode, _flowMode, _networkMode, _focusOffset, _reveal;
    private double _time, _beat, _nextBeat = 1.2, _recoverAge = -1;
    private bool _explodeSettled = true, _recoverReported = true;

    public LumenCoreRenderer()
    {
        // Deterministic layout; this is presentation only, never business state.
        var random = new Random(1701);
        for (var i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new Particle
            {
                Angle = random.NextDouble() * TwoPi,
                Radius = i % 3 == 0 ? 0.25 + random.NextDouble() * 0.7 : 0.9 + random.NextDouble() * 0.7,
                Speed = (0.05 + random.NextDouble() * 0.25) * (random.NextDouble() < 0.5 ? -1 : 1),
                Size = 0.6f + (float)random.NextDouble() * 1.4f,
                Phase = random.NextDouble() * TwoPi,
                Tilt = random.NextDouble() * 0.6 - 0.3,
            };
        }

        _stateWeights[(int)SystemState.Offline] = 1;
    }

    /// <summary>Raised when an animation milestone completes (pulse left the Core, explode settled...).</summary>
    public Action<CoreEvent>? EventRaised { get; set; }

    public SKPoint Center { get; private set; }

    public float Radius { get; private set; }

    public CoreMode Mode => _mode;

    public LumenVisualState Target => _target;

    public CoreInteraction Interaction => _interaction;

    public void SetTypeface(SKTypeface typeface)
    {
        _font.Dispose();
        _font = new SKFont(typeface, 10);
    }

    public void Apply(LumenVisualState state) => _target = state;

    public void SetInteraction(in CoreInteraction interaction) => _interaction = interaction;

    public void SetMode(CoreMode mode)
    {
        if (mode == _mode) return;
        var explodedChanged = (_mode == CoreMode.Exploded) != (mode == CoreMode.Exploded);
        _mode = mode;
        if (explodedChanged) _explodeSettled = false;
    }

    public void Focus(SubsystemId? subsystem) => _focus = subsystem;

    public void SetReveal(double progress) => _revealTarget = Math.Clamp(progress, 0, 1);

    public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

    public void SetNetwork(IReadOnlyList<NetworkNode> nodes) => _network = nodes;

    public void Trigger(LumenVisualTrigger trigger)
    {
        switch (trigger)
        {
            case LumenVisualTrigger.Pulse:
                // Pulse starts at the actual contact point.
                _pulses.Add(new Pulse { OriginX = _cx, OriginY = _cy });
                _beat = 1;
                break;
            case LumenVisualTrigger.Wake:
            case LumenVisualTrigger.Acknowledge:
                _beat = 1;
                break;
            case LumenVisualTrigger.Fault:
                _beat = 0.7;
                break;
            case LumenVisualTrigger.Recover:
                _recoverAge = 0;
                _recoverReported = false;
                break;
            case LumenVisualTrigger.Explode:
                SetMode(CoreMode.Exploded);
                break;
            case LumenVisualTrigger.Collapse:
                if (_mode == CoreMode.Exploded) SetMode(CoreMode.Core);
                break;
            case LumenVisualTrigger.Inspect:
                _beat = Math.Max(_beat, 0.4);
                break;
        }
    }

    /// <summary>Returns the subsystem node under a point (exploded mode only).</summary>
    public SubsystemId? HitTestSubsystem(double x, double y)
    {
        if (_explode < 0.6) return null;
        var hitRadius = Math.Max(28, Radius * 0.2);
        for (var i = 0; i < _nodePositions.Length; i++)
        {
            var dx = x - _nodePositions[i].X;
            var dy = y - _nodePositions[i].Y;
            if (dx * dx + dy * dy <= hitRadius * hitRadius) return (SubsystemId)i;
        }

        return null;
    }

    public void Advance(double dt)
    {
        dt = Math.Clamp(dt, 0, 0.05);
        var motion = _reducedMotion ? 0.3 : 1.0;
        _time += dt * motion;

        // Rive-style interpolation toward the 10 Hz inputs.
        var slow = Lag(dt, 0.7);
        _health += (_target.Health - _health) * slow;
        _power += (_target.Power - _power) * slow;
        _temperature += (_target.Temperature - _temperature) * slow;
        _load += (_target.Load - _load) * Lag(dt, 0.35);
        _latency += (_target.Latency - _latency) * slow;
        _netFlow += (_target.NetFlow - _netFlow) * slow;

        var fast = Lag(dt, 0.12);
        _px += (_interaction.PointerX - _px) * fast;
        _py += (_interaction.PointerY - _py) * fast;
        _pd += (_interaction.PointerDistance - _pd) * fast;
        _force += (_interaction.InteractionForce - _force) * Lag(dt, 0.06);
        _gx += (_interaction.GravityX * motion - _gx) * Lag(dt, 0.4);
        _gy += (_interaction.GravityY * motion - _gy) * Lag(dt, 0.4);
        _cx += (_interaction.ContactX - _cx) * fast;
        _cy += (_interaction.ContactY - _cy) * fast;

        // Mass accelerates slowly: state blends over ~1s.
        var stateLag = Lag(dt, _reducedMotion ? 0.3 : 0.9);
        for (var i = 0; i < _stateWeights.Length; i++)
        {
            var goal = i == (int)_target.SystemState ? 1.0 : 0.0;
            _stateWeights[i] += (goal - _stateWeights[i]) * stateLag;
        }

        _reveal += (_revealTarget - _reveal) * Lag(dt, 0.25);

        var modeLag = Lag(dt, _reducedMotion ? 0.12 : 0.32);
        var explodeGoal = _mode == CoreMode.Exploded ? 1.0 : 0.0;
        _explode += (explodeGoal - _explode) * modeLag;
        _flowMode += ((_mode == CoreMode.Flow ? 1.0 : 0.0) - _flowMode) * modeLag;
        _networkMode += ((_mode == CoreMode.Network ? 1.0 : 0.0) - _networkMode) * modeLag;
        _focusOffset += ((_mode == CoreMode.Exploded && _focus is not null ? 1.0 : 0.0) - _focusOffset) * modeLag;
        for (var i = 0; i < _nodeFocus.Length; i++)
        {
            var goal = _focus is null ? 0.0 : (int)_focus == i ? 1.0 : -1.0;
            _nodeFocus[i] += (goal - _nodeFocus[i]) * modeLag;
        }

        if (!_explodeSettled && Math.Abs(_explode - explodeGoal) < 0.01)
        {
            _explodeSettled = true;
            EventRaised?.Invoke(new CoreEvent(explodeGoal > 0.5 ? CoreEventKind.ExplodeCompleted : CoreEventKind.CollapseCompleted));
        }

        AdvanceHeartbeat(dt);
        AdvancePulses(dt);
        AdvanceParticles(dt * motion);

        if (_recoverAge >= 0)
        {
            _recoverAge += dt;
            if (_recoverAge is > 0.6 and < 0.7) _beat = 1.3; // one strong heartbeat
            if (!_recoverReported && _recoverAge > 1.8)
            {
                _recoverReported = true;
                EventRaised?.Invoke(new CoreEvent(CoreEventKind.RecoveryVisualCompleted));
            }

            if (_recoverAge > 2.5) _recoverAge = -1;
        }
    }

    private void AdvanceHeartbeat(double dt)
    {
        _beat *= Math.Exp(-dt / 0.28);
        if (W(SystemState.Offline) > 0.8) return;

        _nextBeat -= dt;
        if (_nextBeat > 0) return;

        _beat = Math.Max(_beat, 0.55 + 0.3 * W(SystemState.Critical));
        // Irregular in Critical, quicker under load, calm at Nominal.
        var period = 2.6 - 0.9 * _load - 0.6 * W(SystemState.Elevated);
        var critical = W(SystemState.Critical);
        period = period * (1 - critical) + critical * (0.55 + 0.6 * Math.Abs(Math.Sin(_time * 2.3)));
        _nextBeat = Math.Max(0.4, period);
    }

    private void AdvancePulses(double dt)
    {
        for (var i = _pulses.Count - 1; i >= 0; i--)
        {
            var p = _pulses[i];
            p.Age += dt;
            if (!p.Reported && p.Age >= 0.55)
            {
                // The ring has left the membrane: Uno continues the pulse.
                p.Reported = true;
                EventRaised?.Invoke(new CoreEvent(CoreEventKind.PulseCompleted));
            }

            if (p.Age > 1.8) _pulses.RemoveAt(i);
        }
    }

    private void AdvanceParticles(double dt)
    {
        var offline = W(SystemState.Offline);
        var critical = W(SystemState.Critical);
        var recovering = W(SystemState.Recovering);
        var flowSpeed = (0.35 + 1.6 * _load + 0.8 * W(SystemState.Elevated)) * (1 - offline * 0.95);
        for (var i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            p.Angle += p.Speed * flowSpeed * dt;

            // Critical: some particles escape outward; recovery pulls them back in.
            if (i % 9 == 0)
            {
                p.Escape += (critical * 0.35 - recovering * 0.5) * dt;
                p.Escape = Math.Clamp(p.Escape, 0, 1.2);
                if (p.Escape >= 1.2) p.Escape = 0;
            }
        }
    }

    public void Render(SKCanvas canvas, float width, float height)
    {
        if (width < 8 || height < 8) return;

        var minSide = Math.Min(width, height);
        var baseRadius = minSide * (float)RadiusFactor;
        var shrink = 1 - 0.38 * _explode - 0.3 * Math.Max(_flowMode, _networkMode);
        var r = (float)(baseRadius * shrink);
        var cx = width / 2f - (float)(_focusOffset * width * 0.16);
        var cy = height / 2f;

        // Tilt displacement: 3–6% of the Core.
        var gravityShift = new SKPoint((float)(_gx * baseRadius * 0.05), (float)(_gy * baseRadius * 0.05));
        Center = new SKPoint(cx, cy);
        Radius = r;

        var reveal = _reveal;
        var dim = 1 - 0.7 * W(SystemState.Offline);

        DrawRevealScan(canvas, width, cx, cy, r, reveal);
        DrawReticle(canvas, cx, cy, r, reveal, dim);
        DrawOrbitals(canvas, cx, cy, r, reveal, dim);

        var c = new SKPoint(cx + gravityShift.X * 0.5f, cy + gravityShift.Y * 0.5f);
        DrawMembrane(canvas, c, r, reveal, dim);
        DrawInternalGeometry(canvas, c, r, reveal, dim);
        DrawNucleus(canvas, new SKPoint(cx + gravityShift.X, cy + gravityShift.Y), r, reveal, dim);
        DrawParticles(canvas, new SKPoint(cx + gravityShift.X, cy + gravityShift.Y), r, reveal, dim);
        DrawStateEffects(canvas, c, r, reveal);
        DrawSubsystems(canvas, cx, cy, r, reveal, dim);
        DrawEnergyFlow(canvas, cx, cy, r, width, height);
        DrawNetwork(canvas, cx, cy, r);
        DrawPulses(canvas, cx, cy, r);
    }

    private void DrawRevealScan(SKCanvas canvas, float width, float cx, float cy, float r, double reveal)
    {
        // First-run hairline scan (~2.0 s) and the initial point (~0.8 s).
        var point = Smooth(0, 0.08, reveal) * (1 - Smooth(0.45, 0.6, reveal));
        if (point > 0.01)
        {
            _glow.Color = Warm.WithAlpha(A(point));
            _glow.MaskFilter = _blurSmall;
            canvas.DrawCircle(cx, cy, 3 + 4 * (float)_beat, _glow);
            _glow.MaskFilter = null;
        }

        var scan = Smooth(0.2, 0.28, reveal) * (1 - Smooth(0.36, 0.44, reveal));
        if (scan > 0.01)
        {
            var sweep = (float)Smooth(0.2, 0.44, reveal);
            _stroke.StrokeWidth = 1;
            _stroke.Color = Hairline.WithAlpha(A(0.5 * scan));
            var half = width * 0.5f * sweep;
            canvas.DrawLine(cx - half, cy, cx + half, cy, _stroke);
        }
    }

    private void DrawReticle(SKCanvas canvas, float cx, float cy, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.25, 0.5, reveal) * dim * (1 - 0.6 * _flowMode) * (1 - 0.6 * _networkMode);
        if (alpha < 0.01) return;

        var ring = r * 1.5f;
        _stroke.StrokeWidth = 1;
        _stroke.Color = Hairline.WithAlpha(A(0.16 * alpha));
        canvas.DrawCircle(cx, cy, ring, _stroke);

        // Vertical axis and horizontal stubs, like an instrument crosshair.
        _stroke.Color = Hairline.WithAlpha(A(0.28 * alpha));
        canvas.DrawLine(cx, cy - ring * 1.08f, cx, cy + ring * 1.08f, _stroke);
        canvas.DrawLine(cx - ring * 1.12f, cy, cx - r * 1.08f, cy, _stroke);
        canvas.DrawLine(cx + r * 1.08f, cy, cx + ring * 1.12f, cy, _stroke);

        // Tick marks rotate very slowly.
        _fill.Color = Hairline.WithAlpha(A(0.5 * alpha));
        var rotation = _time * 0.02;
        for (var i = 0; i < 12; i++)
        {
            var a = rotation + i * TwoPi / 12;
            var major = i % 3 == 0;
            canvas.DrawCircle(cx + (float)Math.Cos(a) * ring, cy + (float)Math.Sin(a) * ring, major ? 2.2f : 1.2f, _fill);
        }

        _fill.Color = Hairline.WithAlpha(A(0.9 * alpha));
        canvas.DrawCircle(cx, cy - ring * 1.08f, 2, _fill);
        canvas.DrawCircle(cx, cy + ring * 1.08f, 2, _fill);
        canvas.DrawCircle(cx - ring * 1.12f, cy, 3, _fill);
        canvas.DrawCircle(cx + ring * 1.12f, cy, 3, _fill);
    }

    private void DrawOrbitals(SKCanvas canvas, float cx, float cy, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.8, 1.0, reveal) * dim * (1 - W(SystemState.Offline));
        if (alpha < 0.01) return;

        var critical = W(SystemState.Critical);
        var degraded = W(SystemState.Degraded);
        var wobble = (critical * 6 + degraded * 2) * Math.Sin(_time * 3.1);
        canvas.Save();
        canvas.RotateDegrees((float)(-7 + 2 * Math.Sin(_time * TwoPi / 17) + wobble + _px * 6), cx, cy);
        _stroke.StrokeWidth = 1.1f;
        _stroke.Color = Hairline.WithAlpha(A(0.42 * alpha));
        var rx = r * (1.34f + 0.02f * (float)Math.Sin(_time * TwoPi / 6.1));
        var ry = r * (0.24f + 0.03f * (float)_py);
        canvas.DrawOval(cx, cy, rx, ry, _stroke);

        if (critical > 0.05)
        {
            // Unstable orbitals: a second, slipping ring.
            _stroke.Color = Hairline.WithAlpha(A(0.25 * critical * alpha));
            canvas.DrawOval(cx + (float)(4 * Math.Sin(_time * 5)), cy, rx * 0.96f, ry * 1.2f, _stroke);
        }

        // Bright orbital beads.
        _glow.Color = Amber.WithAlpha(A(0.8 * alpha));
        for (var i = 0; i < 3; i++)
        {
            var a = _time * (0.35 + 0.8 * _load) + i * 2.1;
            canvas.DrawCircle(cx + (float)Math.Cos(a) * rx, cy + (float)Math.Sin(a) * ry, 2.2f, _glow);
        }

        canvas.Restore();
    }

    private double MembraneRadius(double theta, double r)
    {
        var t = _time;
        var breath = 0.022 * Math.Sin(TwoPi * t / 4.3) + 0.014 * Math.Sin(TwoPi * t / 6.9 + 1.3) + 0.01 * Math.Sin(TwoPi * t / 5.6 + 0.4);
        var shape = 0.016 * Math.Sin(3 * theta + TwoPi * t / 5.1) + 0.011 * Math.Sin(5 * theta - TwoPi * t / 6.7);

        var contraction = -0.035 * W(SystemState.Elevated) - 0.05 * W(SystemState.Degraded) - 0.09 * W(SystemState.Critical) - 0.25 * W(SystemState.Offline);
        var incoherence = 0.045 * W(SystemState.Critical) * Math.Sin(11 * theta + t * 9.3) + 0.02 * W(SystemState.Degraded) * Math.Sin(7 * theta - t * 4.1);

        // Pointer: the membrane reaches toward a nearby pointer (deformation band).
        var pointerAngle = Math.Atan2(_py, _px);
        var proximity = Smooth(0.7, 0.2, _pd);
        var toward = Math.Pow(Math.Max(0, Math.Cos(theta - pointerAngle)), 3);
        var reach = 0.07 * proximity * toward * (1 - _force);

        // Press compresses toward the actual contact point.
        var contactAngle = Math.Atan2(_cy, _cx);
        var contactDistance = Math.Min(1, Math.Sqrt(_cx * _cx + _cy * _cy));
        var dent = -0.14 * _force * Math.Pow(Math.Max(0, Math.Cos(theta - contactAngle)), 4) * (0.4 + contactDistance) - 0.04 * _force;

        var beat = 0.028 * _beat;
        return r * (1 + breath + shape + contraction + incoherence + reach + dent + beat);
    }

    private void DrawMembrane(SKCanvas canvas, SKPoint c, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.6, 0.82, reveal) * dim;
        if (alpha < 0.01) return;

        _path.Reset();
        const int steps = 120;
        for (var i = 0; i <= steps; i++)
        {
            var theta = i * TwoPi / steps;
            var rr = (float)MembraneRadius(theta, r);
            var x = c.X + (float)Math.Cos(theta) * rr;
            var y = c.Y + (float)Math.Sin(theta) * rr;
            if (i == 0) _path.MoveTo(x, y); else _path.LineTo(x, y);
        }

        _path.Close();

        using (var shader = SKShader.CreateRadialGradient(
            new SKPoint(c.X - r * 0.2f, c.Y - r * 0.25f), r * 1.2f,
            [Warm.WithAlpha(A(0.02 * alpha)), Warm.WithAlpha(A(0.05 * alpha)), Warm.WithAlpha(A(0.13 * alpha))],
            [0f, 0.7f, 1f], SKShaderTileMode.Clamp))
        {
            _fill.Shader = shader;
            canvas.DrawPath(_path, _fill);
            _fill.Shader = null;
        }

        _stroke.StrokeWidth = 1.3f;
        _stroke.Color = Warm.WithAlpha(A(0.55 * alpha));
        canvas.DrawPath(_path, _stroke);

        // Inner lobes: layered, independently breathing shells.
        _stroke.StrokeWidth = 1;
        for (var i = 0; i < 4; i++)
        {
            var period = 4.3 + i * 1.1;
            var squash = 0.52 + 0.06 * Math.Sin(TwoPi * _time / period + i);
            canvas.Save();
            canvas.RotateDegrees((float)(i * 45 + 8 * Math.Sin(TwoPi * _time / (period * 3)) + _px * 10 * (1 - _pd)), c.X, c.Y);
            _stroke.Color = Warm.WithAlpha(A((0.14 - i * 0.02) * alpha));
            canvas.DrawOval(c.X, c.Y, r * 0.93f, (float)(r * squash), _stroke);
            canvas.Restore();
        }
    }

    private void DrawInternalGeometry(SKCanvas canvas, SKPoint c, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.45, 0.7, reveal) * dim;
        if (alpha < 0.01) return;

        // Nearby geometry rotates toward the pointer.
        var rotation = (float)(_px * 9 * (1 - _pd) + 2 * Math.Sin(TwoPi * _time / 9.7));
        var degraded = W(SystemState.Degraded) + W(SystemState.Critical);
        var skew = (float)(degraded * 0.08 * Math.Sin(_time * 2.2));

        canvas.Save();
        canvas.RotateDegrees(rotation, c.X, c.Y);

        var tall = r * (0.95f + 0.04f * (float)_beat);
        var wide = r * (0.26f + 0.05f * (float)_load);
        var pinch = r * 0.035f;
        _path.Reset();
        _path.MoveTo(c.X, c.Y - tall);
        _path.QuadTo(c.X + pinch, c.Y - pinch, c.X + wide * (1 + skew), c.Y);
        _path.QuadTo(c.X + pinch, c.Y + pinch, c.X, c.Y + tall);
        _path.QuadTo(c.X - pinch, c.Y + pinch, c.X - wide * (1 - skew), c.Y);
        _path.QuadTo(c.X - pinch, c.Y - pinch, c.X, c.Y - tall);
        _path.Close();

        using (var shader = SKShader.CreateRadialGradient(c, tall,
            [Amber.WithAlpha(A(0.45 * alpha)), Amber.WithAlpha(A(0.12 * alpha)), SKColors.Transparent], [0f, 0.5f, 1f], SKShaderTileMode.Clamp))
        {
            _glow.Shader = shader;
            canvas.DrawPath(_path, _glow);
            _glow.Shader = null;
        }

        _stroke.StrokeWidth = 1;
        _stroke.Color = Warm.WithAlpha(A(0.5 * alpha));
        canvas.DrawPath(_path, _stroke);

        // Flow network: filaments pulsing outward, faster with load; interrupted when degraded.
        var flowAlpha = alpha * (1 - W(SystemState.Offline));
        using var dash = SKPathEffect.CreateDash([6, 10], (float)(-_time * (30 + 120 * _load)));
        _stroke.PathEffect = dash;
        for (var i = 0; i < 8; i++)
        {
            var a = i * TwoPi / 8 + 0.2;
            var flicker = degraded > 0.1 && Math.Sin(_time * 7 + i * 1.7) > 0.4 ? 0.2 : 1.0;
            _stroke.Color = Amber.WithAlpha(A(0.28 * flowAlpha * flicker));
            var end = (float)MembraneRadius(a, r) * 0.92f;
            var mid = end * 0.55f;
            _path.Reset();
            _path.MoveTo(c.X, c.Y);
            _path.QuadTo(
                c.X + (float)Math.Cos(a + 0.5) * mid, c.Y + (float)Math.Sin(a + 0.5) * mid,
                c.X + (float)Math.Cos(a) * end, c.Y + (float)Math.Sin(a) * end);
            canvas.DrawPath(_path, _stroke);
        }

        _stroke.PathEffect = null;
        canvas.Restore();
    }

    private void DrawNucleus(SKCanvas canvas, SKPoint c, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.4, 0.6, reveal) * dim;
        if (alpha < 0.01) return;

        var scale = 1 + 0.14 * _beat + 0.2 * _force + 0.12 * _load - 0.1 * W(SystemState.Elevated) - 0.3 * W(SystemState.Critical);
        var size = (float)(r * 0.3 * scale);

        // Degraded: broken symmetry, the nucleus leans toward the stressed subsystem.
        if (_target.StressedSubsystem is { } stressed)
        {
            var a = NodeAngles[(int)stressed] * Math.PI / 180;
            var lean = r * 0.05f * (float)(W(SystemState.Degraded) + W(SystemState.Critical));
            c = new SKPoint(c.X + (float)Math.Cos(a) * lean, c.Y + (float)Math.Sin(a) * lean);
        }

        var intensity = alpha * (0.55 + 0.35 * _health + 0.25 * _load + 0.3 * _force);
        using (var shader = SKShader.CreateRadialGradient(c, size,
            [SKColors.White.WithAlpha(A(intensity)), Amber.WithAlpha(A(0.5 * intensity)), SKColors.Transparent], [0f, 0.25f, 1f], SKShaderTileMode.Clamp))
        {
            _glow.Shader = shader;
            canvas.DrawCircle(c, size, _glow);
            _glow.Shader = null;
        }

        // Axis sparks at the top and bottom of the internal geometry.
        _glow.MaskFilter = _blurSmall;
        _glow.Color = SKColors.White.WithAlpha(A(0.8 * alpha));
        canvas.DrawCircle(c.X, c.Y - r * 0.95f, 3 + 2 * (float)_beat, _glow);
        canvas.DrawCircle(c.X, c.Y + r * 0.95f, 3 + 2 * (float)_beat, _glow);
        _glow.MaskFilter = null;
    }

    private void DrawParticles(SKCanvas canvas, SKPoint c, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.28, 0.5, reveal) * dim;
        if (alpha < 0.01) return;

        // Booting: particles converge inward from far away.
        var converge = 1 + 2.4 * (1 - Smooth(0.3, 0.62, reveal)) + 0.8 * W(SystemState.Booting) * (1 - Smooth(0.6, 1, reveal));
        var pointerBias = Smooth(1.0, 0.3, _pd) * 0.18;
        var px = c.X + (float)(_px * r * 300 / 160);
        var py = c.Y + (float)(_py * r * 300 / 160);

        for (var i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            var breathe = 1 + 0.04 * Math.Sin(_time * 0.9 + p.Phase);
            var radius = r * p.Radius * breathe * converge * (1 + p.Escape);
            var x = c.X + (float)(Math.Cos(p.Angle) * radius);
            var y = c.Y + (float)(Math.Sin(p.Angle) * radius * (0.75 + p.Tilt));
            x += (float)((px - x) * pointerBias * (p.Radius < 1 ? 0.4 : 1));
            y += (float)((py - y) * pointerBias * (p.Radius < 1 ? 0.4 : 1));

            var twinkle = 0.45 + 0.55 * Math.Sin(_time * 1.3 + p.Phase * 3);
            var fade = p.Escape > 0 ? Math.Max(0, 1 - p.Escape) : 1;
            var color = p.Escape > 0.05 ? CriticalAccent : (i % 5 == 0 ? Amber : Warm);
            _glow.Color = color.WithAlpha(A(alpha * twinkle * fade * (p.Radius < 1 ? 0.6 : 0.45)));
            canvas.DrawCircle(x, y, p.Size, _glow);
        }
    }

    private void DrawStateEffects(SKCanvas canvas, SKPoint c, float r, double reveal)
    {
        if (reveal < 0.8) return;

        // Localized accent at the stressed subsystem; never tint the whole Core.
        if (_target.StressedSubsystem is { } stressed)
        {
            var intensity = 0.55 * W(SystemState.Elevated) + 0.8 * W(SystemState.Degraded) + 1.0 * W(SystemState.Critical) + 0.3 * W(SystemState.Recovering);
            if (intensity > 0.02)
            {
                var accent = W(SystemState.Critical) + W(SystemState.Degraded) > W(SystemState.Elevated) ? CriticalAccent : ElevatedAccent;
                var flicker = 0.75 + 0.25 * Math.Sin(_time * (4 + 6 * W(SystemState.Critical)));
                var angle = (float)NodeAngles[(int)stressed];
                var rect = new SKRect(c.X - r * 1.02f, c.Y - r * 1.02f, c.X + r * 1.02f, c.Y + r * 1.02f);
                _stroke.StrokeWidth = 6;
                _stroke.MaskFilter = _blurLarge;
                _stroke.Color = accent.WithAlpha(A(0.7 * intensity * flicker));
                canvas.DrawArc(rect, angle - 28, 56, false, _stroke);
                _stroke.MaskFilter = null;
                _stroke.StrokeWidth = 1.5f;
                _stroke.Color = accent.WithAlpha(A(0.9 * intensity));
                canvas.DrawArc(rect, angle - 18, 36, false, _stroke);
            }
        }

        // Recovering: a mineral-green ring realigns inward.
        if (_recoverAge >= 0)
        {
            var u = Math.Clamp(_recoverAge / 1.8, 0, 1);
            _stroke.StrokeWidth = 1.2f;
            _stroke.Color = NominalAccent.WithAlpha(A(0.5 * (1 - u)));
            canvas.DrawCircle(c, (float)(r * (1.7 - 0.7 * EaseOut(u))), _stroke);
        }
    }

    private void DrawSubsystems(SKCanvas canvas, float cx, float cy, float r, double reveal, double dim)
    {
        var alpha = Smooth(0.85, 1.0, reveal) * dim * (1 - _flowMode) * (1 - _networkMode);
        for (var i = 0; i < 6; i++)
        {
            var a = NodeAngles[i] * Math.PI / 180;
            var focus = _nodeFocus[i];
            var recede = Math.Max(0, -focus);
            var selected = Math.Max(0, focus);

            var distance = r * (1.5 + 0.9 * _explode - 0.35 * recede * _explode);
            var x = cx + (float)(Math.Cos(a) * distance);
            var y = cy + (float)(Math.Sin(a) * distance);

            // The selected node moves toward focus: beside the Core, on the inspector side.
            var focusX = cx + r * 2.2f;
            x += (float)((focusX - x) * selected);
            y += (float)((cy - y) * selected);
            _nodePositions[i] = new SKPoint(x, y);
            var connectorAngle = Math.Atan2(y - cy, x - cx);

            if (alpha < 0.01) continue;
            var nodeAlpha = alpha * (1 - 0.7 * recede);

            if (_explode < 0.05)
            {
                continue; // collapsed: the reticle carries the node markers
            }

            var e = _explode * nodeAlpha;
            var condition = _target.StressedSubsystem == (SubsystemId)i ? StressColor() : Hairline;

            // Connector with energy travelling outward.
            var inner = (float)MembraneRadius(connectorAngle, r);
            _stroke.StrokeWidth = 1;
            _stroke.Color = Hairline.WithAlpha(A(0.22 * e));
            var sx = cx + (float)Math.Cos(connectorAngle) * inner;
            var sy = cy + (float)Math.Sin(connectorAngle) * inner;
            canvas.DrawLine(sx, sy, x, y, _stroke);
            var u = (float)((_time * (0.4 + _load) + i * 0.17) % 1.0);
            _glow.Color = Amber.WithAlpha(A(0.8 * e));
            canvas.DrawCircle(sx + (x - sx) * u, sy + (y - sy) * u, 1.8f, _glow);

            var nodeRadius = (float)(r * 0.16 * (1 + 0.35 * selected));
            _fill.Color = new SKColor(0x0B, 0x0C, 0x0C).WithAlpha(A(0.92 * e));
            canvas.DrawCircle(x, y, nodeRadius, _fill);
            _stroke.StrokeWidth = selected > 0.5 ? 1.5f : 1;
            _stroke.Color = condition.WithAlpha(A((0.5 + 0.5 * selected) * e));
            canvas.DrawCircle(x, y, nodeRadius, _stroke);

            // Each subsystem gets a small inner breathing geometry, unstable when stressed.
            var unstable = _target.StressedSubsystem == (SubsystemId)i ? (W(SystemState.Degraded) + W(SystemState.Critical)) : 0;
            var inner2 = nodeRadius * (0.45f + 0.08f * (float)Math.Sin(_time * (1.1 + i * 0.23) + i) + (float)(0.1 * unstable * Math.Sin(_time * 13)));
            _stroke.Color = Warm.WithAlpha(A(0.6 * e));
            canvas.DrawCircle(x, y, inner2, _stroke);
            _glow.Color = Warm.WithAlpha(A(0.7 * e));
            canvas.DrawCircle(x, y, 2 + 1.5f * (float)_beat, _glow);

            _text.Color = Hairline.WithAlpha(A((0.55 + 0.45 * selected) * e));
            canvas.DrawText(((SubsystemId)i).ToString().ToUpperInvariant(), x, y + nodeRadius + 16, SKTextAlign.Center, _font, _text);
        }
    }

    private void DrawEnergyFlow(SKCanvas canvas, float cx, float cy, float r, float width, float height)
    {
        var m = _flowMode;
        if (m < 0.01) return;

        var span = Math.Min(width, height) * 0.36f;
        var solar = new SKPoint(cx - span * 1.2f, cy - span * 0.8f);
        var system = new SKPoint(cx - span * 1.1f, cy + span * 0.85f);
        var battery = new SKPoint(cx + span * 1.2f, cy + span * 0.55f);

        DrawFlowStream(canvas, solar, new SKPoint(cx, cy), _power, 1, Amber, m);
        DrawFlowStream(canvas, new SKPoint(cx, cy), system, _load, 1, Warm, m);
        var charging = _netFlow >= 0;
        DrawFlowStream(canvas, charging ? new SKPoint(cx, cy) : battery, charging ? battery : new SKPoint(cx, cy), Math.Abs(_netFlow), 1, charging ? NominalAccent : ElevatedAccent, m);

        DrawFlowNode(canvas, solar, "SOLAR", m);
        DrawFlowNode(canvas, system, "SYSTEM", m);
        DrawFlowNode(canvas, battery, "BATTERY", m);
    }

    private void DrawFlowStream(SKCanvas canvas, SKPoint from, SKPoint to, double volume, double rate, SKColor color, double m)
    {
        // Speed = rate, density = volume, direction = transfer direction.
        var control = new SKPoint((from.X + to.X) / 2 + (to.Y - from.Y) * 0.18f, (from.Y + to.Y) / 2 - (to.X - from.X) * 0.18f);
        _path.Reset();
        _path.MoveTo(from);
        _path.QuadTo(control, to);
        _stroke.StrokeWidth = 1;
        _stroke.Color = Hairline.WithAlpha(A(0.14 * m));
        canvas.DrawPath(_path, _stroke);

        var count = 4 + (int)(volume * 34);
        var speed = 0.12 + 0.55 * volume * rate;
        _glow.Color = color.WithAlpha(A(0.85 * m));
        for (var i = 0; i < count; i++)
        {
            var u = (float)((_time * speed + i / (double)count) % 1.0);
            var inv = 1 - u;
            var x = inv * inv * from.X + 2 * inv * u * control.X + u * u * to.X;
            var y = inv * inv * from.Y + 2 * inv * u * control.Y + u * u * to.Y;
            canvas.DrawCircle(x, y, 1.4f + 0.8f * (float)volume, _glow);
        }
    }

    private void DrawFlowNode(SKCanvas canvas, SKPoint p, string label, double m)
    {
        _fill.Color = new SKColor(0x0B, 0x0C, 0x0C).WithAlpha(A(m));
        canvas.DrawCircle(p, 16, _fill);
        _stroke.StrokeWidth = 1;
        _stroke.Color = Hairline.WithAlpha(A(0.6 * m));
        canvas.DrawCircle(p, 16, _stroke);
        _glow.Color = Warm.WithAlpha(A(0.8 * m));
        canvas.DrawCircle(p, 2.5f, _glow);
        _text.Color = Hairline.WithAlpha(A(0.7 * m));
        canvas.DrawText(label, p.X, p.Y + 34, SKTextAlign.Center, _font, _text);
    }

    private void DrawNetwork(SKCanvas canvas, float cx, float cy, float r)
    {
        var m = _networkMode;
        if (m < 0.01 || _network.Count == 0) return;

        var ring = r * 2.5f * (float)(0.6 + 0.4 * m);
        _stroke.StrokeWidth = 1;
        _stroke.Color = Hairline.WithAlpha(A(0.1 * m));
        canvas.DrawCircle(cx, cy, ring, _stroke);

        _text.Color = Hairline.WithAlpha(A(0.8 * m));
        canvas.DrawText("SYSTEM 01", cx, cy + r * 1.35f + 14, SKTextAlign.Center, _font, _text);

        for (var i = 0; i < _network.Count; i++)
        {
            var node = _network[i];
            var a = node.Angle * Math.PI / 180 + _time * 0.01;
            var x = cx + (float)Math.Cos(a) * ring;
            var y = cy + (float)Math.Sin(a) * ring * 0.8f;
            var color = node.Connection switch
            {
                NodeConnection.Reconnecting => CriticalAccent,
                NodeConnection.Degraded => ElevatedAccent,
                _ => InfoAccent,
            };

            _stroke.Color = color.WithAlpha(A(0.25 * m));
            _stroke.PathEffect = null;
            if (node.Connection == NodeConnection.Reconnecting)
            {
                using var dash = SKPathEffect.CreateDash([3, 6], 0);
                _stroke.PathEffect = dash;
                canvas.DrawLine(cx, cy, x, y, _stroke);
                _stroke.PathEffect = null;
            }
            else
            {
                canvas.DrawLine(cx, cy, x, y, _stroke);
                // Packets: faster links move faster.
                var speed = 60 / Math.Max(8, node.Latency);
                for (var k = 0; k < 3; k++)
                {
                    var u = (float)((_time * speed * 0.4 + k / 3.0 + i * 0.13) % 1.0);
                    _glow.Color = color.WithAlpha(A(0.9 * m));
                    canvas.DrawCircle(cx + (x - cx) * u, cy + (y - cy) * u, 1.6f, _glow);
                }
            }

            _fill.Color = new SKColor(0x0B, 0x0C, 0x0C).WithAlpha(A(m));
            canvas.DrawCircle(x, y, 10, _fill);
            _stroke.Color = color.WithAlpha(A(0.8 * m));
            canvas.DrawCircle(x, y, 10, _stroke);
            _text.Color = Hairline.WithAlpha(A(0.75 * m));
            canvas.DrawText(node.Platform.ToUpperInvariant(), x, y + 26, SKTextAlign.Center, _font, _text);
            _text.Color = Hairline.WithAlpha(A(0.45 * m));
            canvas.DrawText($"{node.Latency:0} ms", x, y + 40, SKTextAlign.Center, _font, _text);
        }
    }

    private void DrawPulses(SKCanvas canvas, float cx, float cy, float r)
    {
        foreach (var p in _pulses)
        {
            var u = p.Age / 1.8;
            var ox = cx + (float)(p.OriginX * r * (1 - Math.Min(1, p.Age * 2)));
            var oy = cy + (float)(p.OriginY * r * (1 - Math.Min(1, p.Age * 2)));
            var radius = (float)(r * (0.15 + 3.4 * EaseOut(u)));
            _stroke.StrokeWidth = 2;
            _stroke.Color = Warm.WithAlpha(A(0.7 * (1 - u)));
            canvas.DrawCircle(ox, oy, radius, _stroke);
            _stroke.StrokeWidth = 1;
            _stroke.Color = Amber.WithAlpha(A(0.4 * (1 - u)));
            canvas.DrawCircle(ox, oy, radius * 0.86f, _stroke);
        }
    }

    private SKColor StressColor() => W(SystemState.Critical) + W(SystemState.Degraded) > 0.5 ? CriticalAccent : ElevatedAccent;

    private double W(SystemState state) => _stateWeights[(int)state];

    private static byte A(double alpha) => (byte)Math.Clamp(alpha * 255, 0, 255);

    private static double Lag(double dt, double tau) => 1 - Math.Exp(-dt / tau);

    private static double Smooth(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double EaseOut(double u) => 1 - Math.Pow(1 - u, 3);

    public void Dispose()
    {
        _stroke.Dispose();
        _fill.Dispose();
        _glow.Dispose();
        _text.Dispose();
        _blurSmall.Dispose();
        _blurLarge.Dispose();
        _path.Dispose();
        _font.Dispose();
    }

    private struct Particle
    {
        public double Angle;
        public double Radius;
        public double Speed;
        public float Size;
        public double Phase;
        public double Tilt;
        public double Escape;
    }

    private sealed class Pulse
    {
        public double Age;
        public double OriginX;
        public double OriginY;
        public bool Reported;
    }
}
