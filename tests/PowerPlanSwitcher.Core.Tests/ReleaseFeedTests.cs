using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class ReleaseFeedTests
{
    private const string NormalRelease = """
        {
          "tag_name": "v0.3.1",
          "html_url": "https://github.com/SquirrelRat/PowerPlanSwitcher/releases/tag/v0.3.1",
          "assets": [
            {
              "name": "PowerPlanSwitcher.exe",
              "browser_download_url": "https://github.com/SquirrelRat/PowerPlanSwitcher/releases/download/v0.3.1/PowerPlanSwitcher.exe"
            },
            {
              "name": "PowerPlanSwitcher.exe.sha256",
              "browser_download_url": "https://github.com/SquirrelRat/PowerPlanSwitcher/releases/download/v0.3.1/PowerPlanSwitcher.exe.sha256"
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsTagAndBothAssetUrls()
    {
        ReleaseAssets? assets = ReleaseFeed.Parse(NormalRelease);

        Assert.NotNull(assets);
        Assert.Equal("v0.3.1", assets.Tag);
        Assert.Equal(
            "https://github.com/SquirrelRat/PowerPlanSwitcher/releases/download/v0.3.1/PowerPlanSwitcher.exe",
            assets.ExeUrl);
        Assert.EndsWith(
            "/PowerPlanSwitcher.exe.sha256",
            assets.HashUrl);
        Assert.Equal(
            "https://github.com/SquirrelRat/PowerPlanSwitcher/releases/tag/v0.3.1",
            assets.ReleasePageUrl);
    }

    [Fact]
    public void Parse_IgnoresExtraAssets()
    {
        string json = NormalRelease.Replace(
            "]",
            ",{ \"name\": \"release-notes.md\", \"browser_download_url\": \"https://example.com/notes\" } ]",
            StringComparison.Ordinal);

        ReleaseAssets? assets = ReleaseFeed.Parse(json);

        Assert.NotNull(assets);
        Assert.EndsWith("/PowerPlanSwitcher.exe", assets.ExeUrl);
    }

    [Fact]
    public void Parse_NullWithoutTheHashAsset()
    {
        string json = NormalRelease.Replace("PowerPlanSwitcher.exe.sha256", "checksums.txt", StringComparison.Ordinal);

        Assert.Null(ReleaseFeed.Parse(json));
    }

    [Fact]
    public void Parse_NullWithoutTheExeAsset()
    {
        string json = NormalRelease.Replace("\"PowerPlanSwitcher.exe\"", "\"other-tool.exe\"", StringComparison.Ordinal);

        Assert.Null(ReleaseFeed.Parse(json));
    }

    [Fact]
    public void Parse_NullWithoutAssets()
    {
        string json = """
            { "tag_name": "v0.3.1", "html_url": "https://example.com", "assets": [] }
            """;

        Assert.Null(ReleaseFeed.Parse(json));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    public void Parse_NullOnGarbage(string json)
    {
        Assert.Null(ReleaseFeed.Parse(json));
    }
}
