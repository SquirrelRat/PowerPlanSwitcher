namespace PowerPlanSwitcher.Core;

/// <summary>
/// An immutable picture of everything the rules need, captured at one moment.
/// </summary>
public sealed record SystemSnapshot(
    TimeSpan InputIdleTime,
    bool ScreensaverRunning,
    bool WorkstationLocked,
    PowerSource PowerSource);
