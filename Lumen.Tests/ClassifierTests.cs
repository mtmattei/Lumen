using Lumen.Domain;
using Lumen.Simulation;

namespace Lumen.Tests;

public class ClassifierTests
{
    [Theory]
    [InlineData(40, SubsystemCondition.Nominal, SubsystemCondition.Nominal)]
    [InlineData(48, SubsystemCondition.Nominal, SubsystemCondition.Elevated)]
    [InlineData(47, SubsystemCondition.Elevated, SubsystemCondition.Elevated)] // hysteresis holds
    [InlineData(45, SubsystemCondition.Elevated, SubsystemCondition.Nominal)]
    [InlineData(80, SubsystemCondition.Nominal, SubsystemCondition.Critical)]
    [InlineData(75, SubsystemCondition.Critical, SubsystemCondition.Critical)]
    [InlineData(70, SubsystemCondition.Critical, SubsystemCondition.Degraded)]
    public void Thermal_bands_apply_hysteresis(double temperature, SubsystemCondition previous, SubsystemCondition expected)
    {
        var actual = SystemStateClassifier.Band(temperature, SystemStateClassifier.ThermalElevated, SystemStateClassifier.ThermalDegraded, SystemStateClassifier.ThermalCritical, previous);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0.8, SubsystemCondition.Nominal)]
    [InlineData(0.29, SubsystemCondition.Degraded)]
    [InlineData(0.1, SubsystemCondition.Critical)]
    public void Battery_band_is_lower_is_worse(double battery, SubsystemCondition expected)
        => Assert.Equal(expected, SystemStateClassifier.BandBelow(battery, SystemStateClassifier.BatteryDegraded, SystemStateClassifier.BatteryCritical, SubsystemCondition.Nominal));

    [Fact]
    public void Boot_lifecycle_is_not_overridden_by_classification()
    {
        var classifier = new SystemStateClassifier();
        classifier.SetLifecycle(SystemState.Booting);
        classifier.Classify(SubsystemId.Thermal, SubsystemCondition.Critical);
        Assert.Equal(SystemState.Booting, classifier.Update(0.1, faultsActive: false));
    }

    [Fact]
    public void Recovering_settles_after_hold_time()
    {
        var classifier = new SystemStateClassifier();
        classifier.SetLifecycle(SystemState.Nominal);
        classifier.Classify(SubsystemId.Thermal, SubsystemCondition.Degraded);
        Assert.Equal(SystemState.Degraded, classifier.Update(0.1, true));

        classifier.Classify(SubsystemId.Thermal, SubsystemCondition.Nominal);
        classifier.RequestRecovery();
        Assert.Equal(SystemState.Recovering, classifier.Update(0.1, false));

        var elapsed = 0.0;
        while (classifier.Update(0.1, false) == SystemState.Recovering) elapsed += 0.1;
        Assert.InRange(elapsed, SystemStateClassifier.RecoverySettleSeconds - 0.2, SystemStateClassifier.RecoverySettleSeconds + 0.2);
    }

    [Fact]
    public void Timeline_fixture_loads()
    {
        var timeline = CoolingFaultTimeline.Load();
        Assert.Equal("Cooling Pump Degradation", timeline.Scenario);
        Assert.Equal(7, timeline.Keyframes.Count);
        Assert.Equal(32, timeline.RecoveryStart);
        Assert.Equal(0.465, timeline.PumpEfficiencyAt(19.5), 3);
    }
}
