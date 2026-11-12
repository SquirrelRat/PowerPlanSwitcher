using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Services;
using PowerPlanSwitcher.Theme;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace PowerPlanSwitcher.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly Action _saveConfig;
    private readonly Action _installUpdate;
    private readonly DispatcherTimer _logTimer;
    private bool _sessionEnding;
    private bool _forceClose;
    private bool _logVisible;
    private double _widthBeforeLog;

    public SettingsWindow(AppState state, IPlanMonitor planMonitor, Action saveConfig, Action installUpdate)
    {
        _saveConfig = saveConfig;
        _installUpdate = installUpdate;

        InitializeComponent();

        _viewModel = new SettingsViewModel(state, planMonitor);
        DataContext = _viewModel;

        ThemeManager.Apply(this);
        _viewModel.RefreshFromState();
        UpdateBatteryColumns();

        _logTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            (_, _) => TickLog(),
            Dispatcher);
        _logTimer.Start();

        Loaded += (_, _) =>
        {
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 48);
            MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 48);
            Height = Math.Min(Height, MaxHeight);
        };

        System.Windows.Application.Current.SessionEnding += (_, _) => _sessionEnding = true;
        Closed += (_, _) =>
        {
            _logTimer.Stop();

            try
            {
                _viewModel.Dispose();
            }
            catch (Exception)
            {
            }
        };
    }

    /// <summary>Pulls fresh state into the window; called when it is shown.</summary>
    public void Refresh()
    {
        _viewModel.RefreshFromState();
        UpdateBatteryColumns();
    }

    /// <summary>Opens the log pane, optionally after a tray request.</summary>
    public void ShowLog()
    {
        if (!_logVisible)
        {
            _widthBeforeLog = Width;
            _logVisible = true;
            LogColumn.Width = new GridLength(340);
            LogSplitter.Visibility = Visibility.Visible;
            LogPanel.Visibility = Visibility.Visible;
            Width = Math.Max(Width, 900);
        }

        RefreshLogFiles();
    }

    private void HideLog()
    {
        _logVisible = false;
        LogColumn.Width = new GridLength(0);
        LogSplitter.Visibility = Visibility.Collapsed;
        LogPanel.Visibility = Visibility.Collapsed;
        Width = _widthBeforeLog > 0 ? _widthBeforeLog : Math.Max(MinWidth, 540);
    }

    private void UpdateBatteryColumns()
    {
        bool show = _viewModel.ShowBattery;

        BatteryColumn.Width = show
            ? new GridLength(1.35, GridUnitType.Star)
            : new GridLength(0);

        BatteryHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ActiveBatteryCombo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        IdleBatteryCombo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TickLog()
    {
        if (!_logVisible || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (LogLive.IsChecked == true)
        {
            RefreshLogText();
        }
    }

    private void RefreshLogFiles()
    {
        var files = AppLog.ListFiles();
        LogFileInfo? selected = LogDateCombo.SelectedItem as LogFileInfo;

        LogDateCombo.ItemsSource = files;
        LogDateCombo.SelectedItem = selected is null
            ? files.FirstOrDefault()
            : files.FirstOrDefault(f => f.Path == selected.Path) ?? files.FirstOrDefault();

        RefreshLogText();
    }

    private void RefreshLogText()
    {
        if (LogDateCombo.SelectedItem is LogFileInfo file)
        {
            string text = AppLog.ReadTail(file.Path, 2000);

            if (LogText.Text != text)
            {
                bool follow = LogText.VerticalOffset + LogText.ViewportHeight >= LogText.ExtentHeight - 8;
                LogText.Text = text;

                if (follow)
                {
                    LogText.ScrollToEnd();
                }
            }
        }
        else
        {
            LogText.Text = "No log entries yet.";
        }
    }

    private void OnRuleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.CommitRules())
        {
            _saveConfig();
        }
    }

    private void OnScreensaverOrLockChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CommitSettings())
        {
            _saveConfig();
        }
    }

    private void OnCheckForUpdatesChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CommitSettings())
        {
            _saveConfig();
        }
    }

    private void OnInstallUpdate(object sender, RoutedEventArgs e) => _installUpdate();

    private void OnScanIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.CommitSettings())
        {
            _saveConfig();
        }
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CommitSettings())
        {
            return;
        }

        _saveConfig();

        if (!AutoStart.CanRegister)
        {
            _viewModel.StartWithWindows = false;
            _saveConfig();
            return;
        }

        var autoStart = new AutoStart();
        autoStart.SetEnabled(_viewModel.StartWithWindows);

        if (autoStart.IsEnabled() != _viewModel.StartWithWindows)
        {
            _viewModel.StartWithWindows = !_viewModel.StartWithWindows;
            _saveConfig();

            MessageBox.Show(
                "Windows did not accept the startup change. The checkbox was put back.",
                "Power Plan Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnTimeoutCommitted(object sender, RoutedEventArgs e)
    {
        // The binding only pushes on lost focus; make sure the typed text
        // reached the view model before committing it.
        (sender as System.Windows.Controls.TextBox)?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();

        if (_viewModel.CommitSettings())
        {
            _saveConfig();
        }
    }

    private void OnShowLog(object sender, RoutedEventArgs e)
    {
        if (_logVisible)
        {
            HideLog();
        }
        else
        {
            ShowLog();
        }
    }

    private void OnCloseLog(object sender, RoutedEventArgs e) => HideLog();

    private void OnLogFileChanged(object sender, SelectionChangedEventArgs e) => RefreshLogText();

    private void OnLogRefresh(object sender, RoutedEventArgs e) => RefreshLogFiles();

    private void OnLogOpenFile(object sender, RoutedEventArgs e)
    {
        if (LogDateCombo.SelectedItem is LogFileInfo file)
        {
            AppLog.OpenFile(file.Path);
        }
    }

    private void OnLogOpenFolder(object sender, RoutedEventArgs e) => AppLog.OpenFolder();

    private void OnLogCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogText.Text);
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process.
        }
    }

    private void OnPauseHour(object sender, RoutedEventArgs e) =>
        _viewModel.Pause(TimeSpan.FromHours(1));

    private void OnPauseRestart(object sender, RoutedEventArgs e) =>
        _viewModel.Pause(null);

    private void OnResume(object sender, RoutedEventArgs e) => _viewModel.Resume();

    private void OnReturnToAutomatic(object sender, RoutedEventArgs e) =>
        _viewModel.ReturnToAutomatic();

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.BuildDiagnostics());
            MessageBox.Show(
                "Diagnostics copied to the clipboard.",
                "Power Plan Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception)
        {
            MessageBox.Show(
                "The clipboard is busy. Try again.",
                "Power Plan Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnOpenConfigFolder(object sender, RoutedEventArgs e) =>
        OpenFolder(AppPaths.DataDirectory);

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) =>
        AppLog.OpenFolder();

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Closes the window for real, bypassing the hide-on-close behavior. Used
    /// when App abandons a window that failed mid-open, so its log timer and
    /// state subscription are released instead of leaking beside a new window.
    /// </summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // A forced close must reach the Closed handlers (timer stop, dispose).
        if (_forceClose)
        {
            base.OnClosing(e);
            return;
        }

        // Let Windows shutdown/logoff proceed; config is saved on every change.
        if (_sessionEnding)
        {
            base.OnClosing(e);
            return;
        }

        // Keep the app running; only hide the window.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
