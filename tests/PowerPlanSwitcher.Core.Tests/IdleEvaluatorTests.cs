using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class IdleEvaluatorTests
{
    private static readonly IdleSettings Settings = new() { InputTimeoutMinutes = 5 };

    private static SystemSnapshot Snap(
        TimeSpan? idleTime = null,
        bool screensaver = false,
        bool locked = false) =>
        new(idleTime ?? TimeSpan.Zero, screensaver, locked, PowerSource.Ac);

    [Fact]
    public void FreshInput_IsActive()
    {
        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.FromSeconds(3)), Settings);

        Assert.False(decision.IsIdle);
        Assert.Equal("user active", decision.Reason);
    }

    [Fact]
    public void InputExactlyAtTimeout_IsIdle()
    {
        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.FromMinutes(5)), Settings);

        Assert.True(decision.IsIdle);
        Assert.Equal("no input for 5 min", decision.Reason);
    }

    [Fact]
    public void InputOneSecondBelowTimeout_IsActive()
    {
        var decision = IdleEvaluator.Evaluate(
            Snap(TimeSpan.FromMinutes(5).Subtract(TimeSpan.FromSeconds(1))),
            Settings);

        Assert.False(decision.IsIdle);
    }

    [Fact]
    public void ScreensaverRunning_IsIdleImmediately()
    {
        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.Zero, screensaver: true), Settings);

        Assert.True(decision.IsIdle);
        Assert.Equal("screen saver running", decision.Reason);
    }

    [Fact]
    public void WorkstationLocked_IsIdleImmediately()
    {
        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.Zero, locked: true), Settings);

        Assert.True(decision.IsIdle);
        Assert.Equal("workstation locked", decision.Reason);
    }

    [Fact]
    public void ScreensaverDisabled_FallsBackToInputTimeout()
    {
        var settings = new IdleSettings { InputTimeoutMinutes = 5, OnScreensaver = false };

        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.FromSeconds(1), screensaver: true), settings);

        Assert.False(decision.IsIdle);
        Assert.Equal("user active", decision.Reason);
    }

    [Fact]
    public void LockDisabled_FallsBackToInputTimeout()
    {
        var settings = new IdleSettings { InputTimeoutMinutes = 5, OnLock = false };

        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.FromSeconds(1), locked: true), settings);

        Assert.False(decision.IsIdle);
    }

    [Fact]
    public void LockWinsOverScreensaverInReason()
    {
        var decision = IdleEvaluator.Evaluate(Snap(TimeSpan.Zero, screensaver: true, locked: true), Settings);

        Assert.Equal("screen saver running", decision.Reason);
    }
}
