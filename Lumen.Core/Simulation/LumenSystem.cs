using Lumen.Domain;

namespace Lumen.Simulation;

/// <summary>
/// Composition root for the authoritative model: simulator, scenario, and event log advance together.
/// </summary>
public sealed class LumenSystem
{
    public LumenSystem()
    {
        Simulator = new TelemetrySimulator();
        Log = new EventLog();
        Scenario = new FaultScenarioService(Simulator, Log);
    }

    public TelemetrySimulator Simulator { get; }

    public FaultScenarioService Scenario { get; }

    public EventLog Log { get; }

    public SystemTelemetry Current => Simulator.Current;

    public SimulationControls Controls => Simulator.Controls;

    /// <summary>Advance one fixed step. Callers accumulate real time and call this in 100 ms increments.</summary>
    public SystemTelemetry Tick(double dt = TelemetrySimulator.StepSeconds)
    {
        var before = Simulator.Current.State;
        Scenario.Advance(dt);
        var telemetry = Simulator.Step(dt);
        if (telemetry.State != before && before is not (SystemState.Offline or SystemState.Booting))
        {
            Log.Add(telemetry.SimTime, EventLog.SeverityFor(telemetry.State), $"System {telemetry.State.ToString().ToUpperInvariant()}");
        }

        return telemetry;
    }

    public void SetLifecycle(SystemState state) => Simulator.SetLifecycle(state);
}
