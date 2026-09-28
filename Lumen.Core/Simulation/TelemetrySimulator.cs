using Lumen.Domain;

namespace Lumen.Simulation;

/// <summary>
/// Deterministic causal telemetry model. No random numbers: ambient variation comes from fixed-period
/// sine terms of simulated time, so the same inputs always produce the same trace.
/// </summary>
public sealed class TelemetrySimulator
{
    public const double StepSeconds = 0.1;

    // Nominal reference values (spec-kit/fixtures/telemetry.nominal.json).
    public const double NominalGeneration = 12.4;
    public const double AmbientTemperature = 22;
    public const double NominalTemperature = 36;
    public const double NominalPressure = 58;
    public const double NominalLatency = 18;
    public const double NominalCo2 = 612;

    private const double BaseLoad = 1.6;
    private const double LoadRange = 12;
    private const double ComputeLoadRange = 4;
    private const double HeatGain = (NominalTemperature - AmbientTemperature) / 7.8;
    private const double BatteryCapacity = 2400; // kW·s per unit charge, compressed for demo pacing.
    private const double ThermalTau = 2.0;
    private const double PressureTau = 0.8;

    private readonly HashSet<FaultKind> _faults = [];
    private readonly SubsystemTelemetry[] _subsystems = new SubsystemTelemetry[6];

    public TelemetrySimulator()
    {
        Reset();
    }

    public SimulationControls Controls { get; private set; } = new();

    public SystemStateClassifier Classifier { get; } = new();

    public double SimTime { get; private set; }

    public double PumpEfficiency { get; private set; }

    /// <summary>When set, a scenario drives the pump directly (keyframed actuator).</summary>
    public double? PumpEfficiencyOverride { get; set; }

    public double BatteryLevel { get; private set; }

    public double Temperature { get; private set; }

    public double CoolantPressure { get; private set; }

    public double Heading { get; private set; }

    public double Co2 { get; private set; }

    public double Humidity { get; private set; }

    public double Latency { get; private set; }

    public IReadOnlyCollection<FaultKind> ActiveFaults => _faults;

    public SystemTelemetry Current { get; private set; } = null!;

    public void Reset()
    {
        SimTime = 0;
        Controls = new SimulationControls();
        PumpEfficiency = 1;
        PumpEfficiencyOverride = null;
        BatteryLevel = 0.83;
        Temperature = NominalTemperature;
        CoolantPressure = NominalPressure;
        Heading = SimulationControls.NominalHeading;
        Co2 = NominalCo2;
        Humidity = 0.42;
        Latency = NominalLatency;
        _faults.Clear();
        Classifier.SetLifecycle(SystemState.Nominal);
        Current = Snapshot(SystemState.Nominal);
    }

    /// <summary>Boot lifecycle hook: Offline/Booting hold telemetry at nominal, Nominal starts classification.</summary>
    public void SetLifecycle(SystemState state)
    {
        Classifier.SetLifecycle(state);
        Current = Snapshot(Classifier.State);
    }

    public void Inject(FaultKind fault) => _faults.Add(fault);

    public bool IsActive(FaultKind fault) => _faults.Contains(fault);

    /// <summary>Clears every fault and starts the Recovering path.</summary>
    public void BeginRecovery()
    {
        _faults.Clear();
        Classifier.RequestRecovery();
    }

    public SystemTelemetry Step(double dt = StepSeconds)
    {
        SimTime += dt;
        var t = SimTime;
        var c = Controls;

        // Power generation: solar with slow deterministic drift.
        var solar = NominalGeneration * (1 + 0.03 * Math.Sin(Tau * t / 47) + 0.02 * Math.Sin(Tau * t / 13.3));
        if (!c.SolarTracking) solar *= 0.85;
        if (IsActive(FaultKind.PowerDeficit)) solar *= 0.35;

        // Load: setpoint + compute allocation. Compute overload pins compute and adds load.
        var computeUtil = Math.Clamp(0.2 + c.ComputeAllocation * 0.8 + c.LoadSetpoint * 0.1, 0, 1);
        var load = BaseLoad + c.LoadSetpoint * LoadRange + c.ComputeAllocation * ComputeLoadRange;
        if (IsActive(FaultKind.ComputeOverload))
        {
            computeUtil = 0.98;
            load += 3.2;
        }

        load += 0.08 * Math.Sin(Tau * t / 7.1);

        // Battery integrates net power.
        BatteryLevel = Math.Clamp(BatteryLevel + (solar - load) / BatteryCapacity * dt, 0, 1);

        // Cooling: pump efficiency drives pressure, pressure drives capacity.
        if (PumpEfficiencyOverride is { } forced)
        {
            PumpEfficiency = forced;
        }
        else if (IsActive(FaultKind.CoolingFault))
        {
            PumpEfficiency = Math.Max(0.3, PumpEfficiency - 0.025 * dt);
        }
        else
        {
            PumpEfficiency = Math.Min(1, PumpEfficiency + 0.05 * dt);
        }

        var drive = Math.Clamp(PumpEfficiency * (1 + 0.35 * c.PumpBoost), 0, 1.35);
        var targetPressure = 20 + 38 * drive;
        CoolantPressure += (targetPressure - CoolantPressure) * Lag(dt, PressureTau);

        var pressureFactor = Math.Clamp((CoolantPressure - 20) / 38, 0, 1.35);
        var coolingCapacity = 0.1 + 0.9 * pressureFactor * pressureFactor;
        var targetTemperature = AmbientTemperature + HeatGain * load / coolingCapacity;
        Temperature += (targetTemperature - Temperature) * Lag(dt, ThermalTau);

        // Communications.
        var targetLatency = NominalLatency + 2 * Math.Sin(Tau * t / 5.3) + (c.SatelliteSync ? 0 : 26);
        if (IsActive(FaultKind.NetworkDegradation)) targetLatency += 210;
        Latency += (targetLatency - Latency) * Lag(dt, 1.2);

        // Environment. A failed sensor freezes its readings.
        if (!IsActive(FaultKind.SensorFailure))
        {
            var targetCo2 = 420 + 400 * (1 - c.Ventilation) + 6 * Math.Sin(Tau * t / 11);
            Co2 += (targetCo2 - Co2) * Lag(dt, 4);
            var targetHumidity = 0.34 + 0.16 * (1 - c.Ventilation) + 0.004 * Math.Sin(Tau * t / 17);
            Humidity += (targetHumidity - Humidity) * Lag(dt, 5);
        }

        // Navigation. Loss of navigation lets heading drift.
        if (IsActive(FaultKind.NavigationLoss))
        {
            Heading = Wrap(Heading + 3.5 * dt + 1.5 * Math.Sin(Tau * t / 3) * dt);
        }
        else
        {
            Heading = Wrap(Heading + ShortestAngle(Heading, c.HeadingTarget) * Lag(dt, 1.5));
        }

        ClassifySubsystems(solar, load, computeUtil);
        var state = Classifier.Update(dt, _faults.Count > 0);
        Current = Snapshot(state, solar, load, computeUtil);
        return Current;
    }

    private void ClassifySubsystems(double solar, double load, double computeUtil)
    {
        var k = Classifier;

        var powerRaw = SystemStateClassifier.BandBelow(BatteryLevel, SystemStateClassifier.BatteryDegraded, SystemStateClassifier.BatteryCritical, k.Previous(SubsystemId.Power));
        if (IsActive(FaultKind.PowerDeficit) && powerRaw < SubsystemCondition.Elevated) powerRaw = SubsystemCondition.Elevated;
        var net = solar - load;
        _subsystems[(int)SubsystemId.Power] = new(SubsystemId.Power, Health(1 - Math.Max(0, -net) / 10 - Math.Max(0, 0.5 - BatteryLevel)), k.Classify(SubsystemId.Power, powerRaw),
            net >= 0 ? "Generation surplus" : "Drawing from battery");

        var envRaw = SystemStateClassifier.Band(Co2, SystemStateClassifier.Co2Elevated, SystemStateClassifier.Co2Degraded, double.MaxValue, k.Previous(SubsystemId.Environment));
        if (IsActive(FaultKind.SensorFailure)) envRaw = Max(envRaw, SubsystemCondition.Degraded);
        _subsystems[(int)SubsystemId.Environment] = new(SubsystemId.Environment, Health(IsActive(FaultKind.SensorFailure) ? 0.45 : 1 - Math.Max(0, Co2 - 500) / 2000), k.Classify(SubsystemId.Environment, envRaw),
            IsActive(FaultKind.SensorFailure) ? "Sensor array not reporting" : "Atmosphere stable");

        var thermalRaw = SystemStateClassifier.Band(Temperature, SystemStateClassifier.ThermalElevated, SystemStateClassifier.ThermalDegraded, SystemStateClassifier.ThermalCritical, k.Previous(SubsystemId.Thermal));
        _subsystems[(int)SubsystemId.Thermal] = new(SubsystemId.Thermal, Health(1 - Math.Max(0, Temperature - 34) / 50), k.Classify(SubsystemId.Thermal, thermalRaw),
            PumpEfficiency < 0.9 ? $"Pump efficiency {PumpEfficiency:P0}" : "Coolant loop nominal");

        var navRaw = IsActive(FaultKind.NavigationLoss) ? SubsystemCondition.Degraded : SubsystemCondition.Nominal;
        _subsystems[(int)SubsystemId.Navigation] = new(SubsystemId.Navigation, Health(IsActive(FaultKind.NavigationLoss) ? 0.4 : 0.98), k.Classify(SubsystemId.Navigation, navRaw),
            IsActive(FaultKind.NavigationLoss) ? "Position fix lost" : "Holding heading");

        var commsRaw = SystemStateClassifier.Band(Latency, SystemStateClassifier.LatencyElevated, SystemStateClassifier.LatencyDegraded, SystemStateClassifier.LatencyCritical, k.Previous(SubsystemId.Communications));
        _subsystems[(int)SubsystemId.Communications] = new(SubsystemId.Communications, Health(1 - Math.Max(0, Latency - 16) / 300), k.Classify(SubsystemId.Communications, commsRaw),
            Latency > SystemStateClassifier.LatencyElevated ? "Link degraded" : "3 nodes linked");

        var computeRaw = computeUtil >= 0.97 ? SubsystemCondition.Degraded : computeUtil >= 0.9 ? SubsystemCondition.Elevated : SubsystemCondition.Nominal;
        _subsystems[(int)SubsystemId.Compute] = new(SubsystemId.Compute, Health(1 - Math.Max(0, computeUtil - 0.6) * 1.4), k.Classify(SubsystemId.Compute, computeRaw),
            $"Utilization {computeUtil:P0}");
    }

    private SystemTelemetry Snapshot(SystemState state, double? solar = null, double? load = null, double? computeUtil = null)
    {
        if (_subsystems[0] is null)
        {
            foreach (var id in Enum.GetValues<SubsystemId>())
            {
                _subsystems[(int)id] = new(id, 0.97, SubsystemCondition.Nominal, "Nominal");
            }
        }

        var subsystems = _subsystems.ToArray();
        var min = subsystems.Min(s => s.Health);
        var avg = subsystems.Average(s => s.Health);
        var health = state is SystemState.Offline ? 0 : Math.Round(0.6 * min + 0.4 * avg, 3);

        return new SystemTelemetry(
            SimTime,
            state,
            health,
            solar ?? NominalGeneration,
            BatteryLevel,
            load ?? 7.8,
            Temperature,
            CoolantPressure,
            PumpEfficiency,
            Latency,
            1.02 + 0.002 * Math.Sin(Tau * SimTime / 23),
            Humidity,
            Co2,
            21.4 + 0.1 * Math.Sin(Tau * SimTime / 31),
            0,
            421,
            Heading,
            computeUtil ?? 0.5,
            subsystems,
            _faults.Order().ToArray());
    }

    private const double Tau = Math.PI * 2;

    private static double Lag(double dt, double tau) => 1 - Math.Exp(-dt / tau);

    private static double Health(double value) => Math.Round(Math.Clamp(value, 0, 0.99), 3);

    private static SubsystemCondition Max(SubsystemCondition a, SubsystemCondition b) => a > b ? a : b;

    private static double Wrap(double degrees) => ((degrees % 360) + 360) % 360;

    private static double ShortestAngle(double from, double to) => ((to - from + 540) % 360) - 180;
}
