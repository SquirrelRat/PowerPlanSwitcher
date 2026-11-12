using System.Security.Cryptography;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class HashCheckTests
{
    private static readonly byte[] Payload =
        "Power Plan Switcher update payload"u8.ToArray();

    private static string Hex(byte[] bytes) =>
        Convert.ToHexString(bytes);

    [Fact]
    public void Verify_TrueForTheMatchingHash()
    {
        string assetText = Hex(SHA256.HashData(Payload)) + "  PowerPlanSwitcher.exe";

        Assert.True(HashCheck.Verify(assetText, Payload));
    }

    [Fact]
    public void Verify_TrueForUppercaseHexAndTrailingWhitespace()
    {
        string assetText = Hex(SHA256.HashData(Payload)).ToUpperInvariant() + "  PowerPlanSwitcher.exe\r\n";

        Assert.True(HashCheck.Verify(assetText, Payload));
    }

    [Fact]
    public void Verify_FalseForADifferentPayload()
    {
        string assetText = Hex(SHA256.HashData(Payload)) + "  PowerPlanSwitcher.exe";
        byte[] other = "something else"u8.ToArray();

        Assert.False(HashCheck.Verify(assetText, other));
    }

    [Fact]
    public void Verify_FalseForAGarbledAssetText()
    {
        Assert.False(HashCheck.Verify("not a hash", Payload));
        Assert.False(HashCheck.Verify("", Payload));
        Assert.False(HashCheck.Verify(null, Payload));
    }

    [Fact]
    public void Verify_FalseForAHashOfTheWrongLength()
    {
        string shortHex = Hex(SHA256.HashData(Payload))[..32];

        Assert.False(HashCheck.Verify(shortHex + "  PowerPlanSwitcher.exe", Payload));
    }
}
