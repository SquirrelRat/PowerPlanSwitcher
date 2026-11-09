using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Everything the app does to power plans: list, read, and apply.
/// Applying always verifies the result.
/// </summary>
public sealed class PowerPlanService : IPowerPlanService
{
    public IReadOnlyList<PowerPlanInfo>? ListPlans()
    {
        try
        {
            return PowerPlanApi.ListPlans();
        }
        catch (Exception ex)
        {
            AppLog.Error("listing power plans failed: " + ex);
            return null;
        }
    }

    public PowerPlanInfo? GetActivePlan()
    {
        try
        {
            return PowerPlanApi.GetActivePlan();
        }
        catch (Exception ex)
        {
            AppLog.Error("reading active power plan failed: " + ex);
            return null;
        }
    }

    /// <summary>Applies a plan and confirms it took effect.</summary>
    public bool TryApply(Guid planGuid)
    {
        try
        {
            return PowerPlanApi.TrySetActivePlan(planGuid);
        }
        catch (Exception ex)
        {
            AppLog.Error("applying power plan failed: " + ex);
            return false;
        }
    }
}
