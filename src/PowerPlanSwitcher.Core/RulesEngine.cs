namespace PowerPlanSwitcher.Core;

/// <summary>
/// What the switcher should do right now.
/// </summary>
public sealed record PlanTarget(
    Guid? PlanGuid,
    Activity Activity,
    PowerSource PowerSource,
    string Reason,
    bool Forced);

/// <summary>
/// Turns a snapshot into a target plan. A forced plan wins over the rules.
/// </summary>
public static class RulesEngine
{
    /// <summary>
    /// <paramref name="forcedPlanGuid"/> is null in Auto mode, otherwise the plan
    /// the user forced from the tray.
    /// </summary>
    public static PlanTarget Resolve(
        SystemSnapshot snapshot,
        IdleSettings idleSettings,
        PlanRules rules,
        Guid? forcedPlanGuid)
    {
        var decision = IdleEvaluator.Evaluate(snapshot, idleSettings);
        var activity = decision.IsIdle ? Activity.Idle : Activity.Active;

        // Empty means Auto, same as null.
        if (forcedPlanGuid == Guid.Empty)
        {
            forcedPlanGuid = null;
        }

        if (forcedPlanGuid is not null)
        {
            return new PlanTarget(
                forcedPlanGuid,
                activity,
                snapshot.PowerSource,
                decision.Reason,
                Forced: true);
        }

        if (snapshot.PowerSource == PowerSource.Unknown)
        {
            return new PlanTarget(
                null,
                activity,
                PowerSource.Unknown,
                "power source unknown",
                Forced: false);
        }

        return new PlanTarget(
            rules.Get(activity, snapshot.PowerSource),
            activity,
            snapshot.PowerSource,
            decision.Reason,
            Forced: false);
    }
}
