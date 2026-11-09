namespace PowerPlanSwitcher.Core;

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
}
