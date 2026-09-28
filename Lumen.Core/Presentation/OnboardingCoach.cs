namespace Lumen.Presentation;

public enum CoachStep
{
    Hidden,
    TouchTheCore,
    Hold,
    Release,
    LinkEstablished,
    Complete,
}

/// <summary>
/// Teaches the press/hold/release gesture in place. No modal, no carousel.
/// </summary>
public sealed class OnboardingCoach
{
    public CoachStep Step { get; private set; } = CoachStep.Hidden;

    /// <summary>SYSTEM LOAD is exposed after the first successful pulse.</summary>
    public bool IsLoadControlRevealed { get; private set; }

    public string Prompt => Step switch
    {
        CoachStep.TouchTheCore => "TOUCH THE CORE",
        CoachStep.Hold => "HOLD",
        CoachStep.Release => "RELEASE",
        CoachStep.LinkEstablished => "INTERFACE LINK ESTABLISHED",
        _ => string.Empty,
    };

    public void Show()
    {
        if (Step == CoachStep.Hidden) Step = CoachStep.TouchTheCore;
    }

    public bool IsInContact { get; private set; }

    /// <summary>Contact alone keeps TOUCH THE CORE; HOLD appears once the press has lasted ~400 ms.</summary>
    public void OnContact() => IsInContact = true;

    /// <summary>Called while held; the HOLD prompt only matters after ~400 ms.</summary>
    public void OnHeld(double heldMs)
    {
        if (IsInContact && Step == CoachStep.TouchTheCore && heldMs >= PressGesture.HoldHintMs) Step = CoachStep.Hold;
    }

    public void OnCharged()
    {
        IsInContact = true;
        if (Step is CoachStep.TouchTheCore or CoachStep.Hold) Step = CoachStep.Release;
    }

    /// <summary>Released before charge: return to the touch prompt.</summary>
    public void OnReleasedEarly()
    {
        IsInContact = false;
        if (Step is CoachStep.Hold or CoachStep.Release) Step = CoachStep.TouchTheCore;
    }

    public void OnPulseCompleted()
    {
        IsInContact = false;
        if (Step is CoachStep.Complete or CoachStep.Hidden) return;
        Step = CoachStep.LinkEstablished;
        IsLoadControlRevealed = true;
    }

    public void Dismiss()
    {
        if (Step == CoachStep.LinkEstablished) Step = CoachStep.Complete;
    }

    /// <summary>Skip path for returning users and keyboard/assistive users.</summary>
    public void Skip()
    {
        Step = CoachStep.Complete;
        IsLoadControlRevealed = true;
    }
}
