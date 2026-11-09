using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class RulesEngineTests
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private static readonly IdleSettings Settings = new() { InputTimeoutMinutes = 5 };

    private static SystemSnapshot Snap(
        TimeSpan? idleTime = null,
        bool screensaver = false,
        bool locked = false,
        PowerSource powerSource = PowerSource.Ac) =>
        new(idleTime ?? TimeSpan.Zero, screensaver, locked, powerSource);

    [Fact]
    public void ActiveOnAc_ReturnsTheActiveOnAcPlan()
    {
        var target = RulesEngine.Resolve(Snap(TimeSpan.FromSeconds(1)), Settings, new PlanRules(), null);

        Assert.Equal(Balanced, target.PlanGuid);
        Assert.False(target.Forced);
        Assert.Equal(Activity.Active, target.Activity);
        Assert.Equal("user active", target.Reason);
    }

    [Fact]
    public void IdleOnBattery_ReturnsTheIdleOnBatteryPlan()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromMinutes(6), powerSource: PowerSource.Battery),
            Settings,
            new PlanRules(),
            null);

        Assert.Equal(PowerSaver, target.PlanGuid);
        Assert.Equal(Activity.Idle, target.Activity);
        Assert.Equal("no input for 5 min", target.Reason);
    }

    [Fact]
    public void ForcedPlan_OverridesTheRules()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromMinutes(6)),
            Settings,
            new PlanRules(),
            HighPerformance);

        Assert.Equal(HighPerformance, target.PlanGuid);
        Assert.True(target.Forced);
        Assert.Equal("no input for 5 min", target.Reason);
    }

    [Fact]
    public void ForcedPlan_StillReportsTheLiveActivity()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromSeconds(1)),
            Settings,
            new PlanRules(),
            PowerSaver);

        Assert.Equal(PowerSaver, target.PlanGuid);
        Assert.True(target.Forced);
        Assert.Equal(Activity.Active, target.Activity);
        Assert.Equal("user active", target.Reason);
    }

    [Fact]
    public void UnsetCell_ReturnsNullPlanGuid()
    {
        var rules = new PlanRules();
        rules.Set(Activity.Active, PowerSource.Ac, null);

        var target = RulesEngine.Resolve(Snap(TimeSpan.FromSeconds(1)), Settings, rules, null);

        Assert.Null(target.PlanGuid);
        Assert.Equal(Activity.Active, target.Activity);
    }

    [Fact]
    public void ActiveOnBattery_ReturnsTheActiveOnBatteryPlan()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromSeconds(1), powerSource: PowerSource.Battery),
            Settings,
            new PlanRules(),
            null);

        Assert.Equal(PowerSaver, target.PlanGuid);
        Assert.False(target.Forced);
        Assert.Equal(Activity.Active, target.Activity);
        Assert.Equal(PowerSource.Battery, target.PowerSource);
    }

    [Fact]
    public void IdleOnAc_ReturnsTheIdleOnAcPlan()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromMinutes(6), powerSource: PowerSource.Ac),
            Settings,
            new PlanRules(),
            null);

        Assert.Equal(PowerSaver, target.PlanGuid);
        Assert.Equal(Activity.Idle, target.Activity);
        Assert.Equal(PowerSource.Ac, target.PowerSource);
    }

    [Fact]
    public void UnknownPowerSource_LeavesThePlanAlone()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromSeconds(1), powerSource: PowerSource.Unknown),
            Settings,
            new PlanRules(),
            null);

        Assert.Null(target.PlanGuid);
        Assert.Equal(PowerSource.Unknown, target.PowerSource);
        Assert.False(target.Forced);
        Assert.Equal("power source unknown", target.Reason);
    }

    [Fact]
    public void UnknownPowerSource_StillHonoursAForcedPlan()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromSeconds(1), powerSource: PowerSource.Unknown),
            Settings,
            new PlanRules(),
            HighPerformance);

        Assert.Equal(HighPerformance, target.PlanGuid);
        Assert.True(target.Forced);
    }

    [Fact]
    public void EmptyForcedGuid_TreatedAsAutomatic()
    {
        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromSeconds(1)),
            Settings,
            new PlanRules(),
            Guid.Empty);

        Assert.Equal(Balanced, target.PlanGuid);
        Assert.False(target.Forced);
    }

    [Fact]
    public void Locked_CountsAsIdle()
    {
        var target = RulesEngine.Resolve(Snap(TimeSpan.Zero, locked: true), Settings, new PlanRules(), null);

        Assert.Equal(PowerSaver, target.PlanGuid);
        Assert.Equal(Activity.Idle, target.Activity);
        Assert.Equal("workstation locked", target.Reason);
    }

    [Fact]
    public void ScreensaverDisabled_WithSaverRunning_StaysActive()
    {
        var idleSettings = new IdleSettings { InputTimeoutMinutes = 5, OnScreensaver = false };

        var target = RulesEngine.Resolve(
            Snap(TimeSpan.FromSeconds(1), screensaver: true),
            idleSettings,
            new PlanRules(),
            null);

        Assert.Equal(Balanced, target.PlanGuid);
        Assert.Equal(Activity.Active, target.Activity);
        Assert.Equal("user active", target.Reason);
    }
}
