using Lumen.Domain;

namespace Lumen.Presentation;

/// <summary>Normalizes authoritative telemetry into presenter inputs. No thresholds live here.</summary>
public static class VisualStateMapper
{
    public static LumenVisualState Map(SystemTelemetry t)
    {
        SubsystemId? stressed = null;
        var worst = SubsystemCondition.Nominal;
        foreach (var s in t.Subsystems)
        {
            if (s.Condition > worst)
            {
                worst = s.Condition;
                stressed = s.Id;
            }
        }

        return new LumenVisualState(
            t.State,
            Clamp01(t.Health),
            Clamp01(t.PowerGeneration / 15.0),
            Clamp01((t.Temperature - 20) / 70.0),
            Clamp01(t.SystemLoad / 16.0),
            Clamp01(Math.Log10(Math.Max(1, t.NetworkLatency)) / Math.Log10(500)),
            stressed);
    }

    private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
}
