using System.Drawing;
using System.Windows.Forms;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// The tray icon and its menu. Owns no decisions: it shows the state and
/// forwards user intent.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _idleIcon;
    private readonly Icon _activeIcon;
    private readonly Icon _boostIcon;
    private readonly AppState _state;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autoItem;
    private readonly ToolStripMenuItem _forceIdleItem;
    private readonly ToolStripMenuItem _forceBalancedItem;
    private readonly ToolStripMenuItem _forceBoostItem;
    private readonly ToolStripMenuItem _headerItem;
    private string? _lastBalloonedProblem;

    public TrayIconManager(AppState state)
    {
        _state = state;

        _idleIcon = TrayIcons.Idle;
        _activeIcon = TrayIcons.Active;
        _boostIcon = TrayIcons.Boost;

        _menu = new ContextMenuStrip();
        _headerItem = new ToolStripMenuItem("Power Plan Switcher") { Enabled = false };
        _menu.Items.Add(_headerItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettingsRequested?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());

        _autoItem = new ToolStripMenuItem("Auto", null, (_, _) => ForceRequested?.Invoke(null));
        _menu.Items.Add(_autoItem);

        _forceIdleItem = new ToolStripMenuItem("Force Power saver", null, (_, _) => ForceRequested?.Invoke(PlanRole.Idle));
        _forceBalancedItem = new ToolStripMenuItem("Force Balanced", null, (_, _) => ForceRequested?.Invoke(PlanRole.Balanced));
        _forceBoostItem = new ToolStripMenuItem("Force High performance", null, (_, _) => ForceRequested?.Invoke(PlanRole.Boost));
        _menu.Items.Add(_forceIdleItem);
        _menu.Items.Add(_forceBalancedItem);
        _menu.Items.Add(_forceBoostItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Open log", null, (_, _) => AppLog.Open()));
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = _activeIcon,
            Visible = true,
            Text = "Power Plan Switcher",
            ContextMenuStrip = _menu,
        };

        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                OpenSettingsRequested?.Invoke();
            }
        };

        _icon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                OpenSettingsRequested?.Invoke();
            }
        };

        _state.Changed += Refresh;
        Refresh();
    }

    /// <summary>Raised when the user asks to see the settings window.</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>
    /// Raised when the user picks a plan from the tray. Null means back to automatic.
    /// </summary>
    public event Action<PlanRole?>? ForceRequested;

    public event Action? ExitRequested;

    private void Refresh()
    {
        try
        {
            _icon.Text = BuildTooltip();
            _headerItem.Text = BuildHeaderText();
            _icon.Icon = PickIcon();

            _autoItem.Checked = _state.ForcedPlan is null;
            _forceIdleItem.Checked = _state.ForcedRole == PlanRole.Idle;
            _forceBalancedItem.Checked = _state.ForcedRole == PlanRole.Balanced;
            _forceBoostItem.Checked = _state.ForcedRole == PlanRole.Boost;

            if (_state.LastProblem is null)
            {
                _lastBalloonedProblem = null;
            }
            else if (_state.LastProblem != _lastBalloonedProblem)
            {
                _lastBalloonedProblem = _state.LastProblem;
                _icon.ShowBalloonTip(5000, "Power Plan Switcher", _state.LastProblem, ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("tray refresh failed: " + ex);
        }
    }

    private Icon PickIcon()
    {
        if (_state.ForcedPlan is not null)
        {
            return _state.ForcedRole switch
            {
                PlanRole.Idle => _idleIcon,
                PlanRole.Balanced => _activeIcon,
                PlanRole.Boost => _boostIcon,
                _ => _boostIcon,
            };
        }

        return _state.Target?.Activity == Core.Activity.Idle ? _idleIcon : _activeIcon;
    }

    private string BuildHeaderText()
    {
        string plan = _state.ActivePlan?.Name ?? "no plan active";
        string mode = _state.ForcedPlan is null ? "auto" : "forced";

        if (_state.Target is null)
        {
            return $"{plan} ({mode})";
        }

        string activity = _state.Target.Activity == Core.Activity.Idle ? "idle" : "active";
        string power = _state.Target.PowerSource == Core.PowerSource.Battery ? "on battery" : "on AC";
        return $"{plan}: {activity}, {power} ({mode})";
    }

    private string BuildTooltip()
    {
        string text = BuildHeaderText();

        // The NotifyIcon text is capped at 63 characters by Windows.
        if (text.Length <= 63)
        {
            return text;
        }

        string cut = text[..63];
        if (char.IsHighSurrogate(cut[^1]))
        {
            cut = cut[..^1];
        }

        return cut;
    }

    public void Dispose()
    {
        try
        {
            _state.Changed -= Refresh;
        }
        catch (Exception)
        {
        }

        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            _menu.Dispose();
        }
        catch (Exception)
        {
        }
    }
}
