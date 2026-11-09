namespace PowerPlanSwitcher.Core;

/// <summary>
/// The well-known Windows power plan GUIDs. Plans are always identified by
/// GUID, never by localised name.
/// </summary>
public static class WellKnownPlans
{
    /// <summary>Balanced: 381b4222-f694-41f0-9685-ff5bb260df2e.</summary>
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    /// <summary>Power saver: a1841308-3541-4fab-bc81-f71556f20b4a.</summary>
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    /// <summary>High performance: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c.</summary>
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
}
