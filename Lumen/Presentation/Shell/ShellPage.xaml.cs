using System.Globalization;
using System.Text;
using Lumen.Controls;
using Lumen.Domain;
using Lumen.Presentation;
using Lumen.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using Windows.Storage;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Lumen.Presentation.Shell;

public sealed partial class ShellPage : Page
{
    private enum LayoutMode
    {
        Wide,
        Medium,
        Narrow,
    }

    private const double WideMinWidth = 1180;
    private const double MediumMinWidth = 720;
    private const double PulseSpeedPxPerMs = 1.4;

    private readonly TelemetryHost _host;
    private readonly DeviceSensorService _sensors;
    private readonly RiveEventAdapter _adapter;
    private DispatcherQueueTimer? _firstRunTimer;
    private double _firstRunElapsed = -1;
    private bool _firstRunDone;
    private bool _ctrlDown;
    private bool _shiftDown;
    private LayoutMode? _layout;

    public ShellPage()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        _host = App.Services.GetRequiredService<TelemetryHost>();
        _sensors = App.Services.GetRequiredService<DeviceSensorService>();
        _adapter = new RiveEventAdapter(ViewModel);
        InitializeComponent();

        Loaded += OnLoaded;
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnPageKeyDown), handledEventsToo: true);
        AddHandler(KeyUpEvent, new KeyEventHandler(OnPageKeyUp), handledEventsToo: true);
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => SkipFirstRunIfIdle()), handledEventsToo: true);
    }

    public ShellViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var presenter = Core.Presenter;
        _adapter.Attach(presenter);
        Core.PressEventRaised += (_, ev) => _adapter.Route(ev);
        Core.ExploreRequested += (_, _) => ViewModel.ToggleExplodeCommand.Execute(null);
        Core.CollapseRequested += (_, _) =>
        {
            if (ViewModel.SelectedSubsystem is not null) ViewModel.SelectedSubsystem = null;
            else if (ViewModel.Screen != AppScreen.Overview) ViewModel.Screen = AppScreen.Overview;
        };

        ViewModel.ReducedMotion = ViewModel.ReducedMotion || !SystemAnimationsEnabled();
        ViewModel.AttachPresenter(presenter);
        ViewModel.PulsePropagationRequested += (_, _) => PropagatePulse();
        ViewModel.TelemetryApplied += (_, t) =>
        {
            Rail.Push(t);
            if (ViewModel.DeveloperMode) UpdateDeveloperOverlay();
        };
        ViewModel.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(ShellViewModel.Screen):
                    SyncNavSelection();
                    break;
                case nameof(ShellViewModel.DeveloperMode) when ViewModel.DeveloperMode:
                    UpdateDeveloperOverlay();
                    break;
            }
        };

        _sensors.GravityChanged += (_, g) =>
            DispatcherQueue.TryEnqueue(() => Core.SetGravity(ViewModel.ReducedMotion ? 0 : g.X, ViewModel.ReducedMotion ? 0 : g.Y));
        _sensors.Start();

        ApplyLayout(ActualWidth);
        _ = LoadCoreTypefaceAsync();
        StartFirstRun();
        _host.Start(DispatcherQueue);
    }

    // ── First run (≤ 10 s, skippable, no modal) ─────────────────────────────

    private void StartFirstRun()
    {
        ViewModel.System.SetLifecycle(SystemState.Offline);
        ViewModel.ApplyTelemetry(ViewModel.System.Current);
        Core.Presenter.SetReveal(0);

        _firstRunTimer = DispatcherQueue.CreateTimer();
        _firstRunTimer.Interval = TimeSpan.FromMilliseconds(33);
        var started = Environment.TickCount64;
        _firstRunTimer.Tick += (_, _) => AdvanceFirstRun((Environment.TickCount64 - started) / 1000.0);
        _firstRunTimer.Start();
    }

    private void AdvanceFirstRun(double elapsed)
    {
        var reduced = ViewModel.ReducedMotion;
        Core.Presenter.SetReveal(FirstRunTimeline.CoreReveal(elapsed, reduced));
        foreach (var cue in FirstRunTimeline.Between(_firstRunElapsed, elapsed, reduced))
        {
            OnCue(cue);
        }

        _firstRunElapsed = elapsed;
    }

    private void OnCue(FirstRunCue cue)
    {
        var presenter = Core.Presenter;
        switch (cue)
        {
            case FirstRunCue.Heartbeat:
                presenter.Trigger(LumenVisualTrigger.Wake);
                break;
            case FirstRunCue.SystemTitle:
                Fade(SystemTitle, 1, 600);
                break;
            case FirstRunCue.ParticlesConverge:
                ViewModel.System.SetLifecycle(SystemState.Booting);
                ViewModel.ApplyTelemetry(ViewModel.System.Current);
                break;
            case FirstRunCue.PrimarySubsystems:
                Fade(PowerReadout, 1, 500);
                Fade(EnvironmentReadout, 1, 500);
                break;
            case FirstRunCue.NavigationAndHealth:
                Fade(NavArea, 1, 500);
                Fade(ContextArea, 1, 500);
                Fade(Header, 1, 500);
                Fade(CommsReadout, 1, 500);
                Fade(NavigationReadout, 1, 500);
                break;
            case FirstRunCue.SystemInitialized:
                ViewModel.System.SetLifecycle(SystemState.Nominal);
                ViewModel.ApplyTelemetry(ViewModel.System.Current);
                Fade(SystemTitle, 0, 400);
                Fade(InitializedText, 1, 400);
                Fade(Rail, 1, 600);
                Fade(Footer, 1, 600);
                break;
            case FirstRunCue.TouchPrompt:
                Fade(InitializedText, 0, 400);
                ViewModel.ShowCoach();
                break;
            case FirstRunCue.Interactive:
                FinishFirstRun();
                break;
        }
    }

    private void FinishFirstRun()
    {
        if (_firstRunDone) return;
        _firstRunDone = true;
        _firstRunTimer?.Stop();
        Core.Presenter.SetReveal(1);
        foreach (var element in new UIElement[] { Header, NavArea, ContextArea, Rail, Footer, PowerReadout, EnvironmentReadout, CommsReadout, NavigationReadout })
        {
            if (element.Opacity < 1) Fade(element, 1, 300);
        }

        Fade(SystemTitle, 0, 200);
        Fade(InitializedText, 0, 200);
        if (ViewModel.System.Current.State is SystemState.Offline or SystemState.Booting)
        {
            ViewModel.System.SetLifecycle(SystemState.Nominal);
            ViewModel.ApplyTelemetry(ViewModel.System.Current);
        }

        ViewModel.IsInteractive = true;
        Core.Focus(FocusState.Programmatic);
    }

    private void SkipFirstRunIfIdle()
    {
        // Any input before the timeline ends jumps straight to interactive; the coach still shows.
        if (_firstRunDone || _firstRunElapsed < 1.0) return;
        ViewModel.ShowCoach();
        FinishFirstRun();
    }

    // ── Pulse: continues from the Core through Uno controls, nearest first ───

    private void PropagatePulse()
    {
        var origin = Core.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(Core.Presenter.Center.X, Core.Presenter.Center.Y));
        foreach (var element in FindPulseReceivers(Root))
        {
            if (element is not FrameworkElement fe || fe.ActualWidth <= 0 || !IsShown(fe)) continue;
            var centre = fe.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(fe.ActualWidth / 2, fe.ActualHeight / 2));
            var distance = Math.Sqrt(Math.Pow(centre.X - origin.X, 2) + Math.Pow(centre.Y - origin.Y, 2));
            _ = PulseAfter((IPulseReceiver)element, ViewModel.ReducedMotion ? 0 : distance / PulseSpeedPxPerMs);
        }
    }

    private static async Task PulseAfter(IPulseReceiver receiver, double delayMs)
    {
        if (delayMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
        receiver.ReceivePulse();
    }

    private static IEnumerable<DependencyObject> FindPulseReceivers(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is IPulseReceiver) yield return child;
            foreach (var nested in FindPulseReceivers(child)) yield return nested;
        }
    }

    private static bool IsShown(FrameworkElement element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    // ── Keyboard: Ctrl+Shift+D developer mode ────────────────────────────────

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl) _ctrlDown = true;
        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift) _shiftDown = true;
        if (e.Key == VirtualKey.D && _ctrlDown && _shiftDown)
        {
            ViewModel.ToggleDeveloperModeCommand.Execute(null);
            e.Handled = true;
        }

        SkipFirstRunIfIdle();
    }

    private void OnPageKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl) _ctrlDown = false;
        if (e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift) _shiftDown = false;
    }

    // ── Navigation sync ──────────────────────────────────────────────────────

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string screen }) ViewModel.NavigateCommand.Execute(screen);
    }

    private void SyncNavSelection()
    {
        var target = ViewModel.Screen switch
        {
            AppScreen.Energy => NavEnergy,
            AppScreen.Explorer => NavExplorer,
            AppScreen.Diagnostics => NavDiagnostics,
            AppScreen.Network => NavNetwork,
            _ => NavOverview,
        };
        if (target.IsChecked != true) target.IsChecked = true;
    }

    // ── Layout: desktop / tablet / mobile are composed, not scaled ──────────

    private void ApplyLayout(double width)
    {
        if (width <= 0) return;
        var mode = width >= WideMinWidth ? LayoutMode.Wide : width >= MediumMinWidth ? LayoutMode.Medium : LayoutMode.Narrow;
        if (mode == _layout) return;
        _layout = mode;

        var navStyle = (Style)Application.Current.Resources[mode == LayoutMode.Wide ? "NavItemStyle" : "NavItemHorizontalStyle"];
        foreach (var item in NavItems.Children.OfType<RadioButton>())
        {
            item.Style = navStyle;
            item.Content = mode == LayoutMode.Narrow ? ShortName((string)item.Tag) : LongName((string)item.Tag);
        }

        switch (mode)
        {
            case LayoutMode.Wide:
                NavColumn.Width = new GridLength(200);
                ContextColumn.Width = new GridLength(336);
                TopNavRow.Height = new GridLength(0);
                ConditionRow.Height = new GridLength(0);
                LowerContextRow.Height = new GridLength(0);
                StageRow.Height = new GridLength(1, GridUnitType.Star);
                Place(NavArea, row: 2, column: 0, columnSpan: 1);
                Place(Stage, row: 2, column: 1, columnSpan: 1);
                Place(ContextArea, row: 2, column: 2, columnSpan: 1);
                NavItems.Orientation = Orientation.Vertical;
                NavItems.Padding = new Thickness(0, 24, 0, 0);
                NavItems.HorizontalAlignment = HorizontalAlignment.Stretch;
                Meta.Visibility = Visibility.Visible;
                ContextArea.Padding = new Thickness(0, 0, 32, 0);
                CompactCondition.Visibility = Visibility.Collapsed;
                Footer.Visibility = Visibility.Visible;
                HeaderStatus.Visibility = Visibility.Visible;
                Rail.SetCompact(false);
                break;

            case LayoutMode.Medium:
                NavColumn.Width = new GridLength(0);
                ContextColumn.Width = new GridLength(0);
                TopNavRow.Height = GridLength.Auto;
                ConditionRow.Height = new GridLength(0);
                LowerContextRow.Height = new GridLength(0.62, GridUnitType.Star);
                StageRow.Height = new GridLength(1, GridUnitType.Star);
                Place(NavArea, row: 1, column: 0, columnSpan: 3);
                Place(Stage, row: 2, column: 0, columnSpan: 3);
                Place(ContextArea, row: 4, column: 0, columnSpan: 3);
                NavItems.Orientation = Orientation.Horizontal;
                NavItems.Padding = new Thickness(24, 0, 24, 0);
                NavItems.HorizontalAlignment = HorizontalAlignment.Center;
                Meta.Visibility = Visibility.Collapsed;
                ContextArea.Padding = new Thickness(48, 0, 48, 0);
                CompactCondition.Visibility = Visibility.Collapsed;
                Footer.Visibility = Visibility.Visible;
                HeaderStatus.Visibility = Visibility.Visible;
                Rail.SetCompact(false);
                break;

            case LayoutMode.Narrow:
                // Tactile remote: Core upper half, condition, essential telemetry, compact nav at the bottom.
                NavColumn.Width = new GridLength(0);
                ContextColumn.Width = new GridLength(0);
                TopNavRow.Height = new GridLength(0);
                ConditionRow.Height = GridLength.Auto;
                LowerContextRow.Height = new GridLength(0.7, GridUnitType.Star);
                StageRow.Height = new GridLength(1, GridUnitType.Star);
                Place(NavArea, row: 6, column: 0, columnSpan: 3);
                Place(Stage, row: 2, column: 0, columnSpan: 3);
                Place(ContextArea, row: 4, column: 0, columnSpan: 3);
                NavItems.Orientation = Orientation.Horizontal;
                NavItems.Padding = new Thickness(0);
                NavItems.HorizontalAlignment = HorizontalAlignment.Center;
                Meta.Visibility = Visibility.Collapsed;
                ContextArea.Padding = new Thickness(16, 0, 16, 0);
                CompactCondition.Visibility = Visibility.Visible;
                Footer.Visibility = Visibility.Collapsed;
                HeaderStatus.Visibility = Visibility.Collapsed;
                Rail.SetCompact(true);
                break;
        }

        OrbitReadouts.Opacity = mode == LayoutMode.Narrow ? 0 : 1;
        Header.Padding = mode == LayoutMode.Narrow ? new Thickness(16, 16, 16, 8) : new Thickness(32, 24, 32, 16);
    }

    private static void Place(FrameworkElement element, int row, int column, int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    private static string LongName(string tag) => tag switch
    {
        "Energy" => "ENERGY FLOW",
        "Explorer" => "SYSTEM EXPLORER",
        _ => tag.ToUpperInvariant(),
    };

    private static string ShortName(string tag) => tag switch
    {
        "Overview" => "CORE",
        "Energy" => "ENERGY",
        "Explorer" => "SYSTEMS",
        "Diagnostics" => "DIAG",
        _ => "NET",
    };

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => PositionOrbitReadouts(e.NewSize.Width, e.NewSize.Height);

    /// <summary>Primary telemetry sits on the Core's instrument axes (same geometry as the renderer).</summary>
    private void PositionOrbitReadouts(double width, double height)
    {
        var r = Math.Min(width, height) * Rive.LumenCoreRenderer.RadiusFactor;
        var ring = r * 1.5;
        var cx = width / 2;
        var cy = height / 2;
        var unbounded = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);

        // Short stages keep only the horizontal pair; the rail and context panel carry the rest.
        var roomy = height >= 560;
        EnvironmentReadout.Visibility = NavigationReadout.Visibility = roomy ? Visibility.Visible : Visibility.Collapsed;

        PowerReadout.Measure(unbounded);
        Canvas.SetLeft(PowerReadout, Math.Max(16, cx - ring * 1.1 - 24 - PowerReadout.DesiredSize.Width));
        Canvas.SetTop(PowerReadout, cy - 36);

        Canvas.SetLeft(CommsReadout, cx + ring * 1.1 + 24);
        Canvas.SetTop(CommsReadout, cy - 36);

        // Environment right of the upper axis; Navigation left of the lower axis; the prompt right of it.
        Canvas.SetLeft(EnvironmentReadout, cx + 24);
        Canvas.SetTop(EnvironmentReadout, Math.Max(8, cy - ring * 1.08));

        NavigationReadout.Measure(unbounded);
        Canvas.SetLeft(NavigationReadout, cx - 24 - NavigationReadout.DesiredSize.Width);
        Canvas.SetTop(NavigationReadout, Math.Min(height - NavigationReadout.DesiredSize.Height - 8, cy + ring * 1.02));

        Canvas.SetLeft(PromptStack, cx + 24);
        Canvas.SetTop(PromptStack, Math.Min(height - 72, cy + ring * 1.02 + 20));

        if (ViewModel.DeveloperMode) UpdateDeveloperOverlay();
    }

    // ── Developer overlay ────────────────────────────────────────────────────

    private void UpdateDeveloperOverlay()
    {
        DevOverlay.Children.Clear();
        AddRegion(Core, $"CORE · {Core.Presenter.Engine.ToUpperInvariant()} · LumenCore", "InformationBrush");
        AddRegion(NavArea, "UNO · NAVIGATION", "NominalBrush");
        AddRegion(ContextArea, "UNO · CONTEXT", "NominalBrush");
        AddRegion(Rail, "UNO · TELEMETRY RAIL", "NominalBrush");
        if (OrbitReadouts.Visibility == Visibility.Visible)
        {
            AddRegion(PowerReadout, "UNO", "NominalBrush");
            AddRegion(CommsReadout, "UNO", "NominalBrush");
        }

        var s = Core.Presenter.LastState;
        var i = Core.Presenter.LastInteraction;
        var b = new StringBuilder();
        b.AppendLine(CultureInfo.InvariantCulture, $"systemState      {s.SystemState}");
        b.AppendLine(CultureInfo.InvariantCulture, $"health           {s.Health:0.000}");
        b.AppendLine(CultureInfo.InvariantCulture, $"power            {s.Power:0.000}");
        b.AppendLine(CultureInfo.InvariantCulture, $"temperature      {s.Temperature:0.000}");
        b.AppendLine(CultureInfo.InvariantCulture, $"load             {s.Load:0.000}");
        b.AppendLine(CultureInfo.InvariantCulture, $"latency          {s.Latency:0.000}");
        b.AppendLine(CultureInfo.InvariantCulture, $"netFlow*         {s.NetFlow:+0.000;-0.000}");
        b.AppendLine(CultureInfo.InvariantCulture, $"stressed*        {s.StressedSubsystem?.ToString() ?? "—"}");
        b.AppendLine(CultureInfo.InvariantCulture, $"pointerX/Y       {i.PointerX:+0.00;-0.00} {i.PointerY:+0.00;-0.00}");
        b.AppendLine(CultureInfo.InvariantCulture, $"pointerDistance  {i.PointerDistance:0.00}");
        b.AppendLine(CultureInfo.InvariantCulture, $"interactionForce {i.InteractionForce:0.00}");
        b.AppendLine(CultureInfo.InvariantCulture, $"gravityX/Y       {i.GravityX:+0.00;-0.00} {i.GravityY:+0.00;-0.00}");
        b.AppendLine(CultureInfo.InvariantCulture, $"engine           {Core.Presenter.Engine}");
        b.AppendLine(CultureInfo.InvariantCulture, $"mode             {Core.Presenter.Mode}");
        b.AppendLine(CultureInfo.InvariantCulture, $"telemetry        10 Hz · render {Core.Presenter.FramesPerSecond:0} fps");
        b.Append("* contract extension");
        DevBindings.Text = b.ToString();
    }

    private void AddRegion(FrameworkElement element, string label, string brushKey)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        var brush = (Brush)Application.Current.Resources[brushKey];
        var origin = element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rect = new Rectangle
        {
            Width = element.ActualWidth,
            Height = element.ActualHeight,
            Stroke = brush,
            StrokeThickness = 1,
            StrokeDashArray = [4, 4],
        };
        Canvas.SetLeft(rect, origin.X);
        Canvas.SetTop(rect, origin.Y);
        DevOverlay.Children.Add(rect);

        var text = new TextBlock { Text = label, Foreground = brush, Style = (Style)Application.Current.Resources["CaptionTextStyle"] };
        text.Foreground = brush;
        Canvas.SetLeft(text, origin.X + 6);
        Canvas.SetTop(text, origin.Y + 4);
        DevOverlay.Children.Add(text);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Fade(UIElement element, double to, int ms)
    {
        var duration = ViewModel.ReducedMotion ? Math.Min(ms, 150) : ms;
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            storyboard.Stop();
            element.Opacity = to;
        };
        storyboard.Begin();
    }

    private static bool SystemAnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private async Task LoadCoreTypefaceAsync()
    {
        try
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/GeistMono-Regular.ttf"));
            using var stream = await file.OpenStreamForReadAsync();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.Position = 0;
            var typeface = SKTypeface.FromStream(copy);
            if (typeface is not null) Core.Presenter.SetTypeface(typeface);
        }
        catch (Exception)
        {
            // Default typeface stays; labels in the Core are secondary to the Uno text.
        }
    }
}
