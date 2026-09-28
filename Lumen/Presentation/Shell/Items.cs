using System.Globalization;
using Lumen.Domain;
using Lumen.Simulation;
using Microsoft.UI.Xaml.Media;

namespace Lumen.Presentation.Shell;

public sealed partial class SubsystemItem(SubsystemId id) : ObservableObject
{
    public SubsystemId Id { get; } = id;

    public string Name { get; } = id.ToString().ToUpperInvariant();

    [ObservableProperty] public partial string Condition { get; set; } = "● NOMINAL";
    [ObservableProperty] public partial string HealthText { get; set; } = "—";
    [ObservableProperty] public partial Brush ConditionBrush { get; set; } = Brushes.Nominal;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial string AccessibleName { get; set; } = id.ToString();

    public void Update(SubsystemTelemetry s)
    {
        Condition = $"{ShellViewModel.Glyph(s.Condition)} {s.Condition.ToString().ToUpperInvariant()}";
        HealthText = $"{s.Health * 100:0} %";
        ConditionBrush = Brushes.For(s.Condition);
        AccessibleName = $"{Id}, {s.Condition}, health {s.Health * 100:0} percent. {s.Summary}";
    }
}

public sealed partial class NetworkNodeItem(string name, string platform) : ObservableObject
{
    public string Name { get; } = name.ToUpperInvariant();

    public string Platform { get; } = platform.ToUpperInvariant();

    [ObservableProperty] public partial string LatencyText { get; set; } = "—";
    [ObservableProperty] public partial string ConnectionText { get; set; } = "—";
    [ObservableProperty] public partial string LastTelemetryText { get; set; } = "—";
    [ObservableProperty] public partial Brush ConnectionBrush { get; set; } = Brushes.Information;
    [ObservableProperty] public partial bool IsConnected { get; set; } = true;

    public void Update(NetworkNode node)
    {
        LatencyText = string.Format(CultureInfo.InvariantCulture, "{0:0} ms", node.Latency);
        ConnectionText = node.Connection switch
        {
            NodeConnection.Connected => "● CONNECTED",
            NodeConnection.Degraded => "▲ DEGRADED",
            _ => "◌ RECONNECTING",
        };
        ConnectionBrush = node.Connection switch
        {
            NodeConnection.Connected => Brushes.Information,
            NodeConnection.Degraded => Brushes.Elevated,
            _ => Brushes.Critical,
        };
        IsConnected = node.Connection != NodeConnection.Reconnecting;
        LastTelemetryText = string.Format(CultureInfo.InvariantCulture, "{0:0.0} s ago", node.LastTelemetryAge);
    }
}

public sealed class EventItem(SystemEvent e)
{
    public string Message { get; } = e.Message;

    public string Time { get; } = $"T+{(int)(e.SimTime / 60):00}:{(int)(e.SimTime % 60):00}";

    public string Glyph { get; } = e.Severity switch
    {
        EventSeverity.Critical => "■",
        EventSeverity.Elevated => "▲",
        EventSeverity.Resolved => "●",
        _ => "○",
    };

    public Brush Brush { get; } = e.Severity switch
    {
        EventSeverity.Critical => Brushes.Critical,
        EventSeverity.Elevated => Brushes.Elevated,
        EventSeverity.Resolved => Brushes.Nominal,
        _ => Brushes.Information,
    };
}
