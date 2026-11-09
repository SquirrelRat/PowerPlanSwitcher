namespace PowerPlanSwitcher.Core;

/// <summary>Where the machine is currently drawing power from.</summary>
public enum PowerSource
{
    Ac,
    Battery,

    /// <summary>The OS could not say. Leave the active plan alone.</summary>
    Unknown,
}
