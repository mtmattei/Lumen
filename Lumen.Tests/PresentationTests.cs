using Lumen.Domain;
using Lumen.Presentation;
using Lumen.Simulation;

namespace Lumen.Tests;

public class PresentationTests
{
    private sealed class RecordingCommands : ILumenCommands
    {
        public List<string> Calls { get; } = [];
        public void BeginContact() => Calls.Add("contact");
        public void ReachCharge() => Calls.Add("charge");
        public void ReleaseCore(bool charged) => Calls.Add($"release:{charged}");
        public void CompletePulse() => Calls.Add("pulse");
        public void SelectSubsystem(SubsystemId id) => Calls.Add($"select:{id}");
        public void ConfirmExploded(bool exploded) => Calls.Add($"exploded:{exploded}");
        public void AcknowledgeRecoveryVisual() => Calls.Add("recovered");
    }

    [Fact]
    public void Every_rive_event_maps_to_a_semantic_command()
    {
        var commands = new RecordingCommands();
        var adapter = new RiveEventAdapter(commands);

        adapter.Route(new CoreEvent(CoreEventKind.CorePressed));
        adapter.Route(new CoreEvent(CoreEventKind.CoreCharged));
        adapter.Route(new CoreEvent(CoreEventKind.CoreReleased, Charged: true));
        adapter.Route(new CoreEvent(CoreEventKind.PulseCompleted));
        adapter.Route(new CoreEvent(CoreEventKind.SubsystemSelected, SubsystemId.Thermal));
        adapter.Route(new CoreEvent(CoreEventKind.ExplodeCompleted));
        adapter.Route(new CoreEvent(CoreEventKind.CollapseCompleted));
        adapter.Route(new CoreEvent(CoreEventKind.RecoveryVisualCompleted));

        Assert.Equal(["contact", "charge", "release:True", "pulse", "select:Thermal", "exploded:True", "exploded:False", "recovered"], commands.Calls);
    }

    [Fact]
    public void Subsystem_selection_without_id_is_ignored()
    {
        var commands = new RecordingCommands();
        new RiveEventAdapter(commands).Route(new CoreEvent(CoreEventKind.SubsystemSelected));
        Assert.Empty(commands.Calls);
    }

    [Fact]
    public void Visual_state_is_normalized()
    {
        var system = new LumenSystem();
        system.Controls.LoadSetpoint = 1;
        system.Scenario.Inject(FaultKind.NetworkDegradation);
        for (var i = 0; i < 400; i++) system.Tick();

        var v = VisualStateMapper.Map(system.Current);
        foreach (var value in new[] { v.Health, v.Power, v.Temperature, v.Load, v.Latency })
        {
            Assert.InRange(value, 0, 1);
        }

        Assert.Equal(system.Current.State, v.SystemState);
        Assert.NotNull(v.StressedSubsystem);
    }

    [Fact]
    public void Nominal_has_no_stressed_subsystem()
        => Assert.Null(VisualStateMapper.Map(new LumenSystem().Current).StressedSubsystem);

    [Theory]
    [InlineData(400, 160, PointerZone.Passive)]
    [InlineData(250, 160, PointerZone.Awareness)]
    [InlineData(150, 160, PointerZone.Attraction)]
    [InlineData(75, 160, PointerZone.Deformation)]
    [InlineData(20, 160, PointerZone.Direct)]
    [InlineData(150, 80, PointerZone.Awareness)] // half-size Core scales bands down
    public void Pointer_zones_scale_to_rendered_core(double distance, double radius, PointerZone expected)
        => Assert.Equal(expected, PointerField.Zone(distance, radius));

    [Fact]
    public void Pointer_normalization_is_clamped()
    {
        var (x, y, d) = PointerField.Normalize(-5000, 90, 160);
        Assert.Equal(-1, x);
        Assert.Equal(0.3, y, 3);
        Assert.Equal(1, d);
    }

    [Fact]
    public void Press_gesture_reaches_charge_at_800ms()
    {
        var press = new PressGesture();
        Assert.Equal(CoreEventKind.CorePressed, press.Press(1000).Kind);
        Assert.Null(press.Update(1100));
        Assert.Equal(PressPhase.Contact, press.Phase);
        Assert.Null(press.Update(1400));
        Assert.Equal(PressPhase.Charging, press.Phase);
        Assert.InRange(press.Force, 0.2, 1);

        var charged = press.Update(1800);
        Assert.Equal(CoreEventKind.CoreCharged, charged?.Kind);
        Assert.Null(press.Update(1900)); // only once
        Assert.Equal(1, press.Force);

        var release = press.Release(2000);
        Assert.True(release?.Charged);
        Assert.Equal(PressPhase.Idle, press.Phase);
    }

    [Fact]
    public void Early_release_is_not_charged()
    {
        var press = new PressGesture();
        press.Press(0);
        Assert.False(press.Release(500)?.Charged);
        Assert.Null(press.Release(600));
    }

    [Fact]
    public void First_run_is_interactive_within_ten_seconds()
    {
        Assert.True(FirstRunTimeline.Duration(reducedMotion: false) <= 10);
        Assert.True(FirstRunTimeline.Duration(reducedMotion: true) <= 2);
        var cues = FirstRunTimeline.Between(-1, 10).ToList();
        Assert.Equal(Enum.GetValues<FirstRunCue>(), cues);
        Assert.Equal(0, FirstRunTimeline.CoreReveal(0.5));
        Assert.Equal(1, FirstRunTimeline.CoreReveal(6));
    }

    [Fact]
    public void Coach_walks_touch_hold_release_link()
    {
        var coach = new OnboardingCoach();
        coach.Show();
        Assert.Equal("TOUCH THE CORE", coach.Prompt);
        coach.OnContact();
        Assert.Equal("HOLD", coach.Prompt);
        coach.OnCharged();
        Assert.Equal("RELEASE", coach.Prompt);
        Assert.False(coach.IsLoadControlRevealed);
        coach.OnPulseCompleted();
        Assert.Equal("INTERFACE LINK ESTABLISHED", coach.Prompt);
        Assert.True(coach.IsLoadControlRevealed);
        coach.Dismiss();
        Assert.Equal(CoachStep.Complete, coach.Step);
    }

    [Fact]
    public void Coach_resets_on_early_release()
    {
        var coach = new OnboardingCoach();
        coach.Show();
        coach.OnContact();
        coach.OnReleasedEarly();
        Assert.Equal(CoachStep.TouchTheCore, coach.Step);
    }
}
