using Lumen.Domain;
using Microsoft.UI.Xaml.Media;

namespace Lumen.Presentation.Shell;

/// <summary>Semantic brushes resolved from Styles/Colors.xaml.</summary>
public static class Brushes
{
    private static Brush Get(string key) => (Brush)Application.Current.Resources[key];

    public static Brush Primary => Get("PrimaryTextBrush");
    public static Brush Secondary => Get("SecondaryTextBrush");
    public static Brush Nominal => Get("NominalBrush");
    public static Brush Information => Get("InformationBrush");
    public static Brush Elevated => Get("ElevatedAccentBrush");
    public static Brush Critical => Get("CriticalBrush");
    public static Brush Offline => Get("OfflineBrush");

    public static Brush For(SystemState state) => state switch
    {
        SystemState.Nominal => Nominal,
        SystemState.Elevated => Elevated,
        SystemState.Degraded or SystemState.Critical => Critical,
        SystemState.Recovering or SystemState.Booting => Information,
        _ => Offline,
    };

    public static Brush For(SubsystemCondition condition, bool neutralWhenNominal = false) => condition switch
    {
        SubsystemCondition.Nominal => neutralWhenNominal ? Primary : Nominal,
        SubsystemCondition.Elevated => Elevated,
        _ => Critical,
    };
}
