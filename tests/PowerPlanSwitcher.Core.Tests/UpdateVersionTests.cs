using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v0.3.1", 0, 3, 1)]
    [InlineData("0.3.1", 0, 3, 1)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("v10.20.30", 10, 20, 30)]
    public void TryParse_AcceptsThreePartVersions(string text, int major, int minor, int build)
    {
        bool parsed = UpdateVersion.TryParse(text, out Version? version);

        Assert.True(parsed);
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("v0.3")]
    [InlineData("development")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("version 1.2.3")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        bool parsed = UpdateVersion.TryParse(text, out Version? version);

        Assert.False(parsed);
        Assert.Null(version);
    }

    [Fact]
    public void TryParse_StripsAPrereleaseSuffix()
    {
        bool parsed = UpdateVersion.TryParse("v0.3.1-beta", out Version? version);

        Assert.True(parsed);
        Assert.Equal(new Version(0, 3, 1), version);
    }

    [Fact]
    public void IsNewer_TrueForAHigherTag()
    {
        Assert.True(UpdateVersion.IsNewer("v0.3.1", "0.3.0"));
    }

    [Fact]
    public void IsNewer_FalseForTheSameVersion()
    {
        Assert.False(UpdateVersion.IsNewer("v0.3.1", "0.3.1"));
        Assert.False(UpdateVersion.IsNewer("v0.3.1-beta", "0.3.1"));
    }

    [Fact]
    public void IsNewer_FalseForALowerTag()
    {
        Assert.False(UpdateVersion.IsNewer("v0.2.9", "0.3.0"));
    }

    [Fact]
    public void IsNewer_FalseForAnUnparseableTagOrCurrent()
    {
        Assert.False(UpdateVersion.IsNewer("development", "0.3.0"));
        Assert.False(UpdateVersion.IsNewer(null, "0.3.0"));
        Assert.False(UpdateVersion.IsNewer("v0.3.2", "unknown"));
    }
}
