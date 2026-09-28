using Windows.Devices.Sensors;

namespace Lumen.Services;

/// <summary>
/// Optional tilt input. Feeds gravityX/gravityY; absent sensors (desktop, browser) simply report nothing.
/// </summary>
public sealed class DeviceSensorService
{
    private Accelerometer? _accelerometer;

    public event EventHandler<(double X, double Y)>? GravityChanged;

    public bool IsAvailable => _accelerometer is not null;

    public void Start()
    {
        try
        {
            _accelerometer = Accelerometer.GetDefault();
        }
        catch (Exception)
        {
            _accelerometer = null;
        }

        if (_accelerometer is null) return;
        _accelerometer.ReportInterval = 50;
        _accelerometer.ReadingChanged += (_, e) =>
            GravityChanged?.Invoke(this, (Math.Clamp(e.Reading.AccelerationX, -1, 1), Math.Clamp(-e.Reading.AccelerationY, -1, 1)));
    }
}
