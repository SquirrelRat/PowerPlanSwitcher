namespace PowerPlanSwitcher.Core;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Everything the app persists between runs.</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public const int DefaultScanIntervalSeconds = 2;
    public const int MinScanIntervalSeconds = 1;
    public const int MaxScanIntervalSeconds = 60;

    private bool _startWithWindows = true;
    private int _scanIntervalSeconds = DefaultScanIntervalSeconds;

    /// <summary>Bumped when the on-disk shape changes in a breaking way.</summary>
    public int Version { get; set; } = CurrentVersion;

    public PlanRules Rules { get; set; } = new();

    public IdleSettings Idle { get; set; } = new();

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => _startWithWindows = value;
    }

    /// <summary>
    /// How often the app scans for changes, in seconds. Clamped to 1-60.
    /// </summary>
    public int ScanIntervalSeconds
    {
        get => _scanIntervalSeconds;
        set => _scanIntervalSeconds = Math.Clamp(
            value,
            MinScanIntervalSeconds,
            MaxScanIntervalSeconds);
    }

    /// <summary>The config the app uses on a first run or a broken file.</summary>
    public static AppConfig CreateDefault() => new();

    /// <summary>
    /// Fields from newer versions that this version does not understand.
    /// Carried through untouched so a downgrade never destroys them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
