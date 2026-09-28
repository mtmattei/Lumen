namespace Lumen.Controls;

public sealed partial class InstrumentSlider : UserControl, IPulseReceiver
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(InstrumentSlider), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(InstrumentSlider), new PropertyMetadata(0d));
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(nameof(ValueText), typeof(string), typeof(InstrumentSlider), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(InstrumentSlider), new PropertyMetadata(0d));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(InstrumentSlider), new PropertyMetadata(100d));
    public static readonly DependencyProperty StepFrequencyProperty = DependencyProperty.Register(nameof(StepFrequency), typeof(double), typeof(InstrumentSlider), new PropertyMetadata(1d));
    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(InstrumentSlider), new PropertyMetadata(10d));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(InstrumentSlider), new PropertyMetadata(string.Empty, (d, e) => ((InstrumentSlider)d).HintVisibility = string.IsNullOrEmpty(e.NewValue as string) ? Visibility.Collapsed : Visibility.Visible));
    public static readonly DependencyProperty HintVisibilityProperty = DependencyProperty.Register(nameof(HintVisibility), typeof(Visibility), typeof(InstrumentSlider), new PropertyMetadata(Visibility.Collapsed));

    public InstrumentSlider()
    {
        InitializeComponent();
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public string ValueText { get => (string)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double StepFrequency { get => (double)GetValue(StepFrequencyProperty); set => SetValue(StepFrequencyProperty, value); }

    public double TickFrequency { get => (double)GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }

    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public Visibility HintVisibility { get => (Visibility)GetValue(HintVisibilityProperty); private set => SetValue(HintVisibilityProperty, value); }

    public void ReceivePulse() => PulseFlash.Play(PulseLine);
}
