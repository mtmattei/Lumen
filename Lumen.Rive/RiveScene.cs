using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace Lumen.Rive;

public enum RivePointer
{
    Down = 0,
    Move = 1,
    Up = 2,
}

/// <summary>A Rive event reported by the state machine. <see cref="Id"/> carries the custom `id` number property, if any.</summary>
public readonly record struct RiveEvent(string Name, float? Id);

/// <summary>
/// One artboard + state machine instance from a .riv file, rendered with SkiaSharp.
/// Not thread-safe: call it from one thread (the UI/render thread).
/// </summary>
public sealed unsafe class RiveScene : IDisposable
{
    private readonly List<RiveEvent> _events = [];
    private IntPtr _native;

    private RiveScene(IntPtr native)
    {
        _native = native;
        Width = RiveNative.lumen_rive_scene_width(native);
        Height = RiveNative.lumen_rive_scene_height(native);
    }

    public float Width { get; }

    public float Height { get; }

    /// <summary>True when liblumen_rive can be loaded on this platform.</summary>
    public static bool IsRuntimeAvailable
    {
        get
        {
            try
            {
                return RiveNative.lumen_rive_abi_version() == RiveNative.AbiVersion;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return false;
            }
        }
    }

    public static RiveScene Load(ReadOnlySpan<byte> riv, string artboard = "", string stateMachine = "")
    {
        SkiaBridge.EnsureRegistered();
        var native = RiveNative.lumen_rive_scene_new();
        int result;
        fixed (byte* bytes = riv)
        {
            result = RiveNative.lumen_rive_scene_load(native, bytes, riv.Length, artboard, stateMachine);
        }

        if (result != 1)
        {
            RiveNative.lumen_rive_scene_delete(native);
            throw new InvalidDataException(result switch
            {
                0 => "Not a readable .riv file for this runtime.",
                -1 => $"Artboard '{artboard}' not found.",
                _ => $"State machine '{stateMachine}' not found.",
            });
        }

        return new RiveScene(native);
    }

    public bool SetNumber(string name, float value) => RiveNative.lumen_rive_set_number(Native, name, value) == 1;

    public bool SetBool(string name, bool value) => RiveNative.lumen_rive_set_bool(Native, name, value ? 1 : 0) == 1;

    public bool Fire(string name) => RiveNative.lumen_rive_fire(Native, name) == 1;

    /// <summary>Advances the state machine and collects the events it reported.</summary>
    public bool Advance(double seconds)
    {
        var changed = RiveNative.lumen_rive_advance(Native, (float)seconds) == 1;
        CollectEvents();
        return changed;
    }

    /// <summary>Returns and clears the events collected since the last call.</summary>
    public IReadOnlyList<RiveEvent> TakeEvents()
    {
        if (_events.Count == 0) return [];
        var copy = _events.ToArray();
        _events.Clear();
        return copy;
    }

    /// <summary>Draws the artboard contain-fitted and centred in width × height, at the given opacity.</summary>
    public void Draw(SKCanvas canvas, float width, float height, float opacity = 1f)
    {
        var renderer = new SkiaRenderer(canvas, opacity);
        var handle = GCHandle.Alloc(renderer);
        try
        {
            RiveNative.lumen_rive_draw(Native, GCHandle.ToIntPtr(handle), width, height);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Pointer in surface coordinates (the same width × height passed to <see cref="Draw"/>).</summary>
    /// <remarks>Listener events are reported synchronously here and cleared by the next advance, so collect them now.</remarks>
    public void Pointer(RivePointer kind, float x, float y, float width, float height)
    {
        RiveNative.lumen_rive_pointer(Native, (int)kind, x, y, width, height);
        CollectEvents();
    }

    private void CollectEvents()
    {
        var count = RiveNative.lumen_rive_event_count(Native);
        for (var i = 0; i < count; i++)
        {
            _events.Add(new RiveEvent(EventName(i), EventNumber(i, "id")));
        }
    }

    private string EventName(int index)
    {
        var length = RiveNative.lumen_rive_event_name(Native, index, null, 0);
        if (length <= 0) return string.Empty;
        Span<byte> buffer = stackalloc byte[length];
        fixed (byte* p = buffer)
        {
            RiveNative.lumen_rive_event_name(Native, index, p, length);
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private float? EventNumber(int index, string property)
    {
        float value;
        return RiveNative.lumen_rive_event_number(Native, index, property, &value) == 1 ? value : null;
    }

    private IntPtr Native => _native != IntPtr.Zero ? _native : throw new ObjectDisposedException(nameof(RiveScene));

    public void Dispose()
    {
        if (_native == IntPtr.Zero) return;
        RiveNative.lumen_rive_scene_delete(_native);
        _native = IntPtr.Zero;
    }
}
