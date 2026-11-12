using System.Text.Json;

namespace PowerPlanSwitcher.Core;

/// <summary>The files one release must carry for the updater to use it.</summary>
public sealed record ReleaseAssets(
    string Tag,
    string ExeUrl,
    string HashUrl,
    string ReleasePageUrl);

/// <summary>
/// Reads a GitHub release payload. A release without both the exe and the
/// sha256 asset, or a payload that is not JSON, returns null; never throws.
/// </summary>
public static class ReleaseFeed
{
    public const string ExeAssetName = "PowerPlanSwitcher.exe";
    public const string HashAssetName = "PowerPlanSwitcher.exe.sha256";

    public static ReleaseAssets? Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? tag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                && tagElement.ValueKind == JsonValueKind.String
                ? tagElement.GetString()
                : null;

            string? pageUrl = root.TryGetProperty("html_url", out JsonElement pageElement)
                && pageElement.ValueKind == JsonValueKind.String
                ? pageElement.GetString()
                : null;

            string? exeUrl = null;
            string? hashUrl = null;

            if (root.TryGetProperty("assets", out JsonElement assets)
                && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    if (asset.ValueKind != JsonValueKind.Object
                        || !asset.TryGetProperty("name", out JsonElement nameElement)
                        || nameElement.ValueKind != JsonValueKind.String
                        || !asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                        || urlElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? name = nameElement.GetString();
                    string? url = urlElement.GetString();

                    if (name == ExeAssetName && !string.IsNullOrEmpty(url))
                    {
                        exeUrl = url;
                    }
                    else if (name == HashAssetName && !string.IsNullOrEmpty(url))
                    {
                        hashUrl = url;
                    }
                }
            }

            if (string.IsNullOrEmpty(tag)
                || string.IsNullOrEmpty(pageUrl)
                || string.IsNullOrEmpty(exeUrl)
                || string.IsNullOrEmpty(hashUrl))
            {
                return null;
            }

            return new ReleaseAssets(tag, exeUrl, hashUrl, pageUrl);
        }
        catch (Exception)
        {
            // JsonException for malformed JSON; anything else is the same
            // answer: this payload is not usable.
            return null;
        }
    }
}
