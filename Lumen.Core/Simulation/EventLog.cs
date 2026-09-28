using Lumen.Domain;

namespace Lumen.Simulation;

public enum EventSeverity
{
    Information,
    Elevated,
    Critical,
    Resolved,
}

public sealed record SystemEvent(double SimTime, EventSeverity Severity, string Message);

/// <summary>Bounded, newest-first operator log.</summary>
public sealed class EventLog
{
    public const int Capacity = 24;
    private readonly List<SystemEvent> _events = [];

    public IReadOnlyList<SystemEvent> Events => _events;

    public int Version { get; private set; }

    public void Add(double simTime, EventSeverity severity, string message)
    {
        _events.Insert(0, new SystemEvent(simTime, severity, message));
        if (_events.Count > Capacity) _events.RemoveAt(_events.Count - 1);
        Version++;
    }

    public void Clear()
    {
        _events.Clear();
        Version++;
    }

    public static EventSeverity SeverityFor(SystemState state) => state switch
    {
        SystemState.Critical or SystemState.Degraded => EventSeverity.Critical,
        SystemState.Elevated => EventSeverity.Elevated,
        SystemState.Recovering or SystemState.Nominal => EventSeverity.Resolved,
        _ => EventSeverity.Information,
    };
}
