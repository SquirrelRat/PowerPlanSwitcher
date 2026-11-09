namespace PowerPlanSwitcher.Core;

/// <summary>Decides whether a snapshot means the user is idle, and why.</summary>
public sealed record IdleDecision(bool IsIdle, string Reason);

public static class IdleEvaluator
{
    /// <summary>
    /// Order of precedence: screensaver, then lock, then input timeout.
    /// The first enabled trigger that is true decides.
    /// </summary>
    public static IdleDecision Evaluate(SystemSnapshot snapshot, IdleSettings settings)
    {
        if (settings.OnScreensaver && snapshot.ScreensaverRunning)
        {
            return new IdleDecision(true, "screen saver running");
        }

        if (settings.OnLock && snapshot.WorkstationLocked)
        {
            return new IdleDecision(true, "workstation locked");
        }

        if (snapshot.InputIdleTime >= TimeSpan.FromMinutes(settings.InputTimeoutMinutes))
        {
            return new IdleDecision(true, $"no input for {settings.InputTimeoutMinutes} min");
        }

        return new IdleDecision(false, "user active");
    }
}
