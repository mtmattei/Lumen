using Lumen.Domain;

namespace Lumen.Simulation;

/// <summary>
/// Owns every threshold and state transition. Rive/presentation never decides state.
/// </summary>
public sealed class SystemStateClassifier
{
    // Thermal bands (°C) with hysteresis on the way down.
    public const double ThermalElevated = 48;
    public const double ThermalDegraded = 62;
    public const double ThermalCritical = 78;

    public const double LatencyElevated = 60;
    public const double LatencyDegraded = 150;
    public const double LatencyCritical = 400;

    public const double Co2Elevated = 1000;
    public const double Co2Degraded = 1500;

    public const double BatteryDegraded = 0.3;
    public const double BatteryCritical = 0.15;

    public const double HysteresisFraction = 0.05;

    /// <summary>Seconds the raw state must stay Nominal before Recovering settles to Nominal.</summary>
    public const double RecoverySettleSeconds = 4.0;

    private readonly SubsystemCondition[] _conditions = new SubsystemCondition[6];
    private double _settleTimer;
    private bool _recoveryRequested;

    public SystemState State { get; private set; } = SystemState.Offline;

    public IReadOnlyList<SubsystemCondition> Conditions => _conditions;

    /// <summary>Force a lifecycle state (Offline/Booting/Nominal) from the boot sequence.</summary>
    public void SetLifecycle(SystemState state)
    {
        State = state;
        _settleTimer = 0;
        _recoveryRequested = false;
        if (state is SystemState.Offline or SystemState.Booting or SystemState.Nominal)
        {
            Array.Fill(_conditions, SubsystemCondition.Nominal);
        }
    }

    /// <summary>Operator or scenario started recovery: the system enters Recovering on the next update.</summary>
    public void RequestRecovery() => _recoveryRequested = true;

    public SubsystemCondition Classify(SubsystemId id, SubsystemCondition raw) => _conditions[(int)id] = raw;

    public static SubsystemCondition Band(double value, double elevated, double degraded, double critical, SubsystemCondition previous)
    {
        // Rising uses exact thresholds; falling requires dropping below threshold minus a margin.
        var up = Raw(value, elevated, degraded, critical, 0);
        if (up >= previous)
        {
            return up;
        }

        var down = Raw(value, elevated, degraded, critical, HysteresisFraction);
        return down > up ? down : up;
    }

    /// <summary>Lower is worse (battery). Mirrors into the rising form; there is no Elevated band.</summary>
    public static SubsystemCondition BandBelow(double value, double degraded, double critical, SubsystemCondition previous)
        => Band(-value, -degraded, -degraded, -critical, previous);

    private static SubsystemCondition Raw(double value, double elevated, double degraded, double critical, double margin)
    {
        double M(double threshold) => threshold - Math.Abs(threshold) * margin;
        if (value >= M(critical)) return SubsystemCondition.Critical;
        if (value >= M(degraded)) return SubsystemCondition.Degraded;
        if (value >= M(elevated)) return SubsystemCondition.Elevated;
        return SubsystemCondition.Nominal;
    }

    public SubsystemCondition Previous(SubsystemId id) => _conditions[(int)id];

    /// <summary>Folds subsystem conditions into the system state, applying the Recovering path.</summary>
    public SystemState Update(double dt, bool faultsActive)
    {
        if (State is SystemState.Offline or SystemState.Booting)
        {
            return State;
        }

        var worst = _conditions.Max();
        var raw = worst switch
        {
            SubsystemCondition.Critical => SystemState.Critical,
            SubsystemCondition.Degraded => SystemState.Degraded,
            SubsystemCondition.Elevated => SystemState.Elevated,
            _ => SystemState.Nominal,
        };

        if (_recoveryRequested)
        {
            _recoveryRequested = false;
            if (raw != SystemState.Nominal || State != SystemState.Nominal)
            {
                State = SystemState.Recovering;
                _settleTimer = 0;
                return State;
            }
        }

        switch (State)
        {
            case SystemState.Recovering:
                if (faultsActive && Severity(raw) >= Severity(SystemState.Degraded))
                {
                    // A new fault overwhelmed recovery.
                    State = raw;
                    _settleTimer = 0;
                }
                else if (raw == SystemState.Nominal)
                {
                    _settleTimer += dt;
                    if (_settleTimer >= RecoverySettleSeconds)
                    {
                        State = SystemState.Nominal;
                        _settleTimer = 0;
                    }
                }
                else
                {
                    _settleTimer = 0;
                }

                break;

            default:
                if (Severity(raw) >= Severity(State))
                {
                    State = raw;
                }
                else if (faultsActive)
                {
                    // An unresolved fault holds the state until recovery begins.
                }
                else if (raw == SystemState.Nominal)
                {
                    // Every return to Nominal passes through Recovering.
                    State = SystemState.Recovering;
                    _settleTimer = 0;
                }
                else
                {
                    State = raw;
                }

                break;
        }

        return State;
    }

    public static int Severity(SystemState state) => state switch
    {
        SystemState.Critical => 4,
        SystemState.Degraded => 3,
        SystemState.Elevated => 2,
        SystemState.Recovering => 1,
        _ => 0,
    };
}
