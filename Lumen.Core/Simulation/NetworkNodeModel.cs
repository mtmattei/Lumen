using Lumen.Domain;

namespace Lumen.Simulation;

public enum NodeConnection
{
    Connected,
    Degraded,
    Reconnecting,
}

public sealed record NetworkNode(string Name, string Platform, double Latency, NodeConnection Connection, double LastTelemetryAge, double Angle);

/// <summary>
/// Simulated application instances observing System 01. Deterministic: latency is a function of sim time.
/// </summary>
public static class NetworkNodeModel
{
    private static readonly (string Name, string Platform, double BaseLatency, double Angle)[] Nodes =
    [
        ("Desk 01", "Windows", 12, -90),
        ("Studio", "macOS", 16, -30),
        ("Lab Rack", "Linux", 9, 30),
        ("Browser", "WebAssembly", 34, 90),
        ("Field Tablet", "iOS", 41, 150),
        ("Remote", "Android", 47, 210),
    ];

    public static IReadOnlyList<NetworkNode> Evaluate(SystemTelemetry telemetry)
    {
        var degraded = telemetry.ActiveFaults.Contains(FaultKind.NetworkDegradation);
        var t = telemetry.SimTime;
        var result = new NetworkNode[Nodes.Length];
        for (var i = 0; i < Nodes.Length; i++)
        {
            var (name, platform, baseLatency, angle) = Nodes[i];
            var latency = baseLatency + 3 * Math.Sin(t / (2.1 + i * 0.7)) + (telemetry.NetworkLatency - TelemetrySimulator.NominalLatency) * (0.6 + i * 0.12);
            var connection = latency > 250 ? NodeConnection.Reconnecting : latency > 80 ? NodeConnection.Degraded : NodeConnection.Connected;
            if (degraded && i == 4) connection = NodeConnection.Reconnecting;
            var age = connection == NodeConnection.Reconnecting ? 4 + (t % 6) : 0.1 + ((t * (1 + i * 0.3)) % 1.0) * 0.9 * (latency / 40);
            result[i] = new NetworkNode(name, platform, Math.Max(1, latency), connection, age, angle);
        }

        return result;
    }
}
