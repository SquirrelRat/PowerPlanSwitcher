namespace PowerPlanSwitcher.Core;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// When the machine should be considered idle.
/// </summary>
public sealed class IdleSettings
{
    public const int MinimumTimeoutMinutes = 1;
    public const int MaximumTimeoutMinutes = 120;

    private int _inputTimeoutMinutes = 5;

    /// <summary>Minutes of no input before the machine goes idle. Clamped to 1-120.</summary>
    public int InputTimeoutMinutes
    {
        get => _inputTimeoutMinutes;
        set => _inputTimeoutMinutes = Math.Clamp(
            value,
            MinimumTimeoutMinutes,
            MaximumTimeoutMinutes);
    }

    /// <summary>Count a running screensaver as idle, immediately.</summary>
    public bool OnScreensaver { get; set; } = true;

    /// <summary>Count a locked workstation as idle, immediately.</summary>
    public bool OnLock { get; set; } = true;

    /// <summary>
    /// Fields from newer versions that this version does not understand.
    /// Carried through untouched so a downgrade never destroys them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
