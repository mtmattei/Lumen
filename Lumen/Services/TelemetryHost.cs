using Lumen.Domain;
using Lumen.Presentation.Shell;
using Lumen.Simulation;
using Microsoft.UI.Dispatching;

namespace Lumen.Services;

/// <summary>
/// Runs the authoritative model at ~10 Hz on the UI dispatcher in fixed 100 ms steps (deterministic).
/// </summary>
public sealed class TelemetryHost(LumenSystem system, ShellViewModel viewModel)
{
    private DispatcherQueueTimer? _timer;
    private long _last;
    private double _accumulator;

    public void Start(DispatcherQueue dispatcher)
    {
        if (_timer is not null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += (_, _) => OnTick();
        _last = Environment.TickCount64;
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private void OnTick()
    {
        var now = Environment.TickCount64;
        _accumulator += Math.Min(0.5, (now - _last) / 1000.0);
        _last = now;

        var stepped = false;
        while (_accumulator >= TelemetrySimulator.StepSeconds)
        {
            _accumulator -= TelemetrySimulator.StepSeconds;
            if (system.Current.State is SystemState.Offline or SystemState.Booting)
            {
                continue; // first run holds the model until the Core is assembled
            }

            system.Tick();
            stepped = true;
        }

        if (stepped) viewModel.ApplyTelemetry(system.Current);
    }
}
