using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class PowerSourceClassifierTests
{
    [Theory]
    [InlineData(0, 1, PowerSource.Battery)]
    [InlineData(1, 1, PowerSource.Ac)]
    [InlineData(255, 1, PowerSource.Ac)]
    [InlineData(0, 128, PowerSource.Ac)]
    [InlineData(1, 128, PowerSource.Ac)]
    [InlineData(0, 129, PowerSource.Ac)]
    [InlineData(255, 255, PowerSource.Ac)]
    public void Classify_MapsWin32Values(byte acLine, byte flag, PowerSource expected)
    {
        Assert.Equal(expected, PowerSourceClassifier.Classify(acLine, flag));
    }
}
