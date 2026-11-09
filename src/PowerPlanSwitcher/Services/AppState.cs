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

    /// <summary>The plan the OS reports as active right now.</summary>
    public PowerPlanInfo? ActivePlan { get; set; }

    /// <summary>What the last poll decided.</summary>
    public PlanTarget? Target { get; set; }

    public DateTimeOffset LastChanged { get; set; } = DateTimeOffset.Now;

    /// <summary>When the last poll finished, successful or not.</summary>
    public DateTimeOffset LastChecked { get; set; }

    public bool HasBattery { get; set; } = true;

    /// <summary>True while the user has paused switching for this session.</summary>
    public bool Paused { get; private set; }

    /// <summary>Null with <see cref="Paused"/> means "until restart".</summary>
    public DateTimeOffset? PausedUntil { get; private set; }

    /// <summary>The last problem worth showing the user, or null.</summary>
    public string? LastProblem { get; set; }

    /// <summary>What kind of problem <see cref="LastProblem"/> is, if any.</summary>
    public ProblemKind? LastProblemKind { get; set; }

    private readonly List<SwitchRecord> _history = new();

    public IReadOnlyList<SwitchRecord> History => _history;

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

    public void Pause(TimeSpan? duration)
    {
        if (duration is not null && duration.Value <= TimeSpan.Zero)
        {
            Resume();
            return;
        }

        Paused = true;
        PausedUntil = duration is null ? null : DateTimeOffset.Now.Add(duration.Value);
    }

    public void Resume()
    {
        Paused = false;
        PausedUntil = null;
    }

    public bool IsPauseExpired(DateTimeOffset now) =>
        Paused && PausedUntil is not null && PausedUntil.Value <= now;

    public void AddHistory(SwitchRecord record)
    {
        _history.Add(record);

        if (_history.Count > 20)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>Clears a switch-related problem, leaving settings and load problems alone.</summary>
    public void ClearSwitchProblemOnly()
    {
        if (LastProblemKind is ProblemKind.TransientCheck
            or ProblemKind.TargetMissing
            or ProblemKind.SwitchFailed
            or ProblemKind.PowerSourceUnknown)
        {
            LastProblem = null;
            LastProblemKind = null;
        }
    }

    /// <summary>Joins the fields the UI shows, so the monitor can skip no-op refreshes.</summary>
    public string GetChangeSignature() => string.Join(
        "|",
        Plans.Count,
        string.Join(",", Plans.Select(p => p.Guid + ":" + p.Name)),
        ActivePlan?.Guid.ToString() ?? "none",
        ActivePlan?.Name ?? "",
        Target?.PlanGuid?.ToString() ?? "unset",
        Target?.Activity.ToString() ?? "?",
        Target?.PowerSource.ToString() ?? "?",
        Target?.Reason ?? "",
        Target?.Forced.ToString() ?? "",
        ForcedPlan?.ToString() ?? "auto",
        Paused,
        PausedUntil?.ToString() ?? "",
        HasBattery,
        LastProblem ?? "",
        LastProblemKind?.ToString() ?? "");
}

/// <summary>One successful switch, kept for the recent-changes list.</summary>
public sealed record SwitchRecord(
    DateTimeOffset At,
    string PlanName,
    Activity Activity,
    PowerSource PowerSource,
    string Reason,
    bool Forced)
{
    public string Summary =>
        $"{At:HH:mm}  {PlanName}  {Activity.ToString().ToLowerInvariant()}, " +
        $"{PowerSource.ToString().ToLowerInvariant()}";
}

/// <summary>Why <see cref="AppState.LastProblem"/> is set.</summary>
public enum ProblemKind
{
    TransientCheck,
    TargetMissing,
    SwitchFailed,
    PowerSourceUnknown,
    ConfigLoad,
    SettingsWriteFailed,
}
