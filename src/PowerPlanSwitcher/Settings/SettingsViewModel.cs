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
    private readonly PlanMonitor _monitor;
    private bool _suppressBindings;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(AppState state, PlanMonitor PlanMonitor)
    {
        _state = state;
        _monitor = PlanMonitor;

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

    public string StatusLine => _state.ForcedPlan is null
        ? _state.ActivePlan?.Name ?? "No plan active"
        : (_state.ActivePlan?.Name ?? "No plan active") + " (forced)";

    public string StatusDetail
    {
        get
        {
            string activity = _state.Target?.Activity switch
            {
                Core.Activity.Idle => "Idle",
                Core.Activity.Active => "Active",
                _ => "Checking",
            };

            string power = _state.Target?.PowerSource switch
            {
                PowerSource.Battery => "on battery",
                PowerSource.Ac => "on AC",
                _ => "",
            };

            string mode = _state.ForcedPlan is null ? "Automatic" : "Forced by you";
            string reason = _state.Target?.Reason ?? "";

            string detail = $"{activity} {power} - {mode}";
            return string.IsNullOrEmpty(reason) ? detail : $"{detail} - {reason}";
        }
    }

    public Guid? ActiveAcPlan { get; set; }
    public Guid? ActiveBatteryPlan { get; set; }
    public Guid? IdleAcPlan { get; set; }
    public Guid? IdleBatteryPlan { get; set; }

    private bool _startWithWindows;
    private int _inputTimeoutMinutes;
    private int _scanIntervalSeconds;
    private bool _onScreensaver;
    private bool _onLock;

    /// <summary>Pulls the live state into the bindings. Called on load and after each poll.</summary>
    public void RefreshFromState()
    {
        _suppressBindings = true;

        AvailablePlans.Clear();
        AvailablePlans.Add(new PowerPlanOption(null, "Not set"));
        foreach (PowerPlanInfo plan in _state.Plans)
        {
            AvailablePlans.Add(new PowerPlanOption(plan));
        }

        SyncScanChoices();

        ActiveAcPlan = _state.Config.Rules.Get(Core.Activity.Active, PowerSource.Ac);
        ActiveBatteryPlan = _state.Config.Rules.Get(Core.Activity.Active, PowerSource.Battery);
        IdleAcPlan = _state.Config.Rules.Get(Core.Activity.Idle, PowerSource.Ac);
        IdleBatteryPlan = _state.Config.Rules.Get(Core.Activity.Idle, PowerSource.Battery);

        _startWithWindows = _state.Config.StartWithWindows;
        _inputTimeoutMinutes = _state.Config.Idle.InputTimeoutMinutes;
        _scanIntervalSeconds = _state.Config.ScanIntervalSeconds;
        _onScreensaver = _state.Config.Idle.OnScreensaver;
        _onLock = _state.Config.Idle.OnLock;

        OnPropertyChanged(string.Empty);
        _suppressBindings = false;
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

    private PowerPlanOption? OptionFor(Guid? planGuid) =>
        planGuid is null
            ? AvailablePlans.FirstOrDefault(o => o.Guid is null)
            : AvailablePlans.FirstOrDefault(o => o.Guid == planGuid.Value)
                ?? AvailablePlans.FirstOrDefault(o => o.Guid is null);

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
