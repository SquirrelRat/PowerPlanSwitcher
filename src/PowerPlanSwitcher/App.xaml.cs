using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;
using PowerPlanSwitcher.Settings;
using MessageBox = System.Windows.MessageBox;

namespace PowerPlanSwitcher;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\PowerPlanSwitcher-SingleInstance";
    private const string ShowWindowEventName = @"Local\PowerPlanSwitcher-ShowWindow";

    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private volatile bool _shuttingDown;

    private SystemProbe? _probe;
    private AppState? _state;
    private PlanMonitor? _monitor;
    private TrayIconManager? _tray;
    private SettingsWindow? _settings;
    private ConfigStore? _configStore;
    private UpdateService? _updateService;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private string? _restartPath;
    private Thread? _showWindowThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppLog.Configure(AppPaths.LogDirectory);
        }
        catch (Exception)
        {
        }

        // Register before the single-instance check so the second instance
        // also logs instead of crashing silently on exit.
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                AppLog.Error("unhandled UI exception: " + args.Exception);
            }
            catch (Exception)
            {
            }

            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                AppLog.Error("unhandled background exception: " + args.ExceptionObject);
            }
            catch (Exception)
            {
            }
        };

        // The show-window event must exist before anything else: a second
        // instance signals it, and creating it late loses fast double-launches.
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        }
        catch (Exception)
        {
            // Second-instance signal degrades to "just exit"; the app still runs.
            _showEvent = null;
        }

        _mutex = AcquireSingleInstance(out bool created);
        _ownsMutex = created;

        if (!created)
        {
            // Another copy is running. Ask it to show itself and go away.
            SignalShowWindow();
            Shutdown();
            return;
        }

        try
        {
            InitializeApp();
        }
        catch (Exception ex)
        {
            AppLog.Error("startup failed: " + ex);

            try
            {
                MessageBox.Show(
                    "Power Plan Switcher could not start.\n\n" +
                    "The details were written to:\n" + AppPaths.LogDirectory,
                    "Power Plan Switcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception)
            {
            }

            Shutdown();
        }
    }

    private void InitializeApp()
    {
        AppLog.Info("starting");

        _configStore = new ConfigStore(AppPaths.DataDirectory);

        bool firstRun = false;
        try
        {
            firstRun = !File.Exists(_configStore.SettingsPath);
        }
        catch (Exception)
        {
        }

        string? configProblem = null;
        _configStore.ProblemReported += message =>
        {
            if (_state is null)
            {
                configProblem = configProblem is null
                    ? message
                    : configProblem + " " + message;
                return;
            }

            // A save failure after startup must reach the window, not just the log.
            _state.LastProblem = message;
            _state.LastProblemKind = ProblemKind.SettingsWriteFailed;
            _state.NotifyChanged();
        };

        var config = _configStore.Load();
        if (config.Rules.UnsetSlots.Count > 0)
        {
            AppLog.Info($"rules with unset cells: {string.Join(", ", config.Rules.UnsetSlots)}");
        }

        var planService = new PowerPlanService();
        var plans = planService.ListPlans() ?? Array.Empty<PowerPlanInfo>();
        var active = planService.GetActivePlan();

        _probe = new SystemProbe();

        _state = new AppState
        {
            Config = config,
            Plans = plans,
            ActivePlan = active,
            HasBattery = _probe.HasBattery(),
            // Config-load problems are sticky: LastProblemKind is ConfigLoad so
            // routine switch passes never clear them.
            LastProblem = configProblem,
            LastProblemKind = configProblem is null ? null : ProblemKind.ConfigLoad,
        };

        _monitor = new PlanMonitor(_probe, _state, planService);

        // Apply the rules immediately, then watch for lock and unlock.
        _monitor.RunNow();
        _probe.WatchLock(_ =>
        {
            Dispatcher.Invoke(() => _monitor?.RunNow(), DispatcherPriority.Background);
        });

        _tray = new TrayIconManager(_state);
        _tray.OpenSettingsRequested += () => Dispatcher.Invoke(() => ShowSettings());
        _tray.OpenLogRequested += () => Dispatcher.Invoke(() => ShowSettings(showLog: true));
        _tray.ForcePlanRequested += plan => Dispatcher.Invoke(() => SetForcedPlan(plan));
        _tray.PauseRequested += duration => Dispatcher.Invoke(() => PauseSwitching(duration));
        _tray.ResumeRequested += () => Dispatcher.Invoke(ResumeSwitching);
        _tray.UpdateRequested += () => Dispatcher.Invoke(InstallUpdate);
        _tray.ExitRequested += () => Dispatcher.Invoke(ExitApp);

        // Remove the leftover exe from a previous self-update, then start
        // checking; the hourly timer defers to the 24-hour gate inside.
        UpdateService.TryDeleteOldExe();
        _updateService = new UpdateService(_state, AppPaths.DataDirectory);
        _updateTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromHours(1),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => _ = RunUpdateCheckAsync(),
            Dispatcher);
        _updateTimer.Start();

        StartShowWindowListener();

        var autoStart = new AutoStart();
        if (config.StartWithWindows)
        {
            // Heal a stale path (exe moved) by comparing values, not just presence.
            if (AutoStart.CanRegister &&
                !string.Equals(autoStart.RegisteredCommand(), AutoStart.StartupCommand, StringComparison.Ordinal))
            {
                autoStart.SetEnabled(true);
            }
        }
        else if (autoStart.IsEnabled())
        {
            autoStart.SetEnabled(false);
        }

        if (firstRun)
        {
            _tray.ShowWelcome();
        }

        AppLog.Info("ready");

        _ = RunUpdateCheckAsync();
    }

    private async Task RunUpdateCheckAsync()
    {
        if (_updateService is null)
        {
            return;
        }

        try
        {
            await _updateService.CheckAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("update check failed: " + ex);
        }
    }

    private async void InstallUpdate()
    {
        if (_updateService is null)
        {
            return;
        }

        try
        {
            if (await _updateService.ApplyAsync())
            {
                // Start the new exe only after this instance releases the
                // single-instance mutex, or the new one exits at once.
                _restartPath = _updateService.RestartPath;
                ExitApp();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("installing update failed: " + ex);
        }
    }

    private void ShowSettings(bool showLog = false)
    {
        try
        {
            if (_state is null || _monitor is null)
            {
                return;
            }

            if (_settings is null)
            {
                _settings = new SettingsWindow(_state, _monitor, SaveConfig, InstallUpdate);
            }

            _settings.Refresh();

            if (showLog)
            {
                _settings.ShowLog();
            }

            if (_settings.WindowState == WindowState.Minimized)
            {
                _settings.WindowState = WindowState.Normal;
            }

            if (!_settings.IsVisible)
            {
                _settings.Show();
            }

            _settings.Activate();
        }
        catch (Exception ex)
        {
            AppLog.Error("showing settings failed: " + ex);

            // The window may exist even though showing it failed. Close it for
            // real so its timer and subscriptions are released; otherwise they
            // leak while the next ShowSettings builds a second window.
            try
            {
                _settings?.ForceClose();
            }
            catch (Exception)
            {
            }

            _settings = null;
        }
    }

    private void SaveConfig()
    {
        if (_state is null)
        {
            return;
        }

        try
        {
            bool saved = _configStore?.Save(_state.Config) ?? false;

            if (saved && _state.LastProblemKind is ProblemKind.ConfigLoad or ProblemKind.SettingsWriteFailed)
            {
                _state.LastProblem = null;
                _state.LastProblemKind = null;
                _state.NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("saving settings failed: " + ex);
        }
    }

    /// <summary>Null puts the app back on automatic.</summary>
    private void SetForcedPlan(Guid? plan)
    {
        if (_state is null || _monitor is null)
        {
            return;
        }

        try
        {
            if (plan is not null && _state.FindPlan(plan) is null)
            {
                AppLog.Warn("cannot force a plan that is not on this PC: " + plan);
                return;
            }

            _state.Resume();
            _state.ForcedPlan = plan;

            AppLog.Info(plan is null
                ? "automatic mode restored"
                : "forced plan: " + plan);

            _monitor.RunNow();
        }
        catch (Exception ex)
        {
            AppLog.Error("forcing a plan failed: " + ex);
        }
    }

    private void PauseSwitching(TimeSpan? duration)
    {
        if (_state is null)
        {
            return;
        }

        _state.Pause(duration);

        AppLog.Info(duration is null
            ? "switching paused until restart"
            : $"switching paused for {duration.Value.TotalMinutes:0} min");

        _state.NotifyChanged();
    }

    private void ResumeSwitching()
    {
        if (_state is null)
        {
            return;
        }

        _state.Resume();
        AppLog.Info("switching resumed");
        _monitor?.RunNow();
    }

    private void ExitApp()
    {
        try
        {
            if (_state is not null)
            {
                _configStore?.Save(_state.Config);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            AppLog.Info("exiting");
        }
        catch (Exception)
        {
        }

        try
        {
            _tray?.Dispose();
        }
        catch (Exception)
        {
        }

        _tray = null;

        // OnExit releases the mutex after the dispatcher stops. Releasing here
        // would let a fast relaunch exit while this process is still tearing down.
        Shutdown();
    }

    /// <summary>
    /// Takes the single-instance mutex. Creating a mutex does not raise
    /// AbandonedMutexException; a dead owner's object is simply gone.
    /// </summary>
    private static Mutex AcquireSingleInstance(out bool created)
    {
        created = false;

        try
        {
            return new Mutex(initiallyOwned: true, SingleInstanceName, out created);
        }
        catch (Exception)
        {
            // Mutex creation failed (e.g. security). Fall back to an existing
            // handle so this instance exits as the "second" one.
            try
            {
                return Mutex.OpenExisting(SingleInstanceName);
            }
            catch (Exception)
            {
                return new Mutex(initiallyOwned: false, SingleInstanceName, out _);
            }
        }
    }

    private static void SignalShowWindow()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ShowWindowEventName);
            existing.Set();
        }
        catch (Exception)
        {
            // The first instance may just be starting. Nothing else to do.
        }
    }

    private void StartShowWindowListener()
    {
        if (_showEvent is null)
        {
            return;
        }

        _showWindowThread = new Thread(() =>
        {
            try
            {
                while (!_shuttingDown)
                {
                    _showEvent.WaitOne();

                    if (_shuttingDown)
                    {
                        break;
                    }

                    Dispatcher.Invoke(() => ShowSettings());
                }
            }
            catch (Exception ex)
            {
                if (!_shuttingDown)
                {
                    AppLog.Error("show-window listener stopped: " + ex);
                }
            }
        })
        {
            IsBackground = true,
            Name = "ShowWindowListener",
        };

        _showWindowThread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        try
        {
            _showEvent?.Set();
        }
        catch (Exception)
        {
        }

        try
        {
            _showWindowThread?.Join(1000);
        }
        catch (Exception)
        {
        }

        try
        {
            _probe?.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            _monitor?.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            _tray?.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            _showEvent?.Dispose();
        }
        catch (Exception)
        {
        }

        ReleaseMutexIfOwned();

        try
        {
            if (_restartPath is not null)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_restartPath) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // The user can still start the updated exe by hand.
        }

        base.OnExit(e);
    }

    private static void ReleaseMutexIfOwned()
    {
        if (!_ownsMutex)
        {
            try
            {
                _mutex?.Dispose();
            }
            catch (Exception)
            {
            }

            _mutex = null;
            return;
        }

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (Exception)
        {
            // Already released, abandoned, or not owned anymore.
        }

        try
        {
            _mutex?.Dispose();
        }
        catch (Exception)
        {
        }

        _mutex = null;
        _ownsMutex = false;
    }
}
