using Lumen.Domain;

namespace Lumen.Presentation;

/// <summary>
/// Normalized, presentation-only view of authoritative state. Mirrors the `LumenCore` state machine inputs
/// (spec-kit/rive/RIVE_CORE_SPEC.md). Numeric values are 0..1 unless noted.
/// </summary>
/// <param name="NetFlow">Contract extension, -1..1: battery transfer direction and volume (positive = charging).</param>
/// <param name="StressedSubsystem">Contract extension: the subsystem expressing instability, or null.</param>
public readonly record struct LumenVisualState(
    SystemState SystemState,
    double Health,
    double Power,
    double Temperature,
    double Load,
    double Latency,
    double NetFlow,
    SubsystemId? StressedSubsystem);

/// <summary>Pointer/press/tilt inputs. Written at pointer rate without touching XAML.</summary>
public readonly record struct CoreInteraction(
    double PointerX,
    double PointerY,
    double PointerDistance,
    double InteractionForce,
    double GravityX,
    double GravityY,
    double ContactX,
    double ContactY);

public enum LumenVisualTrigger
{
    Wake,
    Pulse,
    Fault,
    Recover,
    Explode,
    Collapse,
    Inspect,
    Acknowledge,
}

public enum CoreEventKind
{
    CorePressed,
    CoreCharged,
    CoreReleased,
    PulseCompleted,
    SubsystemSelected,
    ExplodeCompleted,
    CollapseCompleted,
    RecoveryVisualCompleted,
}

/// <summary>An event raised by the Core presentation. Never mutates business state directly.</summary>
public readonly record struct CoreEvent(CoreEventKind Kind, SubsystemId? Subsystem = null, bool Charged = false);
