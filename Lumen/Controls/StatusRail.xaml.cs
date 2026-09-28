using System.Globalization;
using Lumen.Domain;
using Lumen.Presentation.Shell;
using SkiaSharp;

namespace Lumen.Controls;

public sealed partial class StatusRail : UserControl
{
    private bool _compact;

    public StatusRail()
    {
        InitializeComponent();
        SolarSpark.Color = new SKColor(0xE8, 0xB0, 0x70);
        LoadSpark.Color = new SKColor(0x8F, 0xB4, 0xD8);
        ThermalSpark.Color = new SKColor(0xD8, 0x65, 0x3F);
        BatteryReadout.BarBrush = Brushes.Nominal;
    }

    public void Push(SystemTelemetry t)
    {
        SolarReadout.Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0} kW", t.PowerGeneration);
        BatteryReadout.Value = string.Format(CultureInfo.InvariantCulture, "{0:0} %", t.BatteryLevel * 100);
        BatteryReadout.BarValue = t.BatteryLevel;
        BatteryReadout.BarBrush = t[SubsystemId.Power].Condition == SubsystemCondition.Nominal ? Brushes.Nominal : Brushes.For(t[SubsystemId.Power].Condition);
        LoadReadout.Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0} kW", t.SystemLoad);
        ThermalReadout.Value = string.Format(CultureInfo.InvariantCulture, "{0:0} °C", t.Temperature);
        ThermalReadout.ValueBrush = Brushes.For(t[SubsystemId.Thermal].Condition, neutralWhenNominal: true);

        SolarSpark.Push(t.PowerGeneration);
        LoadSpark.Push(t.SystemLoad);
        ThermalSpark.Push(t.Temperature);
    }

    /// <summary>Mobile: 2×2 grid, no sparklines. Recomposed rather than scaled.</summary>
    public void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;

        var spark = compact ? Visibility.Collapsed : Visibility.Visible;
        SolarSpark.Visibility = LoadSpark.Visibility = ThermalSpark.Visibility = spark;
        var padding = compact ? new Thickness(16, 12, 16, 12) : new Thickness(32, 20, 32, 20);
        foreach (var cell in new[] { SolarCell, BatteryCell, LoadCell, ThermalCell })
        {
            cell.Padding = padding;
        }

        RailGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        RailGrid.ColumnDefinitions[3].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(LoadCell, compact ? 0 : 2);
        Grid.SetRow(LoadCell, compact ? 1 : 0);
        Grid.SetColumn(ThermalCell, compact ? 1 : 3);
        Grid.SetRow(ThermalCell, compact ? 1 : 0);
        LoadCell.BorderThickness = compact ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        ThermalCell.BorderThickness = compact ? new Thickness(1, 1, 0, 0) : new Thickness(1, 0, 0, 0);
    }
}
