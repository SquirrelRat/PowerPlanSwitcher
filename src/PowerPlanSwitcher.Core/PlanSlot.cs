namespace PowerPlanSwitcher.Core;

/// <summary>The four cells of the rules grid.</summary>
public enum PlanSlot
{
    AcActive,
    AcIdle,
    BatteryActive,
    BatteryIdle,
}

/// <summary>Maps an activity and power source to exactly one grid cell.</summary>
public static class PlanSlotExtensions
{
    public static PlanSlot For(Activity activity, PowerSource powerSource) => (activity, powerSource) switch
    {
        (Activity.Active, PowerSource.Ac) => PlanSlot.AcActive,
        (Activity.Active, PowerSource.Battery) => PlanSlot.BatteryActive,
        (Activity.Idle, PowerSource.Ac) => PlanSlot.AcIdle,
        (Activity.Idle, PowerSource.Battery) => PlanSlot.BatteryIdle,
        _ => throw new ArgumentOutOfRangeException(
            nameof(activity),
            $"Unknown activity/power source pair: {activity}/{powerSource}"),
    };
}
