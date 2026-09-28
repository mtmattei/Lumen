namespace Lumen.Presentation;

public enum FirstRunCue
{
    Black,
    Point,
    Heartbeat,
    HairlineScan,
    SystemTitle,
    ParticlesConverge,
    Nucleus,
    Membrane,
    Orbitals,
    PrimarySubsystems,
    NavigationAndHealth,
    SystemInitialized,
    TouchPrompt,
    Interactive,
}

/// <summary>
/// First-run timeline (seconds) from LUMEN_SPEC. Fully interactive at or before 10 s.
/// </summary>
public static class FirstRunTimeline
{
    public static readonly IReadOnlyList<(double Time, FirstRunCue Cue)> Cues =
    [
        (0.0, FirstRunCue.Black),
        (0.8, FirstRunCue.Point),
        (1.4, FirstRunCue.Heartbeat),
        (2.0, FirstRunCue.HairlineScan),
        (2.8, FirstRunCue.SystemTitle),
        (3.2, FirstRunCue.ParticlesConverge),
        (4.0, FirstRunCue.Nucleus),
        (4.8, FirstRunCue.Membrane),
        (5.3, FirstRunCue.Orbitals),
        (5.8, FirstRunCue.PrimarySubsystems),
        (6.5, FirstRunCue.NavigationAndHealth),
        (7.5, FirstRunCue.SystemInitialized),
        (8.5, FirstRunCue.TouchPrompt),
        (9.0, FirstRunCue.Interactive),
    ];

    public const double ReducedMotionScale = 0.16;

    public static double Duration(bool reducedMotion) => Cues[^1].Time * (reducedMotion ? ReducedMotionScale : 1);

    /// <summary>Cues whose time falls in (from, to]. Start with from = -1 to include the 0 s cue.</summary>
    public static IEnumerable<FirstRunCue> Between(double from, double to, bool reducedMotion = false)
    {
        var scale = reducedMotion ? ReducedMotionScale : 1;
        foreach (var (time, cue) in Cues)
        {
            var t = time * scale;
            if (t > from && t <= to) yield return cue;
        }
    }

    /// <summary>Core assembly progress: 0 before the point appears, 1 once orbitals are in.</summary>
    public static double CoreReveal(double t, bool reducedMotion = false)
    {
        var scale = reducedMotion ? ReducedMotionScale : 1;
        var start = 0.8 * scale;
        var end = 5.3 * scale;
        return Math.Clamp((t - start) / (end - start), 0, 1);
    }
}
