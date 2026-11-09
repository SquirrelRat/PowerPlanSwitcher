namespace PowerPlanSwitcher.Core;

using System.Text.Json;
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

    /// <summary>
    /// Fields from newer versions that this version does not understand.
    /// Carried through untouched so a downgrade never destroys them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public PlanRules()
    {
        AcActive = WellKnownPlans.Balanced;
        AcIdle = WellKnownPlans.PowerSaver;
        BatteryActive = WellKnownPlans.PowerSaver;
        BatteryIdle = WellKnownPlans.PowerSaver;
    }

    /// <summary>Gets the plan for a situation, or null when unset.</summary>
    public Guid? Get(Activity activity, PowerSource powerSource) =>
        Get(PlanSlotExtensions.For(activity, powerSource));

    /// <summary>Sets the plan for a situation. Pass null to unset it.</summary>
    public void Set(Activity activity, PowerSource powerSource, Guid? planGuid) =>
        Set(PlanSlotExtensions.For(activity, powerSource), planGuid);

    /// <summary>Gets one cell by slot.</summary>
    public Guid? Get(PlanSlot slot) => Normalize(slot switch
    {
        PlanSlot.AcActive => AcActive,
        PlanSlot.AcIdle => AcIdle,
        PlanSlot.BatteryActive => BatteryActive,
        PlanSlot.BatteryIdle => BatteryIdle,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    });

    /// <summary>Sets one cell by slot. Pass null to unset it.</summary>
    public void Set(PlanSlot slot, Guid? planGuid)
    {
        planGuid = Normalize(planGuid);

        switch (slot)
        {
            case PlanSlot.AcActive:
                AcActive = planGuid;
                break;
            case PlanSlot.AcIdle:
                AcIdle = planGuid;
                break;
            case PlanSlot.BatteryActive:
                BatteryActive = planGuid;
                break;
            case PlanSlot.BatteryIdle:
                BatteryIdle = planGuid;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    private static Guid? Normalize(Guid? value) => value == Guid.Empty ? null : value;

    /// <summary>Every cell that has no plan, in a stable order.</summary>
    [JsonIgnore]
    public IReadOnlyList<PlanSlot> UnsetSlots
    {
        get
        {
            var slots = new List<PlanSlot>(4);

            foreach (PlanSlot slot in Enum.GetValues<PlanSlot>())
            {
                if (Get(slot) is null)
                {
                    slots.Add(slot);
                }
            }

            return slots;
        }
    }
}
