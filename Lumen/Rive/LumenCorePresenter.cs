using Lumen.Domain;
using Lumen.Presentation;
using Lumen.Simulation;
using Microsoft.UI.Dispatching;
using SkiaSharp;

namespace Lumen.Rive;

/// <summary>
/// The single adapter between Uno and the Core animation. Uno pushes normalized state in; animation
/// milestones come back out as <see cref="CoreEvent"/>s for <see cref="RiveEventAdapter"/>.
/// </summary>
public sealed class LumenCorePresenter : ICoreStagePresenter, IDisposable
{
    private readonly LumenCoreRenderer _renderer = new();
    private readonly LumenCoreCanvas? _canvas;
    private readonly DispatcherQueueTimer? _timer;
    private long _lastTicks;

    public LumenCorePresenter(LumenCoreCanvas? canvas, DispatcherQueue dispatcher)
    {
        _canvas = canvas;
        IsAvailable = canvas is not null;
        _renderer.EventRaised = e => EventRaised?.Invoke(this, e);
        if (canvas is null) return;

        canvas.Renderer = _renderer;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => OnFrame();
        _timer.Start();
    }

    public event EventHandler<CoreEvent>? EventRaised;

    public bool IsAvailable { get; }

    public LumenCoreRenderer Renderer => _renderer;

    public string Engine => "Procedural (fallback)";

    public SKPoint Center => _renderer.Center;

    public float Radius => _renderer.Radius;

    public CoreMode Mode => _renderer.Mode;

    public void Pointer(CorePointer kind, double x, double y)
    {
        // The procedural renderer has no listeners; CoreStage hit-tests through HitTestSubsystem.
    }

    /// <summary>Last applied values, for the developer overlay.</summary>
    public LumenVisualState LastState { get; private set; }

    public CoreInteraction LastInteraction { get; private set; }

    public double FramesPerSecond { get; private set; }

    public void Apply(LumenVisualState state)
    {
        LastState = state;
        _renderer.Apply(state);
    }

    public void Trigger(LumenVisualTrigger trigger) => _renderer.Trigger(trigger);

    public void SetInteraction(in CoreInteraction interaction)
    {
        LastInteraction = interaction;
        _renderer.SetInteraction(interaction);
    }

    public void SetMode(CoreMode mode) => _renderer.SetMode(mode);

    public void Focus(SubsystemId? subsystem) => _renderer.Focus(subsystem);

    public void SetReveal(double progress) => _renderer.SetReveal(progress);

    public void SetNetwork(IReadOnlyList<NetworkNode> nodes) => _renderer.SetNetwork(nodes);

    public void SetReducedMotion(bool reduced)
    {
        _renderer.SetReducedMotion(reduced);
        if (_timer is not null)
        {
            // Reduced motion still renders every state, just at a calmer cadence.
            _timer.Interval = TimeSpan.FromMilliseconds(reduced ? 33 : 16);
        }
    }

    public SubsystemId? HitTestSubsystem(double x, double y) => _renderer.HitTestSubsystem(x, y);

    /// <summary>Raised by the Uno input layer for selection in exploded mode.</summary>
    public void RaiseSubsystemSelected(SubsystemId id)
        => EventRaised?.Invoke(this, new CoreEvent(CoreEventKind.SubsystemSelected, id));

    public void SetTypeface(SKTypeface typeface) => _renderer.SetTypeface(typeface);

    private void OnFrame()
    {
        var now = Environment.TickCount64;
        var dt = _lastTicks == 0 ? 0.016 : (now - _lastTicks) / 1000.0;
        _lastTicks = now;
        if (dt > 0) FramesPerSecond += ((1 / dt) - FramesPerSecond) * 0.05;
        _renderer.Advance(dt);
        _canvas?.RequestRedraw();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _renderer.Dispose();
    }
}
