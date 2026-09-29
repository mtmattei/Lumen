using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Lumen.Rive;

/// <summary>
/// Implements the Rive runtime's render interfaces with SkiaSharp. Native code holds GCHandles to these
/// objects and calls back through <see cref="RiveNative.Callbacks"/>.
/// </summary>
internal static unsafe class SkiaBridge
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            if (RiveNative.lumen_rive_abi_version() != RiveNative.AbiVersion)
            {
                throw new InvalidOperationException("liblumen_rive ABI mismatch; rebuild native/lumen-rive.");
            }

            var callbacks = new RiveNative.Callbacks
            {
                MakePath = &MakePath,
                MakePaint = &MakePaint,
                Release = &Release,
                PathRewind = &PathRewind,
                PathFillRule = &PathFillRule,
                PathMoveTo = &PathMoveTo,
                PathLineTo = &PathLineTo,
                PathCubicTo = &PathCubicTo,
                PathClose = &PathClose,
                PathAddPath = &PathAddPath,
                PaintStyle = &PaintStyle,
                PaintColor = &PaintColor,
                PaintThickness = &PaintThickness,
                PaintJoin = &PaintJoin,
                PaintCap = &PaintCap,
                PaintBlendMode = &PaintBlendMode,
                PaintLinearGradient = &PaintLinearGradient,
                PaintRadialGradient = &PaintRadialGradient,
                PaintClearShader = &PaintClearShader,
                Save = &Save,
                Restore = &Restore,
                Transform = &Transform,
                DrawPath = &DrawPath,
                ClipPath = &ClipPath,
                ModulateOpacity = &ModulateOpacity,
            };
            RiveNative.lumen_rive_register(&callbacks);
            _registered = true;
        }
    }

    private static T Get<T>(IntPtr handle) => (T)GCHandle.FromIntPtr(handle).Target!;

    private static SKMatrix Matrix(float xx, float xy, float yx, float yy, float tx, float ty)
        => new(xx, yx, tx, xy, yy, ty, 0, 0, 1);

    // ── factory ──

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr MakePath() => GCHandle.ToIntPtr(GCHandle.Alloc(new SkiaPath()));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr MakePaint() => GCHandle.ToIntPtr(GCHandle.Alloc(new SkiaPaint()));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(IntPtr handle)
    {
        var gch = GCHandle.FromIntPtr(handle);
        (gch.Target as IDisposable)?.Dispose();
        gch.Free();
    }

    // ── path ──

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathRewind(IntPtr p) => Get<SkiaPath>(p).Path.Rewind();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathFillRule(IntPtr p, int rule)
        => Get<SkiaPath>(p).Path.FillType = rule == 1 ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathMoveTo(IntPtr p, float x, float y) => Get<SkiaPath>(p).Path.MoveTo(x, y);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathLineTo(IntPtr p, float x, float y) => Get<SkiaPath>(p).Path.LineTo(x, y);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathCubicTo(IntPtr p, float ox, float oy, float ix, float iy, float x, float y)
        => Get<SkiaPath>(p).Path.CubicTo(ox, oy, ix, iy, x, y);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathClose(IntPtr p) => Get<SkiaPath>(p).Path.Close();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PathAddPath(IntPtr p, IntPtr source, float xx, float xy, float yx, float yy, float tx, float ty)
    {
        var matrix = Matrix(xx, xy, yx, yy, tx, ty);
        Get<SkiaPath>(p).Path.AddPath(Get<SkiaPath>(source).Path, ref matrix, SKPathAddMode.Append);
    }

    // ── paint ──

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintStyle(IntPtr p, int style)
        => Get<SkiaPaint>(p).Paint.Style = style == 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintColor(IntPtr p, uint argb) => Get<SkiaPaint>(p).BaseColor = new SKColor(argb);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintThickness(IntPtr p, float thickness) => Get<SkiaPaint>(p).Paint.StrokeWidth = thickness;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintJoin(IntPtr p, int join) => Get<SkiaPaint>(p).Paint.StrokeJoin = (SKStrokeJoin)join;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintCap(IntPtr p, int cap) => Get<SkiaPaint>(p).Paint.StrokeCap = (SKStrokeCap)cap;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintBlendMode(IntPtr p, int mode) => Get<SkiaPaint>(p).Paint.BlendMode = (SKBlendMode)mode; // same values as Skia

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintLinearGradient(IntPtr p, float sx, float sy, float ex, float ey, uint* colors, float* stops, int count)
    {
        var (c, s) = Stops(colors, stops, count);
        Get<SkiaPaint>(p).SetShader(SKShader.CreateLinearGradient(new SKPoint(sx, sy), new SKPoint(ex, ey), c, s, SKShaderTileMode.Clamp));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintRadialGradient(IntPtr p, float cx, float cy, float radius, uint* colors, float* stops, int count)
    {
        var (c, s) = Stops(colors, stops, count);
        Get<SkiaPaint>(p).SetShader(SKShader.CreateRadialGradient(new SKPoint(cx, cy), Math.Max(radius, 1e-3f), c, s, SKShaderTileMode.Clamp));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PaintClearShader(IntPtr p) => Get<SkiaPaint>(p).SetShader(null);

    private static (SKColor[] Colors, float[] Stops) Stops(uint* colors, float* stops, int count)
    {
        var c = new SKColor[count];
        var s = new float[count];
        for (var i = 0; i < count; i++)
        {
            c[i] = new SKColor(colors[i]);
            s[i] = stops[i];
        }

        return (c, s);
    }

    // ── renderer ──

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Save(IntPtr r) => Get<SkiaRenderer>(r).Save();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Restore(IntPtr r) => Get<SkiaRenderer>(r).Restore();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Transform(IntPtr r, float xx, float xy, float yx, float yy, float tx, float ty)
    {
        var matrix = Matrix(xx, xy, yx, yy, tx, ty);
        Get<SkiaRenderer>(r).Canvas.Concat(ref matrix);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DrawPath(IntPtr r, IntPtr path, IntPtr paint)
        => Get<SkiaRenderer>(r).DrawPath(Get<SkiaPath>(path), Get<SkiaPaint>(paint));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClipPath(IntPtr r, IntPtr path)
        => Get<SkiaRenderer>(r).Canvas.ClipPath(Get<SkiaPath>(path).Path, SKClipOperation.Intersect, antialias: true);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ModulateOpacity(IntPtr r, float opacity) => Get<SkiaRenderer>(r).Opacity *= opacity;
}

internal sealed class SkiaPath : IDisposable
{
    public SKPath Path { get; } = new();

    public void Dispose() => Path.Dispose();
}

internal sealed class SkiaPaint : IDisposable
{
    public SKPaint Paint { get; } = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public SKColor BaseColor { get; set; } = SKColors.Black;

    public bool HasShader { get; private set; }

    public void SetShader(SKShader? shader)
    {
        Paint.Shader?.Dispose();
        Paint.Shader = shader;
        HasShader = shader is not null;
    }

    public void Dispose()
    {
        Paint.Shader?.Dispose();
        Paint.Dispose();
    }
}

/// <summary>Draw target for one frame. Tracks the modulated opacity stack that Rive scopes with save/restore.</summary>
internal sealed class SkiaRenderer(SKCanvas canvas, float opacity)
{
    private readonly Stack<float> _opacity = new();

    public SKCanvas Canvas { get; } = canvas;

    public float Opacity { get; set; } = opacity;

    public void Save()
    {
        _opacity.Push(Opacity);
        Canvas.Save();
    }

    public void Restore()
    {
        if (_opacity.Count > 0) Opacity = _opacity.Pop();
        Canvas.Restore();
    }

    public void DrawPath(SkiaPath path, SkiaPaint paint)
    {
        if (Opacity <= 0) return;
        // With a shader, the paint alpha modulates the gradient; otherwise it scales the solid colour.
        var baseColor = paint.HasShader ? SKColors.White : paint.BaseColor;
        paint.Paint.Color = baseColor.WithAlpha((byte)Math.Clamp(baseColor.Alpha * Opacity, 0, 255));
        Canvas.DrawPath(path.Path, paint.Paint);
    }
}
