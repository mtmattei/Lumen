# Uno Architecture

## Stack
.NET 10; latest stable public Uno packages at implementation time; single-project Uno app; MVUX or MVVM consistently; DI; Rive behind presentation adapter.

## Structure
```text
Lumen/
  Presentation/{Shell,Overview,Energy,Explorer,Diagnostics,Network}/
  Domain/{SystemState,SubsystemId,SystemTelemetry,SubsystemTelemetry}.cs
  Services/{TelemetrySimulator,FaultScenarioService,DeviceSensorService,NetworkNodeService}.cs
  Rive/{ILumenCorePresenter,LumenCorePresenter,LumenVisualState,RiveEventAdapter}.cs
  Controls/{InstrumentSlider,InstrumentToggle,TelemetryReadout,StatusRail}.cs
  Styles/{Colors,Typography,Controls,Layout}.xaml
  Assets/Rive/lumen-core.riv
```

## Domain sketch
```csharp
public enum SystemState { Offline, Booting, Nominal, Elevated, Degraded, Critical, Recovering }
public enum SubsystemId { Power, Environment, Thermal, Navigation, Communications, Compute }
public sealed record SystemTelemetry(double PowerGeneration,double BatteryLevel,double SystemLoad,double Temperature,double CoolantPressure,double NetworkLatency,double AtmosphericPressure,double Humidity,double Co2,SystemState State);
public interface ILumenCorePresenter { void Apply(LumenVisualState state); void Trigger(LumenVisualTrigger trigger); }
```

## Simulation
Use deterministic causal telemetry, never independent random numbers. Load up -> consumption up -> battery drain up -> temperature up -> cooling demand up. Cooling fault: pump efficiency down -> pressure down -> temperature up -> Elevated -> Degraded/Critical. Thresholds stay in C#.

## Performance/testing
Telemetry ~10Hz. Rive interpolates. Avoid broad XAML invalidation on pointer movement. Unit-test domain transitions without Rive. Integration-test Rive event -> semantic command. UI-test keyboard/reduced-motion paths.
