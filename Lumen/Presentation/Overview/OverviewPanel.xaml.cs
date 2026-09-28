using Lumen.Domain;
using Lumen.Presentation.Shell;
using SkiaSharp;

namespace Lumen.Presentation.Overview;

public sealed partial class OverviewPanel : UserControl
{
    public OverviewPanel()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        ViewModel.TelemetryApplied += OnTelemetry;
    }

    public ShellViewModel ViewModel { get; }

    private void OnTelemetry(object? sender, SystemTelemetry t)
    {
        Ring.Update(t.Health, ColorFor(t.State));
        Ring.Step(0.1);
    }

    private static SKColor ColorFor(SystemState state) => state switch
    {
        SystemState.Nominal => new SKColor(0x7F, 0xB8, 0x9A),
        SystemState.Elevated => new SKColor(0xD6, 0xA5, 0x5C),
        SystemState.Degraded or SystemState.Critical => new SKColor(0xD8, 0x65, 0x3F),
        SystemState.Recovering or SystemState.Booting => new SKColor(0x8F, 0xB4, 0xD8),
        _ => new SKColor(0x5A, 0x5E, 0x5B),
    };
}
