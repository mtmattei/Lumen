using Lumen.Domain;
using Lumen.Presentation;
using SkiaSharp;

namespace Lumen.Rive;

public enum CorePointer
{
    Down,
    Move,
    Up,
}

/// <summary>
/// What <see cref="CoreStage"/> and the shell need from a Core presenter beyond the contract:
/// geometry for Uno-owned input, pointer forwarding, and diagnostics for the developer overlay.
/// </summary>
public interface ICoreStagePresenter : ILumenCorePresenter
{
    /// <summary>"Rive runtime" or "Procedural (fallback)".</summary>
    string Engine { get; }

    SKPoint Center { get; }

    float Radius { get; }

    CoreMode Mode { get; }

    LumenVisualState LastState { get; }

    CoreInteraction LastInteraction { get; }

    double FramesPerSecond { get; }

    SubsystemId? HitTestSubsystem(double x, double y);

    void RaiseSubsystemSelected(SubsystemId id);

    /// <summary>Raw pointer in stage coordinates, for presenters with their own listeners.</summary>
    void Pointer(CorePointer kind, double x, double y);

    void SetTypeface(SKTypeface typeface);
}
