using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumen.Rive;

/// <summary>P/Invoke surface of liblumen_rive (native/lumen-rive/LumenRiveInterop.cpp).</summary>
internal static unsafe partial class RiveNative
{
    private const string Library = "lumen_rive";
    public const int AbiVersion = 1;

    static RiveNative()
    {
        // Probe runtimes/<rid>/native next to the app, which is where Lumen.Rive.csproj copies the library.
        NativeLibrary.SetDllImportResolver(typeof(RiveNative).Assembly, (name, assembly, path) =>
        {
            if (name != Library) return IntPtr.Zero;
            var file = OperatingSystem.IsWindows() ? "lumen_rive.dll"
                : OperatingSystem.IsMacOS() ? "liblumen_rive.dylib"
                : "liblumen_rive.so";
            var candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", file);
            if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
            return NativeLibrary.TryLoad(name, assembly, path, out handle) ? handle : IntPtr.Zero;
        });
    }

    [LibraryImport(Library)]
    public static partial int lumen_rive_abi_version();

    [LibraryImport(Library)]
    public static partial void lumen_rive_register(Callbacks* callbacks);

    [LibraryImport(Library)]
    public static partial IntPtr lumen_rive_scene_new();

    [LibraryImport(Library)]
    public static partial void lumen_rive_scene_delete(IntPtr scene);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int lumen_rive_scene_load(IntPtr scene, byte* bytes, int length, string artboard, string stateMachine);

    [LibraryImport(Library)]
    public static partial float lumen_rive_scene_width(IntPtr scene);

    [LibraryImport(Library)]
    public static partial float lumen_rive_scene_height(IntPtr scene);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int lumen_rive_set_number(IntPtr scene, string name, float value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int lumen_rive_set_bool(IntPtr scene, string name, int value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int lumen_rive_fire(IntPtr scene, string name);

    [LibraryImport(Library)]
    public static partial int lumen_rive_advance(IntPtr scene, float seconds);

    [LibraryImport(Library)]
    public static partial void lumen_rive_draw(IntPtr scene, IntPtr renderer, float width, float height);

    [LibraryImport(Library)]
    public static partial void lumen_rive_pointer(IntPtr scene, int kind, float x, float y, float width, float height);

    [LibraryImport(Library)]
    public static partial int lumen_rive_event_count(IntPtr scene);

    [LibraryImport(Library)]
    public static partial int lumen_rive_event_name(IntPtr scene, int index, byte* buffer, int capacity);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int lumen_rive_event_number(IntPtr scene, int index, string property, float* value);

    /// <summary>Must match LumenCallbacks field order exactly.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Callbacks
    {
        public delegate* unmanaged[Cdecl]<IntPtr> MakePath;
        public delegate* unmanaged[Cdecl]<IntPtr> MakePaint;
        public delegate* unmanaged[Cdecl]<IntPtr, void> Release;
        public delegate* unmanaged[Cdecl]<IntPtr, void> PathRewind;
        public delegate* unmanaged[Cdecl]<IntPtr, int, void> PathFillRule;
        public delegate* unmanaged[Cdecl]<IntPtr, float, float, void> PathMoveTo;
        public delegate* unmanaged[Cdecl]<IntPtr, float, float, void> PathLineTo;
        public delegate* unmanaged[Cdecl]<IntPtr, float, float, float, float, float, float, void> PathCubicTo;
        public delegate* unmanaged[Cdecl]<IntPtr, void> PathClose;
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, float, float, float, float, float, float, void> PathAddPath;
        public delegate* unmanaged[Cdecl]<IntPtr, int, void> PaintStyle;
        public delegate* unmanaged[Cdecl]<IntPtr, uint, void> PaintColor;
        public delegate* unmanaged[Cdecl]<IntPtr, float, void> PaintThickness;
        public delegate* unmanaged[Cdecl]<IntPtr, int, void> PaintJoin;
        public delegate* unmanaged[Cdecl]<IntPtr, int, void> PaintCap;
        public delegate* unmanaged[Cdecl]<IntPtr, int, void> PaintBlendMode;
        public delegate* unmanaged[Cdecl]<IntPtr, float, float, float, float, uint*, float*, int, void> PaintLinearGradient;
        public delegate* unmanaged[Cdecl]<IntPtr, float, float, float, uint*, float*, int, void> PaintRadialGradient;
        public delegate* unmanaged[Cdecl]<IntPtr, void> PaintClearShader;
        public delegate* unmanaged[Cdecl]<IntPtr, void> Save;
        public delegate* unmanaged[Cdecl]<IntPtr, void> Restore;
        public delegate* unmanaged[Cdecl]<IntPtr, float, float, float, float, float, float, void> Transform;
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void> DrawPath;
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> ClipPath;
        public delegate* unmanaged[Cdecl]<IntPtr, float, void> ModulateOpacity;
    }
}
