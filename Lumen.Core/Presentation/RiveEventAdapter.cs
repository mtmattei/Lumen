namespace Lumen.Presentation;

/// <summary>
/// Maps Core presentation events onto semantic Uno commands. The only path from Rive back into the app.
/// </summary>
public sealed class RiveEventAdapter(ILumenCommands commands)
{
    public void Attach(ILumenCorePresenter presenter) => presenter.EventRaised += (_, e) => Route(e);

    public void Route(CoreEvent e)
    {
        switch (e.Kind)
        {
            case CoreEventKind.CorePressed:
                commands.BeginContact();
                break;
            case CoreEventKind.CoreCharged:
                commands.ReachCharge();
                break;
            case CoreEventKind.CoreReleased:
                commands.ReleaseCore(e.Charged);
                break;
            case CoreEventKind.PulseCompleted:
                commands.CompletePulse();
                break;
            case CoreEventKind.SubsystemSelected when e.Subsystem is { } id:
                commands.SelectSubsystem(id);
                break;
            case CoreEventKind.ExplodeCompleted:
                commands.ConfirmExploded(true);
                break;
            case CoreEventKind.CollapseCompleted:
                commands.ConfirmExploded(false);
                break;
            case CoreEventKind.RecoveryVisualCompleted:
                commands.AcknowledgeRecoveryVisual();
                break;
        }
    }
}
