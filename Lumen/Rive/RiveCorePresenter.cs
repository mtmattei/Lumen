using Lumen.Domain;
using Lumen.Presentation;
using Lumen.Simulation;
using Microsoft.UI.Dispatching;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace Lumen.Rive;

/// <summary>Draws a <see cref="RiveScene"/> onto the window's Skia canvas, contain-fitted.</summary>
public sealed class RiveCoreCanvas : SKCanvasElement
{
    public RiveScene? Scene { get; set; }

    public object? SceneLock { get; set; }

    public float SceneOpacity { get; set; } = 1f;

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (Scene is null || SceneLock is null) return;
        lock (SceneLock)
        {
            Scene.Draw(canvas, (float)area.Width, (float)area.Height, SceneOpacity);
        }
    }
}

/// <summary>
/// Plays Assets/Rive/lumen-core.riv through the native Rive runtime (Lumen.Rive). Maps the presenter
/// contract onto the `LumenCore` state machine inputs and Rive events back onto <see cref="CoreEvent"/>s.
/// </summary>
/// <remarks>
/// Press timing stays C#-owned (CoreStage + PressGesture), so the file's own CorePressed/CoreCharged/CoreReleased
/// listener events are not forwarded; SubsystemSelected and the animation-completion events are.
/// </remarks>
public sealed class RiveCorePresenter : ICoreStagePresenter, IDisposable
{
    // Geometry of lumen-core.riv (tools/rive/build_lumen_core.py).
    private const float ArtboardSize = 500f;
    private const float MembraneRadius = 120f;
    private const float ExplodedScale = 0.7f;

    private readonly object _lock = new();
    private readonly RiveScene _scene;
    private readonly RiveCoreCanvas _canvas;
    private readonly DispatcherQueueTimer _timer;
    private long _lastTicks;
    private double _speed = 1;
    private CoreMode _mode = CoreMode.Core;

    public RiveCorePresenter(RiveScene scene, RiveCoreCanvas canvas, DispatcherQueue dispatcher)
    {
        _scene = scene;
        _canvas = canvas;
        canvas.Scene = scene;
        canvas.SceneLock = _lock;

        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => OnFrame();
        _timer.Start();
    }

    public event EventHandler<CoreEvent>? EventRaised;

    public bool IsAvailable => true;

    public string Engine => "Rive runtime";

    public CoreMode Mode => _mode;

    public LumenVisualState LastState { get; private set; }

    public CoreInteraction LastInteraction { get; private set; }

    public double FramesPerSecond { get; private set; }

    private float Scale => (float)(Math.Min(_canvas.ActualWidth, _canvas.ActualHeight) / ArtboardSize);

    public SKPoint Center => new((float)_canvas.ActualWidth / 2, (float)_canvas.ActualHeight / 2);

    public float Radius => MembraneRadius * Scale * (_mode == CoreMode.Exploded ? ExplodedScale : 1f);

    public void Apply(LumenVisualState state)
    {
        LastState = state;
        lock (_lock)
        {
            _scene.SetNumber("systemState", (int)state.SystemState);
            _scene.SetNumber("health", (float)state.Health);
            _scene.SetNumber("power", (float)state.Power);
            _scene.SetNumber("temperature", (float)state.Temperature);
            _scene.SetNumber("load", (float)state.Load);
            _scene.SetNumber("latency", (float)state.Latency);
            _scene.SetNumber("stressedSubsystem", state.StressedSubsystem is { } id ? (int)id : -1);
        }
    }

    public void SetInteraction(in CoreInteraction interaction)
    {
        LastInteraction = interaction;
        lock (_lock)
        {
            _scene.SetNumber("pointerX", (float)interaction.PointerX);
            _scene.SetNumber("pointerY", (float)interaction.PointerY);
            _scene.SetNumber("pointerDistance", (float)interaction.PointerDistance);
            _scene.SetNumber("interactionForce", (float)interaction.InteractionForce);
            _scene.SetNumber("gravityX", (float)interaction.GravityX);
            _scene.SetNumber("gravityY", (float)interaction.GravityY);
        }
    }

    public void Trigger(LumenVisualTrigger trigger)
    {
        lock (_lock)
        {
            _scene.Fire(trigger.ToString().ToLowerInvariant());
        }
    }

    public void SetMode(CoreMode mode)
    {
        if (mode == _mode) return;
        var wasExploded = _mode == CoreMode.Exploded;
        _mode = mode;
        // Energy Flow and Network visuals are not in the .riv yet; those modes show the Core itself.
        if (mode == CoreMode.Exploded && !wasExploded) Trigger(LumenVisualTrigger.Explode);
        else if (wasExploded && mode != CoreMode.Exploded) Trigger(LumenVisualTrigger.Collapse);
    }

    public void Focus(SubsystemId? subsystem)
    {
        if (subsystem is not null) Trigger(LumenVisualTrigger.Inspect);
    }

    public void SetReveal(double progress) => _canvas.SceneOpacity = (float)Math.Clamp(progress, 0, 1);

    public void SetReducedMotion(bool reduced) => _speed = reduced ? 0.5 : 1;

    public void SetNetwork(IReadOnlyList<NetworkNode> nodes)
    {
    }

    public SubsystemId? HitTestSubsystem(double x, double y) => null; // the file's listeners select subsystems

    public void RaiseSubsystemSelected(SubsystemId id) => EventRaised?.Invoke(this, new CoreEvent(CoreEventKind.SubsystemSelected, id));

    public void Pointer(CorePointer kind, double x, double y)
    {
        lock (_lock)
        {
            _scene.Pointer((RivePointer)(int)kind, (float)x, (float)y, (float)_canvas.ActualWidth, (float)_canvas.ActualHeight);
        }

        Dispatch();
    }

    public void SetTypeface(SKTypeface typeface)
    {
    }

    private void OnFrame()
    {
        var now = Environment.TickCount64;
        var dt = _lastTicks == 0 ? 1 / 60.0 : (now - _lastTicks) / 1000.0;
        _lastTicks = now;
        if (dt > 0) FramesPerSecond += (1 / dt - FramesPerSecond) * 0.05;
        lock (_lock)
        {
            _scene.Advance(Math.Min(dt, 0.1) * _speed);
        }

        Dispatch();
        _canvas.Invalidate();
    }

    private void Dispatch()
    {
        IReadOnlyList<RiveEvent> events;
        lock (_lock)
        {
            events = _scene.TakeEvents();
        }

        foreach (var e in events)
        {
            CoreEvent? mapped = e.Name switch
            {
                "PulseCompleted" => new CoreEvent(CoreEventKind.PulseCompleted),
                "ExplodeCompleted" => new CoreEvent(CoreEventKind.ExplodeCompleted),
                "CollapseCompleted" => new CoreEvent(CoreEventKind.CollapseCompleted),
                "RecoveryVisualCompleted" => new CoreEvent(CoreEventKind.RecoveryVisualCompleted),
                "SubsystemSelected" when e.Id is { } id && id is >= 0 and <= 5 => new CoreEvent(CoreEventKind.SubsystemSelected, (SubsystemId)(int)id),
                _ => null, // CorePressed/CoreCharged/CoreReleased: Uno owns press timing
            };
            if (mapped is { } ev) EventRaised?.Invoke(this, ev);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        lock (_lock)
        {
            _scene.Dispose();
        }
    }
}
