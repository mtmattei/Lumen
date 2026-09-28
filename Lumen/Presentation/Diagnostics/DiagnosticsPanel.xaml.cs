using Lumen.Presentation.Shell;

namespace Lumen.Presentation.Diagnostics;

public sealed partial class DiagnosticsPanel : UserControl
{
    public DiagnosticsPanel()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
    }

    public ShellViewModel ViewModel { get; }
}
