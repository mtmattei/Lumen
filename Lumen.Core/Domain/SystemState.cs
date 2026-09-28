namespace Lumen.Domain;

public enum SystemState
{
    Offline,
    Booting,
    Nominal,
    Elevated,
    Degraded,
    Critical,
    Recovering,
}
