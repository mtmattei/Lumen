using Lumen.Presentation.Shell;

namespace Lumen.Presentation.Energy;

public sealed partial class EnergyPanel : UserControl
{
    public EnergyPanel()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
    }

    public ShellViewModel ViewModel { get; }
}
