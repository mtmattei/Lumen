using Lumen.Domain;
using Lumen.Presentation.Shell;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lumen.Presentation.Explorer;

public sealed partial class ExplorerPanel : UserControl
{
    public ExplorerPanel()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.SelectedSubsystem) && ViewModel.SelectedSubsystem is not null)
            {
                Emerge();
            }
        };
    }

    public ShellViewModel ViewModel { get; }

    private void OnSubsystemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SubsystemId id }) ViewModel.SelectSubsystem(id);
    }

    /// <summary>The inspector slides in from the Core side, into the space the selected node opened.</summary>
    private void Emerge()
    {
        var duration = ViewModel.ReducedMotion ? 0 : 420;
        var slide = new DoubleAnimation
        {
            From = ViewModel.ReducedMotion ? 0 : -48,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(Math.Max(1, duration * 0.8)) };
        Storyboard.SetTarget(slide, InspectorShift);
        Storyboard.SetTargetProperty(slide, "X");
        Storyboard.SetTarget(fade, Inspector);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }
}
