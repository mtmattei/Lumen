using System.Collections.ObjectModel;
using System.Globalization;
using Lumen.Domain;
using Lumen.Presentation;
using Lumen.Simulation;
using Microsoft.UI.Xaml.Media;

namespace Lumen.Presentation.Shell;

public enum AppScreen
{
    Overview,
    Energy,
    Explorer,
    Diagnostics,
    Network,
}

/// <summary>
/// Shell state. Owns the 10 Hz projection of authoritative telemetry into bindable text and implements the
/// semantic commands that Core events map onto.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, ILumenCommands
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly LumenSystem _system;
    private ILumenCorePresenter? _presenter;
    private SystemState _lastState = SystemState.Offline;
    private int _logVersion = -1;
    private long _contactStartedAt;
    private bool _linkLogged;
    private bool _suppressControlWrites;

    public ShellViewModel(LumenSystem system)
    {
        _system = system;
        foreach (var id in Enum.GetValues<SubsystemId>())
        {
            Subsystems.Add(new SubsystemItem(id));
        }

        foreach (var node in NetworkNodeModel.Evaluate(system.Current))
        {
            NetworkNodes.Add(new NetworkNodeItem(node.Name, node.Platform));
        }

        ReadControls();
        ApplyTelemetry(system.Current);
    }

    public LumenSystem System => _system;

    public OnboardingCoach Coach { get; } = new();

    public ObservableCollection<EventItem> ActiveEvents { get; } = [];

    public ObservableCollection<SubsystemItem> Subsystems { get; } = [];

    public ObservableCollection<NetworkNodeItem> NetworkNodes { get; } = [];

    /// <summary>Raised when the Core pulse leaves the membrane; the view propagates it through Uno controls.</summary>
    public event EventHandler? PulsePropagationRequested;

    /// <summary>Raised after every telemetry tick with the new snapshot (sparklines, ring).</summary>
    public event EventHandler<SystemTelemetry>? TelemetryApplied;

    // ── Screen / mode ────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial AppScreen Screen { get; set; } = AppScreen.Overview;

    [ObservableProperty]
    public partial SubsystemId? SelectedSubsystem { get; set; }

    [ObservableProperty]
    public partial bool IsExploded { get; set; }

    public bool IsOverview => Screen == AppScreen.Overview;

    public bool IsEnergy => Screen == AppScreen.Energy;

    public bool IsExplorer => Screen == AppScreen.Explorer;

    public bool IsDiagnostics => Screen == AppScreen.Diagnostics;

    public bool IsNetwork => Screen == AppScreen.Network;

    public bool ShowOrbitReadouts => Screen is AppScreen.Overview or AppScreen.Diagnostics;

    public bool HasSelection => SelectedSubsystem is not null && IsExplorer;

    public bool HasNoSelection => !HasSelection;

    public string ScreenTitle => Screen switch
    {
        AppScreen.Overview => "OVERVIEW",
        AppScreen.Energy => "ENERGY FLOW",
        AppScreen.Explorer => "SYSTEM EXPLORER",
        AppScreen.Diagnostics => "DIAGNOSTICS",
        _ => "NETWORK",
    };

    partial void OnScreenChanged(AppScreen value)
    {
        if (value != AppScreen.Explorer && SelectedSubsystem is not null)
        {
            SelectedSubsystem = null;
        }

        _presenter?.SetMode(value switch
        {
            AppScreen.Energy => CoreMode.Flow,
            AppScreen.Explorer => CoreMode.Exploded,
            AppScreen.Network => CoreMode.Network,
            _ => CoreMode.Core,
        });

        OnPropertyChanged(nameof(IsOverview));
        OnPropertyChanged(nameof(IsEnergy));
        OnPropertyChanged(nameof(IsExplorer));
        OnPropertyChanged(nameof(IsDiagnostics));
        OnPropertyChanged(nameof(IsNetwork));
        OnPropertyChanged(nameof(ShowOrbitReadouts));
        OnPropertyChanged(nameof(ScreenTitle));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
    }

    partial void OnSelectedSubsystemChanged(SubsystemId? value)
    {
        _presenter?.Focus(value);
        if (value is not null) _presenter?.Trigger(LumenVisualTrigger.Inspect);
        foreach (var item in Subsystems) item.IsSelected = item.Id == value;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        UpdateInspector(_system.Current);
    }

    [RelayCommand]
    private void Navigate(string screen) => Screen = Enum.Parse<AppScreen>(screen);

    [RelayCommand]
    private void ToggleExplode() => Screen = Screen == AppScreen.Explorer ? AppScreen.Overview : AppScreen.Explorer;

    [RelayCommand]
    private void Inspect(SubsystemId id) => SelectSubsystem(id);

    [RelayCommand]
    private void CloseInspector() => SelectedSubsystem = null;

    // ── Condition ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial SystemState State { get; set; } = SystemState.Offline;

    [ObservableProperty]
    public partial string StateLabel { get; set; } = "OFFLINE";

    [ObservableProperty]
    public partial string StateGlyph { get; set; } = "○";

    [ObservableProperty]
    public partial string StateDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush StateBrush { get; set; } = Brushes.Offline;

    [ObservableProperty]
    public partial string HealthText { get; set; } = "—";

    [ObservableProperty]
    public partial double Health { get; set; }

    [ObservableProperty]
    public partial string CoreAccessibleName { get; set; } = "Living core";

    [ObservableProperty]
    public partial string ClockText { get; set; } = "T+00:00";

    // ── Primary telemetry ────────────────────────────────────────────────────

    [ObservableProperty] public partial string PowerText { get; set; } = "—";
    [ObservableProperty] public partial double PowerFraction { get; set; }
    [ObservableProperty] public partial string PowerDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnvironmentText { get; set; } = "—";
    [ObservableProperty] public partial string EnvironmentDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial string LatencyText { get; set; } = "—";
    [ObservableProperty] public partial double LatencyFraction { get; set; }
    [ObservableProperty] public partial string CommsDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial string NavigationText { get; set; } = "—";
    [ObservableProperty] public partial string NavigationDetail { get; set; } = string.Empty;

    [ObservableProperty] public partial string SolarText { get; set; } = "—";
    [ObservableProperty] public partial string BatteryText { get; set; } = "—";
    [ObservableProperty] public partial double BatteryFraction { get; set; }
    [ObservableProperty] public partial string LoadText { get; set; } = "—";
    [ObservableProperty] public partial string ThermalText { get; set; } = "—";
    [ObservableProperty] public partial Brush ThermalBrush { get; set; } = Brushes.Primary;
    [ObservableProperty] public partial string NetFlowText { get; set; } = "—";
    [ObservableProperty] public partial string NetFlowDirection { get; set; } = string.Empty;
    [ObservableProperty] public partial string PumpText { get; set; } = "—";
    [ObservableProperty] public partial string PressureText { get; set; } = "—";
    [ObservableProperty] public partial string ComputeText { get; set; } = "—";

    // ── Controls (write straight into the simulator) ────────────────────────

    [ObservableProperty] public partial double LoadSetpoint { get; set; }
    [ObservableProperty] public partial double ComputeAllocation { get; set; }
    [ObservableProperty] public partial double Ventilation { get; set; }
    [ObservableProperty] public partial double PumpBoost { get; set; }
    [ObservableProperty] public partial double HeadingTarget { get; set; }
    [ObservableProperty] public partial bool SolarTracking { get; set; }
    [ObservableProperty] public partial bool SatelliteSync { get; set; }

    public string LoadSetpointText => $"{LoadSetpoint:0} %";
    public string ComputeAllocationText => $"{ComputeAllocation:0} %";
    public string VentilationText => $"{Ventilation:0} %";
    public string PumpBoostText => $"+{PumpBoost:0} %";
    public string HeadingTargetText => $"{HeadingTarget:000}°";

    partial void OnLoadSetpointChanged(double value) => WriteControls(nameof(LoadSetpointText));
    partial void OnComputeAllocationChanged(double value) => WriteControls(nameof(ComputeAllocationText));
    partial void OnVentilationChanged(double value) => WriteControls(nameof(VentilationText));
    partial void OnPumpBoostChanged(double value) => WriteControls(nameof(PumpBoostText));
    partial void OnHeadingTargetChanged(double value) => WriteControls(nameof(HeadingTargetText));
    partial void OnSolarTrackingChanged(bool value) => WriteControls(null);
    partial void OnSatelliteSyncChanged(bool value) => WriteControls(null);

    private void WriteControls(string? textProperty)
    {
        if (textProperty is not null) OnPropertyChanged(textProperty);
        if (_suppressControlWrites) return;
        var c = _system.Controls;
        c.LoadSetpoint = LoadSetpoint / 100;
        c.ComputeAllocation = ComputeAllocation / 100;
        c.Ventilation = Ventilation / 100;
        c.PumpBoost = PumpBoost / 100;
        c.HeadingTarget = HeadingTarget;
        c.SolarTracking = SolarTracking;
        c.SatelliteSync = SatelliteSync;
    }

    private void ReadControls()
    {
        _suppressControlWrites = true;
        var c = _system.Controls;
        LoadSetpoint = c.LoadSetpoint * 100;
        ComputeAllocation = c.ComputeAllocation * 100;
        Ventilation = c.Ventilation * 100;
        PumpBoost = c.PumpBoost * 100;
        HeadingTarget = c.HeadingTarget;
        SolarTracking = c.SolarTracking;
        SatelliteSync = c.SatelliteSync;
        _suppressControlWrites = false;
    }

    // ── Inspector ────────────────────────────────────────────────────────────

    [ObservableProperty] public partial string InspectorTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectorCondition { get; set; } = string.Empty;
    [ObservableProperty] public partial Brush InspectorBrush { get; set; } = Brushes.Nominal;
    [ObservableProperty] public partial string InspectorHealth { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectorSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectorPrimaryLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectorPrimaryValue { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectorSecondaryLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectorSecondaryValue { get; set; } = string.Empty;

    public bool IsPowerSelected => SelectedSubsystem == SubsystemId.Power;
    public bool IsEnvironmentSelected => SelectedSubsystem == SubsystemId.Environment;
    public bool IsThermalSelected => SelectedSubsystem == SubsystemId.Thermal;
    public bool IsNavigationSelected => SelectedSubsystem == SubsystemId.Navigation;
    public bool IsCommunicationsSelected => SelectedSubsystem == SubsystemId.Communications;
    public bool IsComputeSelected => SelectedSubsystem == SubsystemId.Compute;

    private void UpdateInspector(SystemTelemetry t)
    {
        OnPropertyChanged(nameof(IsPowerSelected));
        OnPropertyChanged(nameof(IsEnvironmentSelected));
        OnPropertyChanged(nameof(IsThermalSelected));
        OnPropertyChanged(nameof(IsNavigationSelected));
        OnPropertyChanged(nameof(IsCommunicationsSelected));
        OnPropertyChanged(nameof(IsComputeSelected));
        if (SelectedSubsystem is not { } id) return;

        var s = t[id];
        InspectorTitle = id.ToString().ToUpperInvariant();
        InspectorCondition = $"{Glyph(s.Condition)} {s.Condition.ToString().ToUpperInvariant()}";
        InspectorBrush = Brushes.For(s.Condition);
        InspectorHealth = $"{s.Health * 100:0} %";
        InspectorSummary = s.Summary;
        (InspectorPrimaryLabel, InspectorPrimaryValue, InspectorSecondaryLabel, InspectorSecondaryValue) = id switch
        {
            SubsystemId.Power => ("GENERATION", F("{0:0.0} kW", t.PowerGeneration), "CONSUMPTION", F("{0:0.0} kW", t.SystemLoad)),
            SubsystemId.Environment => ("CO₂", F("{0:0} ppm", t.Co2), "HUMIDITY", F("{0:0} %", t.Humidity * 100)),
            SubsystemId.Thermal => ("CORE TEMP", F("{0:0.0} °C", t.Temperature), "COOLANT", F("{0:0} psi", t.CoolantPressure)),
            SubsystemId.Navigation => ("HEADING", F("{0:000}°", t.Heading), "ALTITUDE", F("{0:0} m", t.Altitude)),
            SubsystemId.Communications => ("LATENCY", F("{0:0} ms", t.NetworkLatency), "LINK", SatelliteSync ? "SAT SYNC" : "LTE ONLY"),
            _ => ("UTILIZATION", F("{0:0} %", t.ComputeUtilization * 100), "DRAW", F("{0:0.0} kW", ComputeAllocation / 100 * 4)),
        };
    }

    // ── Diagnostics ──────────────────────────────────────────────────────────

    [ObservableProperty] public partial string ScenarioStatus { get; set; } = "No scenario running";
    [ObservableProperty] public partial bool IsScenarioRunning { get; set; }
    [ObservableProperty] public partial bool HasActiveFaults { get; set; }
    [ObservableProperty] public partial bool HasNoEvents { get; set; } = true;

    [RelayCommand]
    private void InjectFault(string fault)
    {
        var kind = Enum.Parse<FaultKind>(fault);
        _system.Scenario.Inject(kind);
        _presenter?.Trigger(LumenVisualTrigger.Fault);
    }

    [RelayCommand]
    private void SimulateFault()
    {
        _system.Scenario.StartCoolingScenario();
        _presenter?.Trigger(LumenVisualTrigger.Fault);
    }

    [RelayCommand]
    private void InitiateRecovery() => _system.Scenario.InitiateRecovery();

    [RelayCommand]
    private void ResetControls()
    {
        var c = _system.Controls;
        c.LoadSetpoint = SimulationControls.NominalLoad;
        c.ComputeAllocation = SimulationControls.NominalCompute;
        c.Ventilation = SimulationControls.NominalVentilation;
        c.PumpBoost = 0;
        c.HeadingTarget = SimulationControls.NominalHeading;
        c.SolarTracking = true;
        c.SatelliteSync = true;
        ReadControls();
    }

    // ── Preferences / developer ──────────────────────────────────────────────

    [ObservableProperty] public partial bool ReducedMotion { get; set; }
    [ObservableProperty] public partial bool HighContrast { get; set; }
    [ObservableProperty] public partial bool DeveloperMode { get; set; }

    partial void OnReducedMotionChanged(bool value) => _presenter?.SetReducedMotion(value);

    [RelayCommand]
    private void ToggleReducedMotion() => ReducedMotion = !ReducedMotion;

    [RelayCommand]
    private void ToggleDeveloperMode() => DeveloperMode = !DeveloperMode;

    // ── Coach / first run ────────────────────────────────────────────────────

    [ObservableProperty] public partial string CoachPrompt { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsCoachVisible { get; set; }
    [ObservableProperty] public partial bool IsLoadControlRevealed { get; set; }
    [ObservableProperty] public partial bool IsInteractive { get; set; }

    public void ShowCoach()
    {
        Coach.Show();
        UpdateCoach();
    }

    public void SkipCoach()
    {
        Coach.Skip();
        UpdateCoach();
    }

    private void UpdateCoach()
    {
        CoachPrompt = Coach.Prompt;
        IsCoachVisible = Coach.Step is not (CoachStep.Hidden or CoachStep.Complete);
        IsLoadControlRevealed = Coach.IsLoadControlRevealed;
    }

    // ── ILumenCommands (Rive events → semantic commands) ────────────────────

    public void AttachPresenter(ILumenCorePresenter presenter)
    {
        _presenter = presenter;
        presenter.SetReducedMotion(ReducedMotion);
        OnScreenChanged(Screen);
        presenter.Apply(VisualStateMapper.Map(_system.Current));
    }

    public void BeginContact()
    {
        _contactStartedAt = Environment.TickCount64;
        Coach.OnContact();
        UpdateCoach();
    }

    public void ReachCharge()
    {
        Coach.OnCharged();
        UpdateCoach();
    }

    public void ReleaseCore(bool charged)
    {
        _contactStartedAt = 0;
        if (charged)
        {
            // Uno decides a pulse happens; the presenter only expresses it.
            _presenter?.Trigger(LumenVisualTrigger.Pulse);
        }
        else
        {
            Coach.OnReleasedEarly();
            UpdateCoach();
        }
    }

    public void CompletePulse()
    {
        PulsePropagationRequested?.Invoke(this, EventArgs.Empty);
        Coach.OnPulseCompleted();
        UpdateCoach();
        if (!_linkLogged)
        {
            _linkLogged = true;
            _system.Log.Add(_system.Current.SimTime, EventSeverity.Information, "Interface link established");
        }
    }

    public void SelectSubsystem(SubsystemId id)
    {
        if (Screen != AppScreen.Explorer) Screen = AppScreen.Explorer;
        SelectedSubsystem = SelectedSubsystem == id ? null : id;
    }

    public void ConfirmExploded(bool exploded) => IsExploded = exploded;

    public void AcknowledgeRecoveryVisual()
    {
    }

    // ── Telemetry tick ───────────────────────────────────────────────────────

    public void ApplyTelemetry(SystemTelemetry t)
    {
        if (_contactStartedAt != 0)
        {
            Coach.OnHeld(Environment.TickCount64 - _contactStartedAt);
            UpdateCoach();
        }

        if (Coach.Step == CoachStep.LinkEstablished && _linkLogged && t.SimTime > 0)
        {
            // Let the confirmation read for a moment, then step aside.
            _linkShownFor += 0.1;
            if (_linkShownFor > 3)
            {
                Coach.Dismiss();
                UpdateCoach();
            }
        }

        State = t.State;
        if (t.State != _lastState)
        {
            OnSystemStateTransition(_lastState, t.State);
            _lastState = t.State;
        }

        StateLabel = t.State.ToString().ToUpperInvariant();
        StateGlyph = Glyph(t.State);
        StateBrush = Brushes.For(t.State);
        StateDescription = Describe(t);
        Health = t.Health;
        HealthText = $"{t.Health * 100:0} %";
        ClockText = $"T+{(int)(t.SimTime / 60):00}:{(int)(t.SimTime % 60):00}";
        CoreAccessibleName = $"Living core. System {StateLabel.ToLowerInvariant()}, health {t.Health * 100:0} percent, load {t.SystemLoad:0.0} kilowatts, temperature {t.Temperature:0} degrees. Press and hold Space to charge; E to explore subsystems.";

        var power = t[SubsystemId.Power];
        PowerText = $"{power.Health * 100:0} %";
        PowerFraction = power.Health;
        PowerDetail = F("{0:0.0} kW IN\n{1:0.0} kW OUT", t.PowerGeneration, t.SystemLoad);

        EnvironmentText = F("{0:0.0}°", t.AmbientTemperature);
        EnvironmentDetail = t.ActiveFaults.Contains(FaultKind.SensorFailure)
            ? "ATM  —\nHUM  —\nCO₂  SENSOR OFFLINE"
            : F("ATM  {0:0.00} bar\nHUM  {1:0} %\nCO₂  {2:0} ppm", t.AtmosphericPressure, t.Humidity * 100, t.Co2);

        LatencyText = F("{0:0} ms", t.NetworkLatency);
        LatencyFraction = 1 - Math.Clamp(t.NetworkLatency / 250, 0, 1);
        CommsDetail = $"{NetworkNodes.Count(n => n.IsConnected)} NODES\n{(SatelliteSync ? "▹ LTE\n▹ SAT SYNC" : "▹ LTE")}";

        var nav = t[SubsystemId.Navigation];
        NavigationText = nav.Condition == SubsystemCondition.Nominal ? "NOMINAL" : "NO FIX";
        NavigationDetail = F("VEL  {0:0.0} m/s\nALT  {1:0} m\nHDG  {2:000}°", t.Velocity, t.Altitude, t.Heading);

        SolarText = F("{0:0.0} kW", t.PowerGeneration);
        BatteryText = F("{0:0} %", t.BatteryLevel * 100);
        BatteryFraction = t.BatteryLevel;
        LoadText = F("{0:0.0} kW", t.SystemLoad);
        ThermalText = F("{0:0} °C", t.Temperature);
        ThermalBrush = Brushes.For(t[SubsystemId.Thermal].Condition, neutralWhenNominal: true);
        NetFlowText = F("{0:+0.0;-0.0} kW", t.NetPower);
        NetFlowDirection = t.NetPower >= 0 ? "CHARGING  ·  CORE → BATTERY" : "DISCHARGING  ·  BATTERY → CORE";
        PumpText = F("{0:0} %", t.PumpEfficiency * 100);
        PressureText = F("{0:0} psi", t.CoolantPressure);
        ComputeText = F("{0:0} %", t.ComputeUtilization * 100);

        foreach (var item in Subsystems)
        {
            item.Update(t[item.Id]);
        }

        UpdateNetwork(t);
        UpdateInspector(t);
        UpdateEvents();

        var scenario = _system.Scenario;
        IsScenarioRunning = scenario.IsScenarioRunning;
        ScenarioStatus = scenario.IsScenarioRunning
            ? F("{0} · T+{1:00.0} s of {2:0} s", scenario.Timeline.Scenario.ToUpperInvariant(), scenario.ScenarioTime, scenario.Timeline.DurationSeconds)
            : t.ActiveFaults.Count > 0 ? $"{t.ActiveFaults.Count} fault(s) active" : "No scenario running";
        HasActiveFaults = t.ActiveFaults.Count > 0;

        _presenter?.Apply(VisualStateMapper.Map(t));
        TelemetryApplied?.Invoke(this, t);
    }

    private double _linkShownFor;

    private void OnSystemStateTransition(SystemState from, SystemState to)
    {
        if (_presenter is null) return;
        if (to == SystemState.Recovering)
        {
            _presenter.Trigger(LumenVisualTrigger.Recover);
        }
        else if (SystemStateClassifier.Severity(to) > SystemStateClassifier.Severity(from) && to != SystemState.Recovering)
        {
            _presenter.Trigger(LumenVisualTrigger.Fault);
        }
        else if (to == SystemState.Nominal && from is SystemState.Booting)
        {
            _presenter.Trigger(LumenVisualTrigger.Wake);
        }
    }

    private void UpdateNetwork(SystemTelemetry t)
    {
        var nodes = NetworkNodeModel.Evaluate(t);
        for (var i = 0; i < nodes.Count && i < NetworkNodes.Count; i++)
        {
            NetworkNodes[i].Update(nodes[i]);
        }

        _presenter?.SetNetwork(nodes);
    }

    private void UpdateEvents()
    {
        var log = _system.Log;
        if (log.Version == _logVersion) return;
        _logVersion = log.Version;
        ActiveEvents.Clear();
        foreach (var e in log.Events.Take(5))
        {
            ActiveEvents.Add(new EventItem(e));
        }

        HasNoEvents = ActiveEvents.Count == 0;
    }

    private static string Describe(SystemTelemetry t) => t.State switch
    {
        SystemState.Offline => "Core offline.",
        SystemState.Booting => "Subsystems coming online.",
        SystemState.Nominal => "All systems operating within parameters.",
        SystemState.Elevated => $"{Worst(t)} outside nominal band. Monitoring.",
        SystemState.Degraded => $"{Worst(t)} degraded. Investigate in Diagnostics.",
        SystemState.Critical => $"{Worst(t)} critical. Initiate recovery.",
        SystemState.Recovering => "Recovery under way. Systems realigning.",
        _ => string.Empty,
    };

    private static string Worst(SystemTelemetry t)
        => Enumerable.MaxBy(t.Subsystems, s => s.Condition)?.Id.ToString() ?? "System";

    public static string Glyph(SystemState state) => state switch
    {
        SystemState.Nominal => "●",
        SystemState.Elevated => "▲",
        SystemState.Degraded => "◆",
        SystemState.Critical => "■",
        SystemState.Recovering => "↻",
        SystemState.Booting => "◌",
        _ => "○",
    };

    public static string Glyph(SubsystemCondition condition) => condition switch
    {
        SubsystemCondition.Nominal => "●",
        SubsystemCondition.Elevated => "▲",
        SubsystemCondition.Degraded => "◆",
        _ => "■",
    };

    private static string F(string format, params object[] args) => string.Format(Invariant, format, args);
}
