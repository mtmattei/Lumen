using Lumen.Domain;
using Lumen.Simulation;

namespace Lumen.Tests;

public class SimulatorTests
{
    private static LumenSystem Run(Action<LumenSystem>? setup, int steps)
    {
        var system = new LumenSystem();
        setup?.Invoke(system);
        for (var i = 0; i < steps; i++) system.Tick();
        return system;
    }

    [Fact]
    public void Reset_matches_nominal_fixture()
    {
        var t = new LumenSystem().Current;
        Assert.Equal(SystemState.Nominal, t.State);
        Assert.Equal(12.4, t.PowerGeneration, 1);
        Assert.Equal(0.83, t.BatteryLevel, 2);
        Assert.Equal(7.8, t.SystemLoad, 1);
        Assert.Equal(36.0, t.Temperature, 1);
        Assert.Equal(58.0, t.CoolantPressure, 1);
        Assert.Equal(18, t.NetworkLatency, 0);
        Assert.Equal(612, t.Co2, 0);
    }

    [Fact]
    public void Nominal_steady_state_stays_near_fixture()
    {
        var t = Run(null, 300).Current;
        Assert.Equal(SystemState.Nominal, t.State);
        Assert.InRange(t.Temperature, 35, 38);
        Assert.InRange(t.Health, 0.93, 0.99);
        Assert.InRange(t.SystemLoad, 7.6, 8.0);
    }

    [Fact]
    public void Same_inputs_produce_identical_traces()
    {
        static string Trace()
        {
            var system = new LumenSystem();
            system.Controls.LoadSetpoint = 0.7;
            system.Scenario.Inject(FaultKind.NetworkDegradation);
            var lines = new List<string>();
            for (var i = 0; i < 200; i++)
            {
                var t = system.Tick();
                lines.Add($"{t.State}|{t.Temperature:R}|{t.BatteryLevel:R}|{t.NetworkLatency:R}|{t.Heading:R}");
            }

            return string.Join('\n', lines);
        }

        Assert.Equal(Trace(), Trace());
    }

    [Fact]
    public void Load_raises_consumption_drain_and_temperature()
    {
        var low = Run(s => s.Controls.LoadSetpoint = 0.2, 200).Current;
        var high = Run(s => s.Controls.LoadSetpoint = 0.8, 200).Current;

        Assert.True(high.SystemLoad > low.SystemLoad + 5);
        Assert.True(high.BatteryLevel < low.BatteryLevel);
        Assert.True(high.Temperature > low.Temperature + 5);
    }

    [Fact]
    public void Pump_boost_adds_cooling_capacity()
    {
        var baseline = Run(s => s.Controls.LoadSetpoint = 0.8, 200).Current;
        var boosted = Run(s => { s.Controls.LoadSetpoint = 0.8; s.Controls.PumpBoost = 1; }, 200).Current;
        Assert.True(boosted.CoolantPressure > baseline.CoolantPressure);
        Assert.True(boosted.Temperature < baseline.Temperature);
    }

    [Theory]
    [InlineData(0, SystemState.Nominal)]
    [InlineData(8, SystemState.Nominal)]
    [InlineData(15, SystemState.Elevated)]
    [InlineData(24, SystemState.Degraded)]
    [InlineData(32, SystemState.Recovering)]
    [InlineData(40, SystemState.Recovering)]
    [InlineData(45, SystemState.Nominal)]
    public void Cooling_scenario_follows_fixture_states(double at, SystemState expected)
    {
        var system = new LumenSystem();
        system.Scenario.StartCoolingScenario();
        var steps = (int)Math.Round(at / TelemetrySimulator.StepSeconds);
        for (var i = 0; i < steps; i++) system.Tick();
        Assert.Equal(expected, system.Current.State);
    }

    [Fact]
    public void Cooling_scenario_tracks_fixture_values()
    {
        var system = new LumenSystem();
        var timeline = system.Scenario.Timeline;
        system.Scenario.StartCoolingScenario();
        var elapsed = 0.0;
        foreach (var key in timeline.Keyframes)
        {
            while (elapsed < key.Time - 1e-9)
            {
                system.Tick();
                elapsed += TelemetrySimulator.StepSeconds;
            }

            var t = system.Current;
            Assert.Equal(key.PumpEfficiency, t.PumpEfficiency, 2);
            Assert.InRange(t.CoolantPressure, key.CoolantPressurePsi - 5, key.CoolantPressurePsi + 5);
            Assert.InRange(t.Temperature, key.TemperatureC - 6, key.TemperatureC + 6);
        }
    }

    [Fact]
    public void Cooling_scenario_visits_states_in_order()
    {
        var system = new LumenSystem();
        system.Scenario.StartCoolingScenario();
        var sequence = new List<SystemState> { system.Current.State };
        for (var i = 0; i < 500; i++)
        {
            var s = system.Tick().State;
            if (s != sequence[^1]) sequence.Add(s);
        }

        Assert.Equal([SystemState.Nominal, SystemState.Elevated, SystemState.Degraded, SystemState.Recovering, SystemState.Nominal], sequence);
        Assert.False(system.Scenario.IsScenarioRunning);
        Assert.Empty(system.Current.ActiveFaults);
    }

    [Fact]
    public void Operator_recovery_jumps_scenario_to_recovery_phase()
    {
        var system = new LumenSystem();
        system.Scenario.StartCoolingScenario();
        for (var i = 0; i < 180; i++) system.Tick();
        Assert.Equal(SystemState.Elevated, system.Current.State);

        system.Scenario.InitiateRecovery();
        system.Tick();
        Assert.Equal(SystemState.Recovering, system.Current.State);
        Assert.True(system.Scenario.ScenarioTime >= system.Scenario.Timeline.RecoveryStart);

        for (var i = 0; i < 300; i++) system.Tick();
        Assert.Equal(SystemState.Nominal, system.Current.State);
    }

    [Theory]
    [InlineData(FaultKind.NetworkDegradation, SubsystemId.Communications)]
    [InlineData(FaultKind.SensorFailure, SubsystemId.Environment)]
    [InlineData(FaultKind.ComputeOverload, SubsystemId.Compute)]
    [InlineData(FaultKind.NavigationLoss, SubsystemId.Navigation)]
    [InlineData(FaultKind.PowerDeficit, SubsystemId.Power)]
    public void Each_fault_degrades_its_subsystem_and_recovers(FaultKind fault, SubsystemId affected)
    {
        var system = new LumenSystem();
        system.Scenario.Inject(fault);
        for (var i = 0; i < 100; i++) system.Tick();

        Assert.NotEqual(SystemState.Nominal, system.Current.State);
        Assert.NotEqual(SubsystemCondition.Nominal, system.Current[affected].Condition);
        Assert.Equal(affected, fault.AffectedSubsystem());

        system.Scenario.InitiateRecovery();
        system.Tick();
        Assert.Equal(SystemState.Recovering, system.Current.State);
        for (var i = 0; i < 200; i++) system.Tick();
        Assert.Equal(SystemState.Nominal, system.Current.State);
    }

    [Fact]
    public void Returning_to_nominal_without_operator_passes_through_recovering()
    {
        var system = new LumenSystem();
        system.Controls.LoadSetpoint = 1;
        var states = new List<SystemState>();
        for (var i = 0; i < 300; i++) states.Add(system.Tick().State);
        Assert.Contains(SystemState.Elevated, states);

        system.Controls.LoadSetpoint = SimulationControls.NominalLoad;
        states.Clear();
        for (var i = 0; i < 400; i++) states.Add(system.Tick().State);
        Assert.Contains(SystemState.Recovering, states);
        Assert.Equal(SystemState.Nominal, states[^1]);
    }

    [Fact]
    public void State_changes_are_logged()
    {
        var system = new LumenSystem();
        system.Scenario.Inject(FaultKind.NetworkDegradation);
        for (var i = 0; i < 100; i++) system.Tick();
        Assert.Contains(system.Log.Events, e => e.Message.Contains("injected"));
        Assert.Contains(system.Log.Events, e => e.Message.StartsWith("System "));
    }

    [Fact]
    public void Network_nodes_reflect_degradation()
    {
        var system = new LumenSystem();
        system.Tick();
        var calm = NetworkNodeModel.Evaluate(system.Current);
        Assert.Equal(6, calm.Count);
        Assert.All(calm, n => Assert.Equal(NodeConnection.Connected, n.Connection));

        system.Scenario.Inject(FaultKind.NetworkDegradation);
        for (var i = 0; i < 80; i++) system.Tick();
        var degraded = NetworkNodeModel.Evaluate(system.Current);
        Assert.Contains(degraded, n => n.Connection != NodeConnection.Connected);
    }
}
