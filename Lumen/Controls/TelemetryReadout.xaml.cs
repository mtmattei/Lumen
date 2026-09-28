using Microsoft.UI.Xaml.Media;

namespace Lumen.Controls;

public sealed partial class TelemetryReadout : UserControl, IPulseReceiver
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(TelemetryReadout), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(TelemetryReadout), new PropertyMetadata(string.Empty, OnValueChanged));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(TelemetryReadout), new PropertyMetadata(string.Empty, (d, _) => ((TelemetryReadout)d).UpdateDetail()));
    public static readonly DependencyProperty BarValueProperty = DependencyProperty.Register(nameof(BarValue), typeof(double), typeof(TelemetryReadout), new PropertyMetadata(double.NaN, (d, _) => ((TelemetryReadout)d).UpdateBar()));
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(TelemetryReadout), new PropertyMetadata(null));
    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(TelemetryReadout), new PropertyMetadata(null));
    public static readonly DependencyProperty BarVisibilityProperty = DependencyProperty.Register(nameof(BarVisibility), typeof(Visibility), typeof(TelemetryReadout), new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty DetailVisibilityProperty = DependencyProperty.Register(nameof(DetailVisibility), typeof(Visibility), typeof(TelemetryReadout), new PropertyMetadata(Visibility.Collapsed));

    public TelemetryReadout()
    {
        InitializeComponent();
        ValueBrush = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        BarBrush = (Brush)Application.Current.Resources["PrimaryTextBrush"];
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    /// <summary>0..1 bar under the value; NaN hides it.</summary>
    public double BarValue { get => (double)GetValue(BarValueProperty); set => SetValue(BarValueProperty, value); }

    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

    public Visibility BarVisibility { get => (Visibility)GetValue(BarVisibilityProperty); private set => SetValue(BarVisibilityProperty, value); }

    public Visibility DetailVisibility { get => (Visibility)GetValue(DetailVisibilityProperty); private set => SetValue(DetailVisibilityProperty, value); }

    public void ReceivePulse() => PulseFlash.Play(PulseLine);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var readout = (TelemetryReadout)d;
        AutomationProperties.SetName(readout, $"{readout.Label} {e.NewValue}");
    }

    private void UpdateBar()
    {
        var visible = double.IsFinite(BarValue);
        BarVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) BarFill.Width = 112 * Math.Clamp(BarValue, 0, 1);
    }

    private void UpdateDetail() => DetailVisibility = string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
}
