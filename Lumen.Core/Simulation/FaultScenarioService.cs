using Lumen.Domain;

namespace Lumen.Simulation;

/// <summary>
/// Plays the cooling-pump scenario and routes Diagnostics fault injection into the simulator.
/// </summary>
public sealed class FaultScenarioService(TelemetrySimulator simulator, EventLog log)
{
    private readonly CoolingFaultTimeline _timeline = CoolingFaultTimeline.Load();
    private bool _recovered;

    public CoolingFaultTimeline Timeline => _timeline;

    public bool IsScenarioRunning { get; private set; }

    public double ScenarioTime { get; private set; }

    public void StartCoolingScenario()
    {
        IsScenarioRunning = true;
        ScenarioTime = 0;
        _recovered = false;
        simulator.Inject(FaultKind.CoolingFault);
        simulator.PumpEfficiencyOverride = _timeline.PumpEfficiencyAt(0);
        log.Add(simulator.SimTime, EventSeverity.Information, $"Scenario started: {_timeline.Scenario}");
    }

    public void Inject(FaultKind fault)
    {
        if (fault == FaultKind.CoolingFault)
        {
            StartCoolingScenario();
            return;
        }

        if (simulator.IsActive(fault)) return;
        simulator.Inject(fault);
        log.Add(simulator.SimTime, EventSeverity.Elevated, $"{fault.DisplayName()} injected");
    }

    /// <summary>Operator-initiated recovery. During the scenario it jumps to the scripted recovery phase.</summary>
    public void InitiateRecovery()
    {
        if (IsScenarioRunning && !_recovered)
        {
            ScenarioTime = Math.Max(ScenarioTime, _timeline.RecoveryStart);
        }

        Recover("Recovery initiated by operator");
    }

    public void Advance(double dt)
    {
        if (!IsScenarioRunning) return;

        ScenarioTime += dt;
        simulator.PumpEfficiencyOverride = _timeline.PumpEfficiencyAt(ScenarioTime);

        if (!_recovered && ScenarioTime >= _timeline.RecoveryStart)
        {
            Recover("Coolant pump reset; recovery under way");
        }

        if (ScenarioTime >= _timeline.DurationSeconds)
        {
            IsScenarioRunning = false;
            simulator.PumpEfficiencyOverride = null;
        }
    }

    private void Recover(string message)
    {
        _recovered = true;
        simulator.BeginRecovery();
        log.Add(simulator.SimTime, EventSeverity.Resolved, message);
    }
}
