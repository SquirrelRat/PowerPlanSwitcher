using PowerPlanSwitcher.Platform;

namespace PowerPlanSwitcher.Services;

/// <summary>Everything the app does to power plans.</summary>
public interface IPowerPlanService
{
    /// <summary>Every plan, or null when the OS call failed.</summary>
    IReadOnlyList<PowerPlanInfo>? ListPlans();

    /// <summary>The active plan, or null when the OS call failed.</summary>
    PowerPlanInfo? GetActivePlan();

    /// <summary>Applies a plan and confirms the result.</summary>
    bool TryApply(Guid planGuid);
}
