namespace Lumen.Presentation;

public enum PointerZone
{
    Passive,
    Awareness,
    Attraction,
    Deformation,
    Direct,
}

/// <summary>
/// Pointer field bands from RIVE_CORE_SPEC, defined at a reference Core radius and scaled to the rendered Core.
/// </summary>
public static class PointerField
{
    /// <summary>Core radius (px) at which the spec's pixel bands apply.</summary>
    public const double ReferenceRadius = 160;

    public const double PassiveBeyond = 300;
    public const double AwarenessBeyond = 200;
    public const double AttractionBeyond = 100;
    public const double DeformationBeyond = 50;

    public static PointerZone Zone(double distancePx, double coreRadiusPx)
    {
        var d = distancePx * ReferenceRadius / Math.Max(1, coreRadiusPx);
        return d > PassiveBeyond ? PointerZone.Passive
            : d > AwarenessBeyond ? PointerZone.Awareness
            : d > AttractionBeyond ? PointerZone.Attraction
            : d > DeformationBeyond ? PointerZone.Deformation
            : PointerZone.Direct;
    }

    /// <summary>Returns (pointerX, pointerY, pointerDistance) for the state machine; distance 0 = centre, 1 = passive edge.</summary>
    public static (double X, double Y, double Distance) Normalize(double dx, double dy, double coreRadiusPx)
    {
        var scale = ReferenceRadius / Math.Max(1, coreRadiusPx);
        var reach = PassiveBeyond;
        var x = Math.Clamp(dx * scale / reach, -1, 1);
        var y = Math.Clamp(dy * scale / reach, -1, 1);
        var distance = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) * scale / reach, 0, 1);
        return (x, y, distance);
    }
}
