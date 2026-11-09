using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using System.Windows.Threading;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Runs the app loop: watch the machine, decide, apply. A single
/// DispatcherTimer on the UI thread means no cross-thread state at all.
/// </summary>
public sealed class PlanMonitor : IPlanMonitor, IDisposable
{
    private readonly ISystemProbe _probe;
    private readonly AppState _state;
    private readonly IPowerPlanService _plans;
    private readonly Func<DateTimeOffset> _now;
    private readonly DispatcherTimer _timer;
    private string? _lastNotifiedSignature;
    private string? _loggedProblemKey;

    public PlanMonitor(
        ISystemProbe probe,
        AppState state,
        IPowerPlanService plans,
        Func<DateTimeOffset>? now = null)
    {
        _probe = probe;
        _state = state;
        _plans = plans;
        _now = now ?? (() => DateTimeOffset.Now);

        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(_state.Config.ScanIntervalSeconds),
            DispatcherPriority.Background,
            OnTick,
            Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    /// <summary>Runs a pass right now, for a change made in the UI.</summary>
    public void RunNow()
    {
        try
        {
            RunPass();
        }
        catch (Exception ex)
        {
            // UI-triggered passes are not covered by the timer's guard.
            SetProblem(
                ProblemKind.TransientCheck,
                "A power check failed unexpectedly; will retry automatically.");
            AppLog.Error("run-now failed: " + ex);
            NotifyIfChanged();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            RunPass();
        }
        catch (Exception ex)
        {
            SetProblem(
                ProblemKind.TransientCheck,
                "A power check failed unexpectedly; will retry automatically.");
            AppLog.Error("poll failed: " + ex);
            NotifyIfChanged();
        }
    }

    /// <summary>
    /// One pass: refresh plans, decide what should be active, apply it.
    /// Called only from guarded entry points; still never lets timer state break.
    /// </summary>
    private void RunPass()
    {
        // The scan timer is user-settable; follow it without a restart.
        try
        {
            _timer.Interval = TimeSpan.FromSeconds(_state.Config.ScanIntervalSeconds);
        }
        catch (Exception)
        {
        }

        DateTimeOffset now = _now();
        _state.LastChecked = now;

        if (_state.Paused)
        {
            if (_state.IsPauseExpired(now))
            {
                _state.Resume();
                AppLog.Info("pause ended");
            }
            else
            {
                NotifyIfChanged();
                return;
            }
        }

        IReadOnlyList<PowerPlanInfo>? plans = _plans.ListPlans();
        if (plans is null)
        {
            SetProblem(
                ProblemKind.TransientCheck,
                "Could not read the power plans. Will retry.");
            LogProblemOnce("plans", "ERROR", "listing power plans failed");
            NotifyIfChanged();
            return;
        }

        _state.Plans = plans;

        PowerPlanInfo? active = _plans.GetActivePlan();
        if (active is null)
        {
            SetProblem(
                ProblemKind.TransientCheck,
                "Could not read the active power plan. Will retry.");
            LogProblemOnce("active", "ERROR", "reading the active power plan failed");
            NotifyIfChanged();
            return;
        }

        _state.ActivePlan = active;
        _state.HasBattery = _probe.HasBattery();

        var snapshot = new SystemSnapshot(
            _probe.GetInputIdleTime(),
            _probe.IsScreensaverRunning(),
            _probe.IsWorkstationLocked(),
            _probe.GetPowerSource());

        var target = RulesEngine.Resolve(
            snapshot,
            _state.Config.Idle,
            _state.Config.Rules,
            _state.ForcedPlan);

        _state.Target = target;

        if (target.PlanGuid is null)
        {
            if (target.PowerSource == PowerSource.Unknown)
            {
                SetProblem(
                    ProblemKind.PowerSourceUnknown,
                    "Windows could not report the power source, so the plan was left alone.");
                LogProblemOnce("source", "WARN", "power source unknown, leaving the plan alone");
            }
            else
            {
                // An unset cell means "leave the plan alone", and it clears any
                // switch problem caused by the cell that was set before.
                _state.ClearSwitchProblemOnly();
                ClearProblemLog();
            }

            NotifyIfChanged();
            return;
        }

        if (_state.FindPlan(target.PlanGuid) is null)
        {
            // That plan no longer exists on this machine: treat it as an unset
            // cell instead of failing a switch every two seconds.
            SetProblem(
                ProblemKind.TargetMissing,
                "The planned power plan is no longer on this PC, so nothing was switched.");
            LogProblemOnce(
                "missing:" + target.PlanGuid,
                "WARN",
                "target plan missing, leaving the active plan alone: " + target.PlanGuid);
            NotifyIfChanged();
            return;
        }

        if (_state.ActivePlan?.Guid == target.PlanGuid.Value)
        {
            _state.ClearSwitchProblemOnly();
            ClearProblemLog();
            NotifyIfChanged();
            return;
        }

        if (_plans.TryApply(target.PlanGuid.Value))
        {
            _state.ActivePlan = _state.FindPlan(target.PlanGuid);
            _state.LastChanged = now;
            _state.AddHistory(new SwitchRecord(
                now,
                _state.ActivePlan?.Name ?? target.PlanGuid.Value.ToString(),
                target.Activity,
                target.PowerSource,
                target.Reason,
                target.Forced));
            _state.ClearSwitchProblemOnly();
            ClearProblemLog();
            AppLog.Info(
                $"switched to {_state.ActivePlan?.Name ?? target.PlanGuid.Value.ToString()} " +
                $"({target.Activity}, {target.PowerSource}, {target.Reason})");
        }
        else
        {
            SetProblem(
                ProblemKind.SwitchFailed,
                $"Could not switch to {_state.FindPlan(target.PlanGuid)?.Name ?? target.PlanGuid.Value.ToString()}.");
            LogProblemOnce(
                "switch:" + target.PlanGuid,
                "ERROR",
                "switch failed for plan " + target.PlanGuid);
        }

        NotifyIfChanged();
    }

    private void SetProblem(ProblemKind kind, string message)
    {
        _state.LastProblem = message;
        _state.LastProblemKind = kind;
    }

    private void LogProblemOnce(string key, string level, string message)
    {
        if (string.Equals(_loggedProblemKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _loggedProblemKey = key;

        if (level == "ERROR")
        {
            AppLog.Error(message);
        }
        else
        {
            AppLog.Warn(message);
        }
    }

    private void ClearProblemLog() => _loggedProblemKey = null;

    /// <summary>
    /// Notifies the UI only when something worth showing changed, so the tray
    /// and the settings window are not rebuilt on every 2-second poll.
    /// </summary>
    private void NotifyIfChanged()
    {
        string signature = _state.GetChangeSignature();

        if (signature == _lastNotifiedSignature)
        {
            return;
        }

        _lastNotifiedSignature = signature;
        _state.NotifyChanged();
    }

    public void Dispose()
    {
        try
        {
            _timer.Stop();
        }
        catch (Exception)
        {
        }
    }
}
