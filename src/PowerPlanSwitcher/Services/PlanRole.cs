namespace PowerPlanSwitcher.Services;

/// <summary>
/// The three roles the tray offers, resolved to real plans at click time.
/// </summary>
public enum PlanRole
{
    Idle,
    Balanced,
    Boost,
}
