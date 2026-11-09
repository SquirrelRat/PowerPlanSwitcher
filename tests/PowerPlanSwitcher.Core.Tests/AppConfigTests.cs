using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void ScanInterval_DefaultsToTwoSeconds()
    {
        Assert.Equal(2, new AppConfig().ScanIntervalSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-30, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(3600, 60)]
    public void ScanIntervalSeconds_IsClampedToOneToSixty(int input, int expected)
    {
        var config = new AppConfig { ScanIntervalSeconds = input };

        Assert.Equal(expected, config.ScanIntervalSeconds);
    }
}
