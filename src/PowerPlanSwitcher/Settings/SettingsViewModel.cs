using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;

namespace PowerPlanSwitcher.Settings;

/// <summary>
/// Everything the settings window binds to. Raises change notifications so the
/// UI tracks the plan the PlanMonitor applies.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppState _state;
    private readonly IPlanMonitor _monitor;
    private bool _suppressBindings;
    private bool _disposed;
    private string? _planKey;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(AppState state, IPlanMonitor planMonitor)
    {
        _state = state;
        _monitor = planMonitor;

        _state.Changed += OnStateChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _state.Changed -= OnStateChanged;
        }
        catch (Exception)
        {
        }
    }

    public ObservableCollection<PowerPlanOption> AvailablePlans { get; } = new();

    public ObservableCollection<ScanIntervalOption> ScanIntervalChoices { get; } = new();

    public IReadOnlyList<SwitchRecord> RecentChanges =>
        _state.History.Reverse().Take(5).ToArray();

    public string VersionText => AppInfo.VersionText;

    public ScanIntervalOption? SelectedScanInterval
    {
        get => ScanIntervalChoices.FirstOrDefault(o => o.Seconds == _scanIntervalSeconds);
        set
        {
            if (value is null)
            {
                return;
            }

            ScanIntervalSeconds = value.Seconds;
        }
    }

    public int ScanIntervalSeconds
    {
        get => _scanIntervalSeconds;
        set
        {
            if (_scanIntervalSeconds == value)
            {
                return;
            }

            _state.Config.ScanIntervalSeconds = value;

            // The config clamps to 1-60; show the clamped value, not the raw one.
            _scanIntervalSeconds = _state.Config.ScanIntervalSeconds;
            SyncScanChoices();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScanInterval));
        }
    }

    // Rule selections, exposed as combo-box items.

    public PowerPlanOption? SelectedActiveAc
    {
        get => OptionFor(ActiveAcPlan);
        set => ActiveAcPlan = value?.Guid;
    }

    public PowerPlanOption? SelectedActiveBattery
    {
        get => OptionFor(ActiveBatteryPlan);
        set => ActiveBatteryPlan = value?.Guid;
    }

    public PowerPlanOption? SelectedIdleAc
    {
        get => OptionFor(IdleAcPlan);
        set => IdleAcPlan = value?.Guid;
    }

    public PowerPlanOption? SelectedIdleBattery
    {
        get => OptionFor(IdleBatteryPlan);
        set => IdleBatteryPlan = value?.Guid;
    }

    // Settings

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            _startWithWindows = value;
            _state.Config.StartWithWindows = value;
            OnPropertyChanged();
        }
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            if (_checkForUpdates == value)
            {
                return;
            }

            _checkForUpdates = value;
            _state.Config.CheckForUpdates = value;
            OnPropertyChanged();
        }
    }

    public int InputTimeoutMinutes
    {
        get => _inputTimeoutMinutes;
        set
        {
            if (_inputTimeoutMinutes == value)
            {
                return;
            }

            _state.Config.Idle.InputTimeoutMinutes = value;

            // The settings clamp to 1-120; show the clamped value, not the raw typing.
            _inputTimeoutMinutes = _state.Config.Idle.InputTimeoutMinutes;
            OnPropertyChanged();
        }
    }

    public bool OnScreensaver
    {
        get => _onScreensaver;
        set
        {
            if (_onScreensaver == value)
            {
                return;
            }

            _onScreensaver = value;
            _state.Config.Idle.OnScreensaver = value;
            OnPropertyChanged();
        }
    }

    public bool OnLock
    {
        get => _onLock;
        set
        {
            if (_onLock == value)
            {
                return;
            }

            _onLock = value;
            _state.Config.Idle.OnLock = value;
            OnPropertyChanged();
        }
    }

    // Status, derived from the live state

    public string StatusLine
    {
        get
        {
            string plan = _state.ActivePlan?.Name ?? "No plan active";

            if (_state.Paused)
            {
                return plan + " (paused)";
            }

            return _state.ForcedPlan is null ? plan : plan + " (forced)";
        }
    }

    public string StatusDetail
    {
        get
        {
            var parts = new List<string>(4);

            if (_state.Target is not null)
            {
                parts.Add(_state.Target.Activity == Core.Activity.Idle ? "Idle" : "Active");
                parts.Add(_state.Target.PowerSource switch
                {
                    PowerSource.Battery => "on battery",
                    PowerSource.Ac => "on AC",
                    _ => "power source unknown",
                });
            }

            parts.Add(ModeLabel);

            string? reason = _state.Target?.Reason;
            if (!string.IsNullOrWhiteSpace(reason) &&
                !parts.Contains(reason, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(reason);
            }

            return string.Join(" - ", parts);
        }
    }

    public string ModeLabel => _state.Paused
        ? "Paused"
        : _state.ForcedPlan is null ? "Automatic" : "Forced";

    public string LastCheckedText
    {
        get
        {
            if (_state.LastChecked == default)
            {
                return "Not checked yet";
            }

            return $"Checked at {_state.LastChecked:HH:mm:ss}";
        }
    }

    public string PauseLabel
    {
        get
        {
            if (!_state.Paused)
            {
                return string.Empty;
            }

            if (_state.PausedUntil is null)
            {
                return "Switching stays paused until you resume or restart the app.";
            }

            return $"Switching resumes at {_state.PausedUntil.Value:HH:mm}.";
        }
    }

    public bool IsPaused => _state.Paused;

    public bool CanPause => !_state.Paused;

    public bool IsForced => _state.ForcedPlan is not null;

    public bool IsAutomatic => !IsForced;

    public bool ShowBattery => _state.HasBattery;

    /// <summary>True once at least one switch has happened this run.</summary>
    public bool HasRecentChanges => _state.History.Count > 0;

    /// <summary>True when the monitor has decided a target at least once.</summary>
    public bool HasTarget => _state.Target is not null;

    /// <summary>True when the decided target is idle (drives the status dot).</summary>
    public bool ShowIdleDot => _state.Target is not null && _state.Target.Activity == Core.Activity.Idle;

    public bool CanUpdate => _state.PendingUpdate is not null;

    public string UpdateLine => _state.PendingUpdate is { } pending
        ? $"Version {pending.Version} available"
        : $"Up to date ({AppInfo.Version})";

    public string UpdateNote => _state.UpdateStatus ?? string.Empty;

    public bool HasUpdateNote => !string.IsNullOrEmpty(_state.UpdateStatus);

    public string Problem => _state.LastProblem ?? string.Empty;

    public bool HasProblem => !string.IsNullOrEmpty(_state.LastProblem);

    public string ForcedPlanName =>
        _state.FindPlan(_state.ForcedPlan)?.Name ?? "a plan that is no longer installed";

    public string ForcedNotice => IsForced
        ? $"Forced: {ForcedPlanName}. The rules below apply again when you return to automatic."
        : string.Empty;

    public Guid? ActiveAcPlan { get; set; }
    public Guid? ActiveBatteryPlan { get; set; }
    public Guid? IdleAcPlan { get; set; }
    public Guid? IdleBatteryPlan { get; set; }

    private bool _startWithWindows;
    private bool _checkForUpdates;
    private int _inputTimeoutMinutes;
    private int _scanIntervalSeconds;
    private bool _onScreensaver;
    private bool _onLock;

    /// <summary>Pulls the live state into the bindings. Called on load and after each poll.</summary>
    public void RefreshFromState()
    {
        _suppressBindings = true;

        SyncPlans();
        SyncScanChoices();

        ActiveAcPlan = _state.Config.Rules.Get(Core.Activity.Active, PowerSource.Ac);
        ActiveBatteryPlan = _state.Config.Rules.Get(Core.Activity.Active, PowerSource.Battery);
        IdleAcPlan = _state.Config.Rules.Get(Core.Activity.Idle, PowerSource.Ac);
        IdleBatteryPlan = _state.Config.Rules.Get(Core.Activity.Idle, PowerSource.Battery);

        EnsureMissingOption(ActiveAcPlan);
        EnsureMissingOption(ActiveBatteryPlan);
        EnsureMissingOption(IdleAcPlan);
        EnsureMissingOption(IdleBatteryPlan);

        _startWithWindows = _state.Config.StartWithWindows;
        _checkForUpdates = _state.Config.CheckForUpdates;
        _inputTimeoutMinutes = _state.Config.Idle.InputTimeoutMinutes;
        _scanIntervalSeconds = _state.Config.ScanIntervalSeconds;
        _onScreensaver = _state.Config.Idle.OnScreensaver;
        _onLock = _state.Config.Idle.OnLock;

        OnPropertyChanged(string.Empty);
        _suppressBindings = false;
    }

    /// <summary>Rebuilds the plan list only when the plan set itself changed.</summary>
    private void SyncPlans()
    {
        string key = _state.PlansKey;

        if (key == _planKey)
        {
            return;
        }

        _planKey = key;
        AvailablePlans.Clear();
        AvailablePlans.Add(new PowerPlanOption(null, "Not set"));

        foreach (PowerPlanInfo plan in _state.Plans)
        {
            AvailablePlans.Add(new PowerPlanOption(plan));
        }
    }

    private void EnsureMissingOption(Guid? planGuid)
    {
        if (planGuid is null)
        {
            return;
        }

        if (AvailablePlans.Any(o => o.Guid == planGuid.Value))
        {
            return;
        }

        AvailablePlans.Add(new PowerPlanOption(planGuid.Value, "Missing plan"));
    }

    /// <summary>
    /// Rebuilds the scan-timer presets, keeping the live value selectable even
    /// when it is not a preset (e.g. hand-edited to 60 in the settings file).
    /// </summary>
    private void SyncScanChoices()
    {
        var wanted = new List<int> { 1, 2, 5, 10, 15, 30, 60 };
        if (!wanted.Contains(_scanIntervalSeconds))
        {
            wanted.Add(_scanIntervalSeconds);
        }

        wanted.Sort();

        if (!ScanIntervalChoices.Select(o => o.Seconds).SequenceEqual(wanted))
        {
            ScanIntervalChoices.Clear();
            foreach (int seconds in wanted)
            {
                ScanIntervalChoices.Add(new ScanIntervalOption(seconds));
            }
        }
    }

    /// <summary>A poll landed. Refresh without disturbing a control the user is editing.</summary>
    private void OnStateChanged()
    {
        if (_suppressBindings)
        {
            return;
        }

        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.ComboBox { IsDropDownOpen: true })
        {
            return;
        }

        RefreshFromState();
    }

    /// <summary>
    /// Persists the rules grid and applies a change at once. Returns false when
    /// the call came from a programmatic refresh (rebuilding the combo boxes
    /// fires selection events) rather than the user, so callers can skip saving.
    /// </summary>
    public bool CommitRules()
    {
        if (_suppressBindings)
        {
            return false;
        }

        _state.Config.Rules.Set(Core.Activity.Active, PowerSource.Ac, ActiveAcPlan);
        _state.Config.Rules.Set(Core.Activity.Active, PowerSource.Battery, ActiveBatteryPlan);
        _state.Config.Rules.Set(Core.Activity.Idle, PowerSource.Ac, IdleAcPlan);
        _state.Config.Rules.Set(Core.Activity.Idle, PowerSource.Battery, IdleBatteryPlan);

        _monitor.RunNow();
        return true;
    }

    public bool CommitSettings()
    {
        if (_suppressBindings)
        {
            return false;
        }

        _monitor.RunNow();
        return true;
    }

    public void Pause(TimeSpan? duration)
    {
        _state.Pause(duration);
        AppLog.Info(duration is null
            ? "switching paused until restart"
            : $"switching paused for {duration.Value.TotalMinutes:0} min");
        _monitor.RunNow();
    }

    public void Resume()
    {
        _state.Resume();
        AppLog.Info("switching resumed");
        _monitor.RunNow();
    }

    public void ReturnToAutomatic()
    {
        _state.ForcedPlan = null;
        AppLog.Info("automatic mode restored");
        _monitor.RunNow();
    }

    /// <summary>A text bundle for a support request. Local only; no network.</summary>
    public string BuildDiagnostics()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"Power Plan Switcher {AppInfo.Version}");
        text.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        text.AppendLine($".NET: {Environment.Version}");
        text.AppendLine($"Config: {AppPaths.SettingsPath}");
        text.AppendLine($"Logs: {AppLog.LogDirectory}");
        text.AppendLine($"Mode: {ModeLabel}");
        text.AppendLine($"Active plan: {_state.ActivePlan?.Name ?? "none"}");
        text.AppendLine($"Forced plan: {_state.ForcedPlan?.ToString() ?? "none"}");
        text.AppendLine($"Battery present: {_state.HasBattery}");
        text.AppendLine($"Last problem: {_state.LastProblem ?? "none"}");
        text.AppendLine("Plans:");

        foreach (PowerPlanInfo plan in _state.Plans)
        {
            text.AppendLine($"  {plan.Name} ({plan.Guid})");
        }

        text.AppendLine("--- last 40 log lines ---");
        text.AppendLine(AppLog.ReadTail(AppLog.LogPath, 40));
        return text.ToString();
    }

    private PowerPlanOption? OptionFor(Guid? planGuid)
    {
        if (planGuid is null)
        {
            return AvailablePlans.FirstOrDefault(o => o.Guid is null);
        }

        PowerPlanOption? found = AvailablePlans.FirstOrDefault(o => o.Guid == planGuid.Value);
        if (found is not null)
        {
            return found;
        }

        var missing = new PowerPlanOption(planGuid.Value, "Missing plan");
        AvailablePlans.Add(missing);
        return missing;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A plan as shown in a combo box. A null GUID means "Not set".</summary>
public sealed class PowerPlanOption
{
    public PowerPlanOption(PowerPlanInfo plan)
        : this(plan.Guid, plan.Name)
    {
    }

    public PowerPlanOption(Guid? guid, string name)
    {
        Guid = guid;
        Name = name;
    }

    public Guid? Guid { get; }

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>A scan-timer choice as shown in a combo box.</summary>
public sealed class ScanIntervalOption
{
    public ScanIntervalOption(int seconds)
    {
        Seconds = seconds;
    }

    public int Seconds { get; }

    public string Label => Seconds == AppConfig.DefaultScanIntervalSeconds
        ? $"Every {Seconds} seconds (default)"
        : Seconds == 1
            ? "Every second"
            : $"Every {Seconds} seconds";

    public override string ToString() => Label;
}
