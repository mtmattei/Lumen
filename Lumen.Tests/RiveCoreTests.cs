using Lumen.Domain;
using Lumen.Rive;
using SkiaSharp;

namespace Lumen.Tests;

/// <summary>
/// lumen-core.riv through the native Rive runtime (liblumen_rive) and the SkiaSharp bridge, headless.
/// Skipped where native/lumen-rive has not been built for this platform.
/// </summary>
public class RiveCoreTests
{
    private const int Size = 500;
    private const double Frame = 1 / 60.0;

    private static RiveScene Load()
    {
        Assert.SkipUnless(RiveScene.IsRuntimeAvailable, "liblumen_rive not built for this platform (native/lumen-rive/build.*)");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lumen.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var riv = File.ReadAllBytes(Path.Combine(dir!.FullName, "Lumen", "Assets", "Rive", "lumen-core.riv"));
        return RiveScene.Load(riv, "LumenCore", "LumenCore");
    }

    private static List<RiveEvent> Run(RiveScene scene, double seconds)
    {
        var events = new List<RiveEvent>();
        for (var t = 0.0; t < seconds; t += Frame)
        {
            scene.Advance(Frame);
            events.AddRange(scene.TakeEvents());
        }

        return events;
    }

    private static SKBitmap Render(RiveScene scene)
    {
        var bitmap = new SKBitmap(Size, Size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x0B, 0x0C, 0x0C));
        scene.Draw(canvas, Size, Size);
        return bitmap;
    }

    private static double Difference(SKBitmap a, SKBitmap b)
    {
        long total = 0;
        for (var y = 0; y < Size; y += 2)
        for (var x = 0; x < Size; x += 2)
        {
            var p = a.GetPixel(x, y);
            var q = b.GetPixel(x, y);
            total += Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue);
        }

        return total / (Size / 2.0 * Size / 2.0);
    }

    private static (double Brightness, double WarmBias) Stats(SKBitmap bitmap)
    {
        double sum = 0, warm = 0;
        for (var y = 0; y < Size; y += 2)
        for (var x = 0; x < Size; x += 2)
        {
            var p = bitmap.GetPixel(x, y);
            sum += Math.Max(0, (p.Red + p.Green + p.Blue) / 3.0 - 12); // light above the canvas colour
            warm += p.Red - p.Blue;
        }

        var n = Size / 2.0 * Size / 2.0;
        return (sum / n, warm / n);
    }

    [Fact]
    public void Loads_with_contract_inputs()
    {
        using var scene = Load();
        Assert.Equal(500, scene.Width);
        foreach (var number in new[] { "systemState", "health", "power", "temperature", "load", "latency", "pointerX", "pointerY",
                     "pointerDistance", "interactionForce", "gravityX", "gravityY", "stressedSubsystem" })
        {
            Assert.True(scene.SetNumber(number, 0), number);
        }

        foreach (var trigger in new[] { "wake", "pulse", "fault", "recover", "explode", "collapse", "inspect", "acknowledge" })
        {
            Assert.True(scene.Fire(trigger), trigger);
        }

        Assert.True(scene.SetBool("pressed", false));
        Assert.False(scene.SetNumber("notAnInput", 1));
    }

    [Fact]
    public void Nominal_renders_and_keeps_moving()
    {
        using var scene = Load();
        scene.SetNumber("systemState", (int)SystemState.Nominal);
        Run(scene, 2);
        using var a = Render(scene);
        Run(scene, 0.7);
        using var b = Render(scene);

        Assert.True(Stats(a).Brightness > 2, $"brightness {Stats(a).Brightness:0.00}");
        Assert.True(Difference(a, b) > 0.3, $"difference {Difference(a, b):0.00}");
    }

    [Fact]
    public void System_state_changes_the_render()
    {
        using var scene = Load();
        scene.SetNumber("systemState", (int)SystemState.Offline);
        Run(scene, 1.5);
        using var offline = Render(scene);
        scene.SetNumber("systemState", (int)SystemState.Nominal);
        Run(scene, 1.5);
        using var nominal = Render(scene);
        scene.SetNumber("systemState", (int)SystemState.Critical);
        scene.SetNumber("stressedSubsystem", (int)SubsystemId.Thermal);
        scene.SetNumber("temperature", 0.95f);
        Run(scene, 1.5);
        using var critical = Render(scene);

        Assert.True(Stats(nominal).Brightness > Stats(offline).Brightness * 1.5, "Offline is near-dark");
        Assert.True(Stats(critical).WarmBias > Stats(nominal).WarmBias, "Critical adds hot oxide / heat");
    }

    [Fact]
    public void Triggers_report_completion_events()
    {
        using var scene = Load();
        scene.SetNumber("systemState", (int)SystemState.Nominal);
        Run(scene, 1);

        scene.Fire("pulse");
        Assert.Contains(Run(scene, 1.8), e => e.Name == "PulseCompleted");

        scene.Fire("recover");
        Assert.Contains(Run(scene, 2.2), e => e.Name == "RecoveryVisualCompleted");

        scene.Fire("explode");
        Assert.Contains(Run(scene, 1.4), e => e.Name == "ExplodeCompleted");

        scene.Fire("collapse");
        Assert.Contains(Run(scene, 1.2), e => e.Name == "CollapseCompleted");
    }

    [Fact]
    public void Pointer_press_hold_release_reports_contact_charge_release()
    {
        using var scene = Load();
        scene.SetNumber("systemState", (int)SystemState.Nominal);
        Run(scene, 1);

        scene.Pointer(RivePointer.Down, 265, 245, Size, Size);
        var pressed = scene.TakeEvents();
        var held = Run(scene, 1.1);
        scene.Pointer(RivePointer.Up, 265, 245, Size, Size);
        var released = scene.TakeEvents().Concat(Run(scene, 0.2));
        var names = pressed.Concat(held).Concat(released).Select(e => e.Name).ToList();

        Assert.Contains("CorePressed", names);
        Assert.Contains("CoreCharged", names);
        Assert.Contains("CoreReleased", names);
    }

    [Fact]
    public void Exploded_node_click_selects_subsystem_with_id()
    {
        using var scene = Load();
        scene.SetNumber("systemState", (int)SystemState.Nominal);
        Run(scene, 1);
        scene.Fire("explode");
        Run(scene, 1.4);

        var angle = 150 * Math.PI / 180; // Thermal
        var x = (float)(250 + Math.Cos(angle) * 222);
        var y = (float)(250 + Math.Sin(angle) * 222);
        scene.Pointer(RivePointer.Down, x, y, Size, Size);
        var events = scene.TakeEvents().Concat(Run(scene, 0.2)).ToList();
        scene.Pointer(RivePointer.Up, x, y, Size, Size);

        var selected = Assert.Single(events, e => e.Name == "SubsystemSelected");
        Assert.Equal((float)SubsystemId.Thermal, selected.Id);
    }

    [Fact]
    public void Fits_and_centres_in_any_surface()
    {
        using var scene = Load();
        scene.SetNumber("systemState", (int)SystemState.Nominal);
        Run(scene, 1.5);
        using var wide = new SKBitmap(1000, 500);
        using var canvas = new SKCanvas(wide);
        canvas.Clear(SKColors.Black);
        scene.Draw(canvas, 1000, 500);

        // Contain-fit: the Core is centred, the side margins stay empty.
        Assert.True(wide.GetPixel(500, 250).Red > 60, "centre lit");
        Assert.Equal(SKColors.Black, wide.GetPixel(40, 250));
        Assert.Equal(SKColors.Black, wide.GetPixel(960, 250));
    }

    /// <summary>Writes PNGs of every state when LUMEN_RIVE_SNAPSHOTS=&lt;dir&gt; is set (docs / visual review).</summary>
    [Fact]
    public void Snapshots()
    {
        var dir = Environment.GetEnvironmentVariable("LUMEN_RIVE_SNAPSHOTS");
        Assert.SkipWhen(string.IsNullOrEmpty(dir), "set LUMEN_RIVE_SNAPSHOTS to write PNGs");
        Directory.CreateDirectory(dir!);
        using var scene = Load();
        foreach (var state in Enum.GetValues<SystemState>())
        {
            scene.SetNumber("systemState", (int)state);
            scene.SetNumber("stressedSubsystem", state is SystemState.Degraded or SystemState.Critical ? (int)SubsystemId.Thermal : -1);
            Run(scene, state == SystemState.Booting ? 3.2 : 1.6);
            using var bitmap = Render(scene);
            using var file = File.Create(Path.Combine(dir!, $"native-{(int)state}-{state.ToString().ToLowerInvariant()}.png"));
            bitmap.Encode(file, SKEncodedImageFormat.Png, 100);
        }
    }
}
