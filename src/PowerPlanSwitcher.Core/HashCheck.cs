using System.Security.Cryptography;

namespace PowerPlanSwitcher.Core;

/// <summary>
/// Checks a downloaded file against the text of a .sha256 release asset
/// ("<hex>  filename"). A mismatch or unreadable text is simply false.
/// </summary>
public static class HashCheck
{
    public static bool Verify(string? sha256AssetText, byte[] fileBytes)
    {
        if (string.IsNullOrWhiteSpace(sha256AssetText) || fileBytes.Length == 0)
        {
            return false;
        }

        string hex = sha256AssetText
            .Trim()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .ToLowerInvariant()
            ?? string.Empty;

        if (hex.Length != 64)
        {
            return false;
        }

        try
        {
            byte[] expected = Convert.FromHexString(hex);

            byte[] actual = SHA256.HashData(fileBytes);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            // Not hexadecimal; not a hash we can check against.
            return false;
        }
    }
}
