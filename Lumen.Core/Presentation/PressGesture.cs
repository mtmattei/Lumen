namespace Lumen.Presentation;

public enum PressPhase
{
    Idle,
    Contact,
    Charging,
    Charged,
}

/// <summary>
/// Press/hold/release timing owned by C#. 0–250 ms contact, 250–800 ms charge, 800 ms+ charged.
/// </summary>
public sealed class PressGesture
{
    public const double ContactMs = 250;
    public const double ChargedMs = 800;
    public const double HoldHintMs = 400;

    private double _pressedAt;

    public PressPhase Phase { get; private set; }

    public double HeldMs { get; private set; }

    /// <summary>0..1: contact ramps to 0.2, charge ramps to 1.</summary>
    public double Force => Phase switch
    {
        PressPhase.Idle => 0,
        PressPhase.Contact => 0.2 * HeldMs / ContactMs,
        PressPhase.Charging => 0.2 + 0.8 * (HeldMs - ContactMs) / (ChargedMs - ContactMs),
        _ => 1,
    };

    public CoreEvent Press(double nowMs)
    {
        _pressedAt = nowMs;
        HeldMs = 0;
        Phase = PressPhase.Contact;
        return new CoreEvent(CoreEventKind.CorePressed);
    }

    /// <summary>Advances the hold; returns CoreCharged exactly once when the threshold is crossed.</summary>
    public CoreEvent? Update(double nowMs)
    {
        if (Phase == PressPhase.Idle) return null;
        HeldMs = nowMs - _pressedAt;
        var previous = Phase;
        Phase = HeldMs >= ChargedMs ? PressPhase.Charged : HeldMs >= ContactMs ? PressPhase.Charging : PressPhase.Contact;
        return previous != PressPhase.Charged && Phase == PressPhase.Charged ? new CoreEvent(CoreEventKind.CoreCharged) : null;
    }

    public CoreEvent? Release(double nowMs)
    {
        if (Phase == PressPhase.Idle) return null;
        Update(nowMs);
        var charged = Phase == PressPhase.Charged;
        Phase = PressPhase.Idle;
        HeldMs = 0;
        return new CoreEvent(CoreEventKind.CoreReleased, Charged: charged);
    }

    public void Cancel()
    {
        Phase = PressPhase.Idle;
        HeldMs = 0;
    }
}
