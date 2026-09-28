namespace Lumen.Simulation;

/// <summary>
/// Operator-adjustable inputs. The Uno inspector writes these continuously; the simulator reads them each step.
/// All values are normalized 0..1 unless noted.
/// </summary>
public sealed class SimulationControls
{
    public const double NominalLoad = 0.4;
    public const double NominalCompute = 0.35;
    public const double NominalVentilation = 0.52;
    public const double NominalHeading = 182;

    /// <summary>SYSTEM LOAD setpoint.</summary>
    public double LoadSetpoint { get; set; } = NominalLoad;

    /// <summary>Compute allocation; adds electrical load and heat.</summary>
    public double ComputeAllocation { get; set; } = NominalCompute;

    /// <summary>Environment ventilation; drives CO2 and humidity targets.</summary>
    public double Ventilation { get; set; } = NominalVentilation;

    /// <summary>Extra coolant pump drive beyond the automatic baseline.</summary>
    public double PumpBoost { get; set; }

    /// <summary>Navigation heading target in degrees.</summary>
    public double HeadingTarget { get; set; } = NominalHeading;

    /// <summary>Solar array tracking; off costs ~15% generation.</summary>
    public bool SolarTracking { get; set; } = true;

    /// <summary>Satellite sync relay; off adds latency.</summary>
    public bool SatelliteSync { get; set; } = true;

    public SimulationControls Clone() => (SimulationControls)MemberwiseClone();
}
