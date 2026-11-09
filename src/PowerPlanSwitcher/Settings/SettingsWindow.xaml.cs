using System.Windows;
using System.Windows.Controls;
using PowerPlanSwitcher.Services;
using PowerPlanSwitcher.Theme;

namespace PowerPlanSwitcher.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly Action _forceAuto;
    private readonly Action _saveConfig;
    private bool _sessionEnding;

    /// <param name="forceAuto">Called when the user picks "Force auto".</param>
    /// <param name="saveConfig">Persists the config after a user change.</param>
    public SettingsWindow(AppState state, PlanMonitor planMonitor, Action forceAuto, Action saveConfig)
    {
        _forceAuto = forceAuto;
        _saveConfig = saveConfig;
        _viewModel = new SettingsViewModel(state, planMonitor);
        DataContext = _viewModel;

        InitializeComponent();

        ThemeManager.Apply(this);
        _viewModel.RefreshFromState();

        System.Windows.Application.Current.SessionEnding += (_, _) => _sessionEnding = true;
        Closed += (_, _) =>
        {
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
    public void Refresh() => _viewModel.RefreshFromState();

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

        if (AutoStart.CanRegister)
        {
            new AutoStart().SetEnabled(_viewModel.StartWithWindows);
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

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        AppLog.Open();
    }

    private void OnForceAuto(object sender, RoutedEventArgs e)
    {
        _forceAuto();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
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
