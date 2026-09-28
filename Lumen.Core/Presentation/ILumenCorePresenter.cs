namespace Lumen.Presentation;

public enum CoreMode
{
    Core,
    Flow,
    Exploded,
    Network,
}

/// <summary>
/// The single boundary between Uno and the Core animation (Rive or the procedural stand-in).
/// </summary>
public interface ILumenCorePresenter
{
    /// <summary>Raised when the Core presentation reports an interaction or animation milestone.</summary>
    event EventHandler<CoreEvent>? EventRaised;

    /// <summary>False when the animation runtime failed; Uno status and controls must remain usable.</summary>
    bool IsAvailable { get; }

    void Apply(LumenVisualState state);

    void Trigger(LumenVisualTrigger trigger);

    void SetInteraction(in CoreInteraction interaction);

    void SetMode(CoreMode mode);

    /// <summary>Subsystem the Explorer inspector is focused on, or null.</summary>
    void Focus(Lumen.Domain.SubsystemId? subsystem);

    /// <summary>0..1 first-run reveal progress; 1 is fully assembled.</summary>
    void SetReveal(double progress);

    void SetReducedMotion(bool reduced);

    /// <summary>Instances shown around System 01 in Network mode.</summary>
    void SetNetwork(IReadOnlyList<Lumen.Simulation.NetworkNode> nodes);
}
