using Lumen.Domain;

namespace Lumen.Presentation;

/// <summary>Semantic Uno commands that Core events map onto. Implemented by the shell view model.</summary>
public interface ILumenCommands
{
    void BeginContact();

    void ReachCharge();

    void ReleaseCore(bool charged);

    void CompletePulse();

    void SelectSubsystem(SubsystemId id);

    void ConfirmExploded(bool exploded);

    void AcknowledgeRecoveryVisual();
}
