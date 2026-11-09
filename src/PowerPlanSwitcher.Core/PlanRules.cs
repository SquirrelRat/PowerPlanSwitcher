namespace PowerPlanSwitcher.Core;

using System.Text.Json.Serialization;

/// <summary>
/// Maps each activity and power-source combination to a power plan GUID.
/// A null cell means "leave the plan alone in that situation".
/// </summary>
public sealed class PlanRules
{
    public Guid? AcActive { get; set; }
    public Guid? AcIdle { get; set; }
    public Guid? BatteryActive { get; set; }
    public Guid? BatteryIdle { get; set; }

    public PlanRules()
    {
        AcActive = WellKnownPlans.Balanced;
        AcIdle = WellKnownPlans.PowerSaver;
        BatteryActive = WellKnownPlans.PowerSaver;
        BatteryIdle = WellKnownPlans.PowerSaver;
    }

    /// <summary>Gets the plan for a situation, or null when unset.</summary>
    public Guid? Get(Activity activity, PowerSource powerSource) =>
        Normalize(SlotIndex(activity, powerSource) switch
        {
            0 => AcActive,
            1 => BatteryActive,
            2 => AcIdle,
            3 => BatteryIdle,
            _ => throw new ArgumentOutOfRangeException(
                nameof(activity),
                $"Unknown activity/power source pair: {activity}/{powerSource}"),
        });

    /// <summary>Sets the plan for a situation. Pass null to unset it.</summary>
    public void Set(Activity activity, PowerSource powerSource, Guid? planGuid)
    {
        planGuid = Normalize(planGuid);

        switch (SlotIndex(activity, powerSource))
        {
            case 0:
                AcActive = planGuid;
                break;
            case 1:
                BatteryActive = planGuid;
                break;
            case 2:
                AcIdle = planGuid;
                break;
            case 3:
                BatteryIdle = planGuid;
                break;
        }
    }

    private static Guid? Normalize(Guid? value) => value == Guid.Empty ? null : value;

    private static int SlotIndex(Activity activity, PowerSource powerSource) => (activity, powerSource) switch
    {
        (Activity.Active, PowerSource.Ac) => 0,
        (Activity.Active, PowerSource.Battery) => 1,
        (Activity.Idle, PowerSource.Ac) => 2,
        (Activity.Idle, PowerSource.Battery) => 3,
        _ => throw new ArgumentOutOfRangeException(
            nameof(activity),
            $"Unknown activity/power source pair: {activity}/{powerSource}"),
    };

    /// <summary>Every cell that has no plan, in a stable order.</summary>
    [JsonIgnore]
    public IReadOnlyList<PlanSlot> UnsetSlots
    {
        get
        {
            var slots = new List<PlanSlot>(4);
            if (Normalize(AcActive) is null) slots.Add(PlanSlot.AcActive);
            if (Normalize(AcIdle) is null) slots.Add(PlanSlot.AcIdle);
            if (Normalize(BatteryActive) is null) slots.Add(PlanSlot.BatteryActive);
            if (Normalize(BatteryIdle) is null) slots.Add(PlanSlot.BatteryIdle);
            return slots;
        }
    }
}
