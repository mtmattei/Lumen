namespace Lumen.Domain;

/// <summary>
/// One authoritative telemetry snapshot. Units: kW, 0..1 fractions, °C, psi, ms, bar, ppm.
/// </summary>
public sealed record SystemTelemetry(
    double SimTime,
    SystemState State,
    double Health,
    double PowerGeneration,
    double BatteryLevel,
    double SystemLoad,
    double Temperature,
    double CoolantPressure,
    double PumpEfficiency,
    double NetworkLatency,
    double AtmosphericPressure,
    double Humidity,
    double Co2,
    double AmbientTemperature,
    double Velocity,
    double Altitude,
    double Heading,
    double ComputeUtilization,
    IReadOnlyList<SubsystemTelemetry> Subsystems,
    IReadOnlyList<FaultKind> ActiveFaults)
{
    public SubsystemTelemetry this[SubsystemId id] => Subsystems[(int)id];

    /// <summary>Net battery flow in kW; positive means charging.</summary>
    public double NetPower => PowerGeneration - SystemLoad;
}
