using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace PowerPlanSwitcher.Core;

/// <summary>
/// Parses release version tags. A tag is "v" plus three numbers, such as
/// v0.3.1; a suffix after the third number ("-beta") is ignored for
/// comparison, and anything else is not a version.
/// </summary>
public static partial class UpdateVersion
{
    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex TagPattern();

    public static bool TryParse(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Match match = TagPattern().Match(text.Trim());

        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out int major)
            || !int.TryParse(match.Groups[2].Value, out int minor)
            || !int.TryParse(match.Groups[3].Value, out int build))
        {
            return false;
        }

        try
        {
            version = new Version(major, minor, build);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            // A component beyond int range is not a version we can act on.
            return false;
        }
    }

    /// <summary>True when the tag is a version strictly newer than the running one.</summary>
    public static bool IsNewer(string? tag, string currentVersionText)
    {
        if (!TryParse(tag, out Version? candidate)
            || !TryParse(currentVersionText, out Version? current))
        {
            return false;
        }

        return candidate > current;
    }
}
