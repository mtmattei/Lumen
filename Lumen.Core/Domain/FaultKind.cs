namespace Lumen.Domain;

/// <summary>Faults that Diagnostics can inject. Each one has a primary affected subsystem.</summary>
public enum FaultKind
{
    CoolingFault,
    PowerDeficit,
    NetworkDegradation,
    SensorFailure,
    ComputeOverload,
    NavigationLoss,
}

public static class FaultKindExtensions
{
    public static SubsystemId AffectedSubsystem(this FaultKind fault) => fault switch
    {
        FaultKind.CoolingFault => SubsystemId.Thermal,
        FaultKind.PowerDeficit => SubsystemId.Power,
        FaultKind.NetworkDegradation => SubsystemId.Communications,
        FaultKind.SensorFailure => SubsystemId.Environment,
        FaultKind.ComputeOverload => SubsystemId.Compute,
        FaultKind.NavigationLoss => SubsystemId.Navigation,
        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
    };

    public static string DisplayName(this FaultKind fault) => fault switch
    {
        FaultKind.CoolingFault => "Cooling Fault",
        FaultKind.PowerDeficit => "Power Deficit",
        FaultKind.NetworkDegradation => "Network Degradation",
        FaultKind.SensorFailure => "Sensor Failure",
        FaultKind.ComputeOverload => "Compute Overload",
        FaultKind.NavigationLoss => "Navigation Loss",
        _ => fault.ToString(),
    };
}
