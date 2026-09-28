using Lumen.Presentation.Shell;

namespace Lumen.Presentation.Network;

public sealed partial class NetworkPanel : UserControl
{
    public NetworkPanel()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
    }

    public ShellViewModel ViewModel { get; }
}
