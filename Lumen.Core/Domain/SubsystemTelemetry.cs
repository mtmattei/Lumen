namespace Lumen.Domain;

public enum SubsystemCondition
{
    Nominal,
    Elevated,
    Degraded,
    Critical,
}

/// <summary>Per-subsystem health derived by the simulator. Health is 0..1.</summary>
public sealed record SubsystemTelemetry(SubsystemId Id, double Health, SubsystemCondition Condition, string Summary);
