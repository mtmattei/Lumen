using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Domain;

namespace Lumen.Simulation;

public sealed record TimelineKeyframe(
    [property: JsonPropertyName("t")] double Time,
    [property: JsonPropertyName("state")] SystemState State,
    [property: JsonPropertyName("pumpEfficiency")] double PumpEfficiency,
    [property: JsonPropertyName("coolantPressurePsi")] double CoolantPressurePsi,
    [property: JsonPropertyName("temperatureC")] double TemperatureC);

/// <summary>The primary demo scenario, loaded from spec-kit/fixtures/cooling-fault.timeline.json.</summary>
public sealed record CoolingFaultTimeline(
    [property: JsonPropertyName("scenario")] string Scenario,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("keyframes")] IReadOnlyList<TimelineKeyframe> Keyframes)
{
    public static CoolingFaultTimeline Load()
    {
        using var stream = typeof(CoolingFaultTimeline).Assembly.GetManifestResourceStream("cooling-fault.timeline.json")
            ?? throw new InvalidOperationException("Embedded cooling-fault timeline missing.");
        return JsonSerializer.Deserialize(stream, FixtureJsonContext.Default.CoolingFaultTimeline)
            ?? throw new InvalidOperationException("Cooling-fault timeline is empty.");
    }

    /// <summary>First keyframe where the fixture expects Recovering: the scripted maintenance action.</summary>
    public double RecoveryStart => Keyframes.First(k => k.State == SystemState.Recovering).Time;

    public double PumpEfficiencyAt(double t)
    {
        if (t <= Keyframes[0].Time) return Keyframes[0].PumpEfficiency;
        for (var i = 1; i < Keyframes.Count; i++)
        {
            var b = Keyframes[i];
            if (t <= b.Time)
            {
                var a = Keyframes[i - 1];
                var u = (t - a.Time) / (b.Time - a.Time);
                return a.PumpEfficiency + (b.PumpEfficiency - a.PumpEfficiency) * u;
            }
        }

        return Keyframes[^1].PumpEfficiency;
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(CoolingFaultTimeline))]
internal sealed partial class FixtureJsonContext : JsonSerializerContext;
