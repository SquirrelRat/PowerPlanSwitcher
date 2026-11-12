using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlanSwitcher.Core;

/// <summary>
/// Reads and writes the settings file. A broken file or a single wrong-typed
/// field degrades to defaults and is reported, never fatal.
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

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly string[] KnownRootFields =
    {
        "version",
        "rules",
        "idle",
        "startWithWindows",
        "checkForUpdates",
        "scanIntervalSeconds",
    };

    private static readonly string[] KnownRuleFields =
    {
        "acActive",
        "acIdle",
        "batteryActive",
        "batteryIdle",
        "active",
        "idle",
    };

    private static readonly string[] KnownIdleFields =
    {
        "inputTimeoutMinutes",
        "onScreensaver",
        "onLock",
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
        catch (JsonException ex)
        {
            // Only malformed JSON is quarantined. An IO error is not the file's fault.
            var defaults = UseDefaults(ex);
            TryRenameToBad(path);
            return defaults;
        }
        catch (Exception ex)
        {
            return UseDefaults(ex);
        }
    }

    private AppConfig UseDefaults(Exception ex)
    {
        ReportProblem($"Settings file could not be read, so defaults were used. Details: {ex.Message}");
        return AppConfig.CreateDefault();
    }

    public bool Save(AppConfig config)
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
            return false;
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

            return true;
        }
        catch (Exception ex)
        {
            ReportProblem($"Settings could not be saved. Details: {ex.Message}");
            TryDeleteTemp(tempPath);
            return false;
        }
    }

    private AppConfig FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The settings file must contain a JSON object.");
        }

        var config = AppConfig.CreateDefault();
        var problems = new List<string>();

        if (TryGet(root, "version", out JsonElement version) &&
            version.ValueKind != JsonValueKind.Null)
        {
            if (version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int value))
            {
                config.Version = value;
            }
            else
            {
                problems.Add("version");
            }
        }

        if (TryGet(root, "startWithWindows", out JsonElement start) &&
            start.ValueKind != JsonValueKind.Null)
        {
            if (start.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                config.StartWithWindows = start.GetBoolean();
            }
            else
            {
                problems.Add("startWithWindows");
            }
        }

        if (TryGet(root, "checkForUpdates", out JsonElement check) &&
            check.ValueKind != JsonValueKind.Null)
        {
            if (check.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                config.CheckForUpdates = check.GetBoolean();
            }
            else
            {
                problems.Add("checkForUpdates");
            }
        }

        if (TryGet(root, "scanIntervalSeconds", out JsonElement scan) &&
            scan.ValueKind != JsonValueKind.Null)
        {
            if (scan.ValueKind == JsonValueKind.Number && scan.TryGetInt32(out int seconds))
            {
                config.ScanIntervalSeconds = seconds;
            }
            else
            {
                problems.Add("scanIntervalSeconds");
            }
        }

        if (TryGet(root, "rules", out JsonElement rules) &&
            rules.ValueKind != JsonValueKind.Null)
        {
            if (rules.ValueKind == JsonValueKind.Object)
            {
                config.Rules = ParseRules(rules, problems);
            }
            else
            {
                problems.Add("rules");
            }
        }

        if (TryGet(root, "idle", out JsonElement idle) &&
            idle.ValueKind != JsonValueKind.Null)
        {
            if (idle.ValueKind == JsonValueKind.Object)
            {
                ParseIdle(idle, config.Idle, problems);
            }
            else
            {
                problems.Add("idle");
            }
        }

        config.ExtensionData = ReadUnknown(root, KnownRootFields);

        if (problems.Count > 0)
        {
            ReportProblem(
                "Some settings had the wrong type and were reset to defaults: " +
                string.Join(", ", problems) + ". The other settings were kept.");
        }

        if (config.Version > AppConfig.CurrentVersion)
        {
            ReportProblem(
                $"The settings file was written by a newer version (schema {config.Version}). " +
                "Known settings loaded; unknown fields are kept.");
        }

        return config;
    }

    private static PlanRules ParseRules(JsonElement rules, List<string> problems)
    {
        var result = new PlanRules();

        // Flat shape (what we write): acActive / acIdle / batteryActive / batteryIdle.
        TryParseGuidCell(rules, "acActive", "rules.acActive", problems, v => result.AcActive = v);
        TryParseGuidCell(rules, "acIdle", "rules.acIdle", problems, v => result.AcIdle = v);
        TryParseGuidCell(rules, "batteryActive", "rules.batteryActive", problems, v => result.BatteryActive = v);
        TryParseGuidCell(rules, "batteryIdle", "rules.batteryIdle", problems, v => result.BatteryIdle = v);

        // Nested shape: rules.active.ac / rules.active.battery and rules.idle.*.
        // Nested values win when a file contains both shapes.
        if (TryGet(rules, "active", out JsonElement active) && active.ValueKind == JsonValueKind.Object)
        {
            TryParseGuidCell(active, "ac", "rules.active.ac", problems, v => result.AcActive = v);
            TryParseGuidCell(active, "battery", "rules.active.battery", problems, v => result.BatteryActive = v);
        }

        if (TryGet(rules, "idle", out JsonElement idle) && idle.ValueKind == JsonValueKind.Object)
        {
            TryParseGuidCell(idle, "ac", "rules.idle.ac", problems, v => result.AcIdle = v);
            TryParseGuidCell(idle, "battery", "rules.idle.battery", problems, v => result.BatteryIdle = v);
        }

        result.ExtensionData = ReadUnknown(rules, KnownRuleFields);
        return result;
    }

    private static void ParseIdle(JsonElement idle, IdleSettings target, List<string> problems)
    {
        if (TryGet(idle, "inputTimeoutMinutes", out JsonElement timeout) &&
            timeout.ValueKind != JsonValueKind.Null)
        {
            if (timeout.ValueKind == JsonValueKind.Number && timeout.TryGetInt32(out int minutes))
            {
                target.InputTimeoutMinutes = minutes;
            }
            else
            {
                problems.Add("idle.inputTimeoutMinutes");
            }
        }

        if (TryGet(idle, "onScreensaver", out JsonElement screensaver) &&
            screensaver.ValueKind != JsonValueKind.Null)
        {
            if (screensaver.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                target.OnScreensaver = screensaver.GetBoolean();
            }
            else
            {
                problems.Add("idle.onScreensaver");
            }
        }

        if (TryGet(idle, "onLock", out JsonElement locked) &&
            locked.ValueKind != JsonValueKind.Null)
        {
            if (locked.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                target.OnLock = locked.GetBoolean();
            }
            else
            {
                problems.Add("idle.onLock");
            }
        }

        target.ExtensionData = ReadUnknown(idle, KnownIdleFields);
    }

    private static void TryParseGuidCell(
        JsonElement parent,
        string name,
        string problemName,
        List<string> problems,
        Action<Guid?> assign)
    {
        if (!TryGet(parent, name, out JsonElement cell))
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
                problems.Add(problemName);
            }

            return;
        }

        // A number or object where a GUID string belongs is a typo, not a plan.
        assign(null);
        problems.Add(problemName);
    }

    private static Dictionary<string, JsonElement>? ReadUnknown(
        JsonElement parent,
        params string[] known)
    {
        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement>? unknown = null;

        foreach (JsonProperty property in parent.EnumerateObject())
        {
            if (knownSet.Contains(property.Name))
            {
                continue;
            }

            unknown ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            unknown[property.Name] = property.Value.Clone();
        }

        return unknown;
    }

    private static bool TryGet(JsonElement parent, string name, out JsonElement value)
    {
        value = default;

        if (parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
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
