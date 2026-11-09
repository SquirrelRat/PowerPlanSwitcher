using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;
using PowerPlanSwitcher.Settings;

namespace PowerPlanSwitcher;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\PowerPlanSwitcher-SingleInstance";
    private const string ShowWindowEventName = @"Local\PowerPlanSwitcher-ShowWindow";

    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private EventWaitHandle? _showEvent;

    private SystemProbe? _probe;
    private AppState? _state;
    private PlanMonitor? _monitor;
    private TrayIconManager? _tray;
    private SettingsWindow? _settings;
    private ConfigStore? _configStore;
    private Thread? _showWindowThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerPlanSwitcher");

        try
        {
            AppLog.Configure(dataDirectory);
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
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Second-instance signal degrades to "just exit"; the app still runs.
            _showEvent = null;
        }
        catch (Exception)
        {
            _showEvent = null;
        }

        bool created;
        _mutex = AcquireSingleInstance(out created);
        _ownsMutex = created;

        if (!created)
        {
            // Another copy is running. Ask it to show itself and go away.
            SignalShowWindow();
            Shutdown();
            return;
        }

        AppLog.Info("starting");

        _configStore = new ConfigStore(dataDirectory);

        string? configProblem = null;
        _configStore.ProblemReported += m => configProblem = m;

        var config = _configStore.Load();
        if (config.Rules.UnsetSlots.Count > 0)
        {
            AppLog.Info($"rules with unset cells: {string.Join(", ", config.Rules.UnsetSlots)}");
        }

        var planService = new PowerPlanService();
        var plans = planService.ListPlans();
        var active = planService.GetActivePlan();

        _probe = new SystemProbe();

        _state = new AppState
        {
            Config = config,
            Plans = plans,
            ActivePlan = active,
            // Config-load problems are sticky: LastProblemKind stays null so routine
            // switch passes never clear them via ClearSwitchProblemOnly.
            LastProblem = configProblem,
        };

        _monitor = new PlanMonitor(_probe, _state, planService);

        // Apply the rules immediately, then watch for lock and unlock.
        _monitor.RunNow();
        _probe.WatchLock(_ =>
        {
            Dispatcher.Invoke(() => _monitor?.RunNow(), DispatcherPriority.Background);
        });

        _tray = new TrayIconManager(_state);
        _tray.OpenSettingsRequested += () => Dispatcher.Invoke(ShowSettings);
        _tray.ForceRequested += role => Dispatcher.Invoke(() => SetForcedPlan(role));
        _tray.ExitRequested += () => Dispatcher.Invoke(ExitApp);

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

        AppLog.Info("ready");
    }

    private void ShowSettings()
    {
        try
        {
            if (_settings is null)
            {
                _settings = new SettingsWindow(_state!, _monitor!, () => SetForcedPlan(null), SaveConfig);
            }

            _settings.Refresh();
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
            _configStore?.Save(_state.Config);
        }
        catch (Exception ex)
        {
            AppLog.Error("saving settings failed: " + ex);
        }
    }

    /// <summary>Null puts the app back on automatic.</summary>
    private void SetForcedPlan(PlanRole? role)
    {
        if (_state is null || _monitor is null)
        {
            return;
        }

        try
        {
            if (role is null)
            {
                _state.ForcedPlan = null;
                _state.ForcedRole = null;
                AppLog.Info("automatic mode restored");
            }
            else
            {
                // Force pins the well-known plan for that role, not a rule cell:
                // rule cells move when the user edits them, the role must not.
                Guid plan = role switch
                {
                    PlanRole.Idle => WellKnownPlans.PowerSaver,
                    PlanRole.Balanced => WellKnownPlans.Balanced,
                    PlanRole.Boost => WellKnownPlans.HighPerformance,
                    _ => throw new ArgumentOutOfRangeException(nameof(role)),
                };

                _state.ForcedPlan = plan;
                _state.ForcedRole = role;
                AppLog.Info($"forced plan for role {role}: {plan}");
            }

            _monitor.RunNow();
        }
        catch (Exception ex)
        {
            AppLog.Error("forcing a plan failed: " + ex);
        }
    }

    private void ExitApp()
    {
        try
        {
            _configStore?.Save(_state!.Config);
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

        ReleaseMutexIfOwned();

        Shutdown();
    }

    /// <summary>
    /// Takes the single-instance mutex. A previous instance that died without
    /// releasing leaves it abandoned; that still means "ours now", not a crash.
    /// Only the creator owns the mutex; a second instance must never release it.
    /// </summary>
    private static Mutex AcquireSingleInstance(out bool created)
    {
        created = false;
        try
        {
            return new Mutex(initiallyOwned: true, SingleInstanceName, out created);
        }
        catch (AbandonedMutexException ex)
        {
            // The previous owner died without releasing. We own it now.
            created = true;
            if (ex.Mutex is not null)
            {
                return ex.Mutex;
            }

            try
            {
                var existing = Mutex.OpenExisting(SingleInstanceName);
                try
                {
                    existing.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // We now own the abandoned mutex.
                }
                catch (Exception)
                {
                }

                created = true;
                return existing;
            }
            catch (Exception)
            {
                return new Mutex(initiallyOwned: true, SingleInstanceName, out created);
            }
        }
        catch (Exception)
        {
            // Mutex creation failed (e.g. security). Fall back to an unowned
            // handle so this instance exits as the "second" one.
            created = false;
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
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.Invoke(ShowSettings);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("show-window listener stopped: " + ex);
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
