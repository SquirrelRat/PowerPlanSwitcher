using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// The app's live state. Single threaded: only the PlanMonitor writes this, on the
/// UI thread, and the UI reads it on the same thread.
/// </summary>
public sealed class AppState
{
    public required AppConfig Config { get; set; }
    public required IReadOnlyList<PowerPlanInfo> Plans { get; set; }

    /// <summary>The plan the app forced, or null in Auto mode.</summary>
    public Guid? ForcedPlan { get; set; }

    /// <summary>Which tray role produced <see cref="ForcedPlan"/>, or null in Auto mode.</summary>
    public PlanRole? ForcedRole { get; set; }

    /// <summary>The plan the OS reports as active right now.</summary>
    public PowerPlanInfo? ActivePlan { get; set; }

    /// <summary>What the last poll decided.</summary>
    public PlanTarget? Target { get; set; }

    public DateTimeOffset LastChanged { get; set; } = DateTimeOffset.Now;

    /// <summary>The last problem worth showing the user, or null.</summary>
    public string? LastProblem { get; set; }

    /// <summary>What kind of problem <see cref="LastProblem"/> is, if any. Tracked separately so rewording the text cannot pin a stale error.</summary>
    public ProblemKind? LastProblemKind { get; set; }

    public event Action? Changed;

    public void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // A UI listener must never break the monitor loop.
        }
    }

    public PowerPlanInfo? FindPlan(Guid? guid) =>
        guid is null ? null : Plans.FirstOrDefault(p => p.Guid == guid.Value);

    /// <summary>Clears a switch-related problem, leaving other problems alone.</summary>
    public void ClearSwitchProblemOnly()
    {
        if (LastProblemKind is not null)
        {
            LastProblem = null;
            LastProblemKind = null;
        }
    }

    /// <summary>Joins the fields the UI shows, so the monitor can skip no-op refreshes.</summary>
    public string GetChangeSignature() => string.Join(
        "|",
        Plans.Count,
        string.Join(",", Plans.Select(p => p.Guid)),
        ActivePlan?.Guid.ToString() ?? "none",
        Target?.PlanGuid?.ToString() ?? "unset",
        Target?.Activity.ToString() ?? "?",
        Target?.PowerSource.ToString() ?? "?",
        Target?.Reason ?? "",
        Target?.Forced.ToString() ?? "",
        ForcedPlan?.ToString() ?? "auto",
        ForcedRole?.ToString() ?? "",
        LastProblem ?? "",
        LastProblemKind?.ToString() ?? "");
}

/// <summary>Why <see cref="AppState.LastProblem"/> is set. Kept separate from the text so message edits cannot stick a stale error.</summary>
public enum ProblemKind
{
    TransientCheck,
    TargetMissing,
    SwitchFailed,
}
