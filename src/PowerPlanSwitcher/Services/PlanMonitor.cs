using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using System.Windows.Threading;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Runs the app loop: watch the machine, decide, apply. A single
/// DispatcherTimer on the UI thread means no cross-thread state at all.
/// </summary>
public sealed class PlanMonitor : IDisposable
{
    private readonly SystemProbe _probe;
    private readonly AppState _state;
    private readonly PowerPlanService _plans;
    private readonly DispatcherTimer _timer;
    private string? _lastNotifiedSignature;

    public PlanMonitor(SystemProbe probe, AppState state, PowerPlanService plans)
    {
        _probe = probe;
        _state = state;
        _plans = plans;

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
            _state.LastProblem = "A power check failed unexpectedly; will retry automatically.";
            _state.LastProblemKind = ProblemKind.TransientCheck;
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
            AppLog.Error("poll failed: " + ex);
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

        _state.Plans = _plans.ListPlans();
        _state.ActivePlan = _plans.GetActivePlan();

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
            // An unset cell means "leave the plan alone", so there is nothing to do.
            // Do not clear LastProblem here: an unset cell is not a recovery.
            NotifyIfChanged();
            return;
        }

        if (_state.FindPlan(target.PlanGuid) is null)
        {
            // That plan no longer exists on this machine: treat it as an unset
            // cell instead of failing a switch every two seconds.
            _state.LastProblem =
                "The planned power plan is no longer on this PC, so nothing was switched.";
            _state.LastProblemKind = ProblemKind.TargetMissing;
            AppLog.Warn("target plan missing, leaving the active plan alone: " + target.PlanGuid);
            NotifyIfChanged();
            return;
        }

        if (_state.ActivePlan?.Guid == target.PlanGuid.Value)
        {
            _state.ClearSwitchProblemOnly();
            NotifyIfChanged();
            return;
        }

        if (_plans.TryApply(target.PlanGuid.Value))
        {
            _state.ActivePlan = _state.FindPlan(target.PlanGuid);
            _state.LastChanged = DateTimeOffset.Now;
            _state.ClearSwitchProblemOnly();
            AppLog.Info(
                $"switched to {_state.ActivePlan?.Name ?? target.PlanGuid.Value.ToString()} " +
                $"({target.Activity}, {target.PowerSource}, {target.Reason})");
        }
        else
        {
            _state.LastProblem =
                $"Could not switch to {_state.FindPlan(target.PlanGuid)?.Name ?? target.PlanGuid.Value.ToString()}.";
            _state.LastProblemKind = ProblemKind.SwitchFailed;
            AppLog.Error("switch failed for plan " + target.PlanGuid);
        }

        NotifyIfChanged();
    }

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
