using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlanSwitcher.Core;

/// <summary>
/// Reads and writes the settings file. Never throws: a broken file
/// degrades to defaults and is reported, not fatal.
/// </summary>
public sealed class ConfigStore
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _directory;

    public ConfigStore(string directory)
    {
        _directory = directory;
    }

    /// <summary>Full path of the settings file.</summary>
    public string SettingsPath => Path.Combine(_directory, FileName);

    /// <summary>Raised with a user-facing message when something goes wrong.</summary>
    public event Action<string>? ProblemReported;

    public AppConfig Load()
    {
        string path;
        try
        {
            path = SettingsPath;
        }
        catch (Exception ex)
        {
            return UseDefaults(ex);
        }

        bool exists;
        try
        {
            exists = File.Exists(path);
        }
        catch (Exception ex)
        {
            return UseDefaults(ex);
        }

        if (!exists)
        {
            var defaults = AppConfig.CreateDefault();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            return FromJson(json);
        }
        catch (Exception ex)
        {
            var defaults = UseDefaults(ex);
            TryRenameToBad(path);
            return defaults;
        }
    }

    private AppConfig UseDefaults(Exception ex)
    {
        ReportProblem($"Settings file could not be read, so defaults were used. Details: {ex.Message}");
        return AppConfig.CreateDefault();
    }

    public void Save(AppConfig config)
    {
        string path;
        string tempPath;
        try
        {
            path = SettingsPath;
            tempPath = path + ".tmp";
        }
        catch (Exception ex)
        {
            ReportProblem($"Settings could not be saved. Details: {ex.Message}");
            return;
        }

        try
        {
            Directory.CreateDirectory(_directory);

            File.WriteAllText(tempPath, ToJson(config));

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch (Exception ex)
        {
            ReportProblem($"Settings could not be saved. Details: {ex.Message}");
            TryDeleteTemp(tempPath);
        }
    }

    internal static AppConfig FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<AppConfigDto>(json, SerializerOptions)
            ?? new AppConfigDto();

        // Every field falls back to its own default: one null or missing
        // field must never reset the unrelated settings.
        var config = AppConfig.CreateDefault();

        if (dto.StartWithWindows.HasValue)
        {
            config.StartWithWindows = dto.StartWithWindows.Value;
        }

        if (dto.ScanIntervalSeconds.HasValue)
        {
            config.ScanIntervalSeconds = dto.ScanIntervalSeconds.Value;
        }

        if (dto.Rules is not null && dto.Rules.Value.ValueKind != JsonValueKind.Null)
        {
            config.Rules = ParseRules(dto.Rules.Value, config.Rules);
        }

        if (dto.Idle is not null)
        {
            if (dto.Idle.InputTimeoutMinutes.HasValue)
            {
                config.Idle.InputTimeoutMinutes = dto.Idle.InputTimeoutMinutes.Value;
            }

            if (dto.Idle.OnScreensaver.HasValue)
            {
                config.Idle.OnScreensaver = dto.Idle.OnScreensaver.Value;
            }

            if (dto.Idle.OnLock.HasValue)
            {
                config.Idle.OnLock = dto.Idle.OnLock.Value;
            }
        }

        // Preserve the on-disk version as a migration signal; default when absent.
        config.Version = dto.Version ?? AppConfig.CurrentVersion;
        config.ExtensionData = dto.ExtensionData;
        return config;
    }

    private static PlanRules ParseRules(JsonElement rulesElement, PlanRules defaults)
    {
        if (rulesElement.ValueKind != JsonValueKind.Object)
        {
            return defaults;
        }

        var result = new PlanRules
        {
            AcActive = defaults.AcActive,
            AcIdle = defaults.AcIdle,
            BatteryActive = defaults.BatteryActive,
            BatteryIdle = defaults.BatteryIdle,
        };

        // Flat shape (what we write): acActive / acIdle / batteryActive / batteryIdle.
        TryParseGuidCell(rulesElement, "acActive", v => result.AcActive = v);
        TryParseGuidCell(rulesElement, "acIdle", v => result.AcIdle = v);
        TryParseGuidCell(rulesElement, "batteryActive", v => result.BatteryActive = v);
        TryParseGuidCell(rulesElement, "batteryIdle", v => result.BatteryIdle = v);

        // Nested spec shape (§9): rules.active.ac, rules.active.battery, rules.idle.ac, rules.idle.battery.
        if (TryGetObject(rulesElement, "active", out JsonElement active))
        {
            TryParseGuidCell(active, "ac", v => result.AcActive = v);
            TryParseGuidCell(active, "battery", v => result.BatteryActive = v);
        }

        if (TryGetObject(rulesElement, "idle", out JsonElement idle))
        {
            TryParseGuidCell(idle, "ac", v => result.AcIdle = v);
            TryParseGuidCell(idle, "battery", v => result.BatteryIdle = v);
        }

        return result;

        static void TryParseGuidCell(
            JsonElement obj,
            string name,
            Action<Guid?> assign)
        {
            if (!TryGetPropertyCaseInsensitive(obj, name, out JsonElement cell))
            {
                return;
            }

            if (cell.ValueKind == JsonValueKind.Null || cell.ValueKind == JsonValueKind.Undefined)
            {
                assign(null);
                return;
            }

            if (cell.ValueKind == JsonValueKind.String)
            {
                string? text = cell.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    assign(null);
                    return;
                }

                if (Guid.TryParse(text, out Guid parsed) && parsed != Guid.Empty)
                {
                    assign(parsed);
                }
                else
                {
                    // One bad cell degrades to unset, never to a whole-file reset.
                    assign(null);
                }

                return;
            }

            // Unexpected kind: leave the default for this cell.
        }
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement child)
    {
        child = default;
        if (!TryGetPropertyCaseInsensitive(parent, name, out JsonElement found))
        {
            return false;
        }

        if (found.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        child = found;
        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty prop in parent.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deserialization shape with every scalar nullable, so a single null
    /// field degrades to its default instead of failing the whole file.
    /// Rules stays a raw element so one bad GUID cell degrades to unset
    /// instead of failing the whole file, and both the flat shape we write
    /// (acActive) and the nested spec shape (§9: active.ac) are accepted.
    /// </summary>
    private sealed class AppConfigDto
    {
        public int? Version { get; set; }
        public JsonElement? Rules { get; set; }
        public IdleSettingsDto? Idle { get; set; }
        public bool? StartWithWindows { get; set; }
        public int? ScanIntervalSeconds { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class IdleSettingsDto
    {
        public int? InputTimeoutMinutes { get; set; }
        public bool? OnScreensaver { get; set; }
        public bool? OnLock { get; set; }
    }

    internal static string ToJson(AppConfig config) =>
        JsonSerializer.Serialize(config, SerializerOptions);

    private void TryRenameToBad(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception)
        {
            // nothing more we can do, defaults are already in use
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception)
        {
            // best effort: never let a stray tmp escape as a throw
        }
    }

    private void ReportProblem(string message)
    {
        try
        {
            ProblemReported?.Invoke(message);
        }
        catch (Exception)
        {
            // A problem listener must never take the app down.
        }
    }
}
