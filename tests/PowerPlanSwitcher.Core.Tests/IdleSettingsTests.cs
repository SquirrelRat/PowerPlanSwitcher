using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class IdleSettingsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(120, 120)]
    [InlineData(121, 120)]
    [InlineData(6000, 120)]
    public void InputTimeoutMinutes_IsClampedToOneToHundredTwenty(int input, int expected)
    {
        var settings = new IdleSettings { InputTimeoutMinutes = input };

        Assert.Equal(expected, settings.InputTimeoutMinutes);
    }

    [Fact]
    public void ScreensaverAndLockDefaultToEnabled()
    {
        var settings = new IdleSettings();

        Assert.True(settings.OnScreensaver);
        Assert.True(settings.OnLock);
    }
}
