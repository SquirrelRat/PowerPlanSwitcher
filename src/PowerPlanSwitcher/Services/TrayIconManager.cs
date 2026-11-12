using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Theme;

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
    private readonly Icon _pausedIcon;
    private readonly AppState _state;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _autoItem;
    private readonly ToolStripMenuItem _forceMenu;
    private readonly ToolStripMenuItem _pauseHourItem;
    private readonly ToolStripMenuItem _pauseRestartItem;
    private readonly ToolStripMenuItem _resumeItem;
    private string? _lastBalloonedProblem;
    private string? _lastPlanKey;
    private readonly SynchronizationContext? _uiContext;
    private bool? _darkMenu;

    private static readonly Color DarkMenuText = Color.FromArgb(0xF2, 0xF2, 0xF4);

    public TrayIconManager(AppState state)
    {
        _state = state;

        _idleIcon = TrayIcons.Idle;
        _activeIcon = TrayIcons.Active;
        _boostIcon = TrayIcons.Boost;
        _pausedIcon = TrayIcons.Paused;

        _menu = new ContextMenuStrip();
        _uiContext = SynchronizationContext.Current;
        ApplyMenuTheme();

        _headerItem = new ToolStripMenuItem("Power Plan Switcher") { Enabled = false };
        _menu.Items.Add(_headerItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Settings...", () => OpenSettingsRequested?.Invoke()));
        _menu.Items.Add(Item("Show log", () => OpenLogRequested?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());

        _autoItem = new ToolStripMenuItem("Automatic", null, (_, _) => ForcePlanRequested?.Invoke(null));
        _menu.Items.Add(_autoItem);

        _forceMenu = new ToolStripMenuItem("Force plan");
        _menu.Items.Add(_forceMenu);

        _menu.Items.Add(new ToolStripSeparator());

        _pauseHourItem = Item("Pause for 1 hour", () => PauseRequested?.Invoke(TimeSpan.FromHours(1)));
        _pauseRestartItem = Item("Pause until restart", () => PauseRequested?.Invoke(null));
        _resumeItem = Item("Resume", () => ResumeRequested?.Invoke());
        _menu.Items.Add(_pauseHourItem);
        _menu.Items.Add(_pauseRestartItem);
        _menu.Items.Add(_resumeItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Open log folder", AppLog.OpenFolder));
        _menu.Items.Add(Item("Exit", () => ExitRequested?.Invoke()));

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

        _state.Changed += Refresh;

        // The window re-themes live; follow along so the menu never disagrees
        // with it after a mid-session theme change.
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        Refresh();
    }

    /// <summary>Raised when the user asks to see the settings window.</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>Raised when the user asks to see the log pane.</summary>
    public event Action? OpenLogRequested;

    /// <summary>Raised with a plan to force, or null for automatic.</summary>
    public event Action<Guid?>? ForcePlanRequested;

    /// <summary>Raised with a duration, or null for "until restart".</summary>
    public event Action<TimeSpan?>? PauseRequested;

    public event Action? ResumeRequested;

    public event Action? ExitRequested;

    /// <summary>A one-time hint that the app lives in the notification area.</summary>
    public void ShowWelcome()
    {
        try
        {
            _icon.ShowBalloonTip(
                6000,
                "Power Plan Switcher",
                "Running in the notification area. Left-click the icon for settings.",
                ToolTipIcon.Info);
        }
        catch (Exception)
        {
        }
    }

    private ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        item.ForeColor = _menu.ForeColor;
        return item;
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Accessibility
            or UserPreferenceCategory.Color))
        {
            return;
        }

        // SystemEvents raises on its own thread; the menu belongs to the UI thread.
        if (_uiContext is null)
        {
            ApplyMenuTheme();
            return;
        }

        _uiContext.Post(_ => ApplyMenuTheme(), null);
    }

    /// <summary>Matches the menu renderer and text color to the current theme.</summary>
    private void ApplyMenuTheme()
    {
        bool dark = ThemeManager.UseDarkSurfaces();

        if (dark == _darkMenu)
        {
            return;
        }

        _darkMenu = dark;

        if (dark)
        {
            _menu.Renderer = new DarkMenuRenderer();
            _menu.ForeColor = DarkMenuText;
        }
        else
        {
            _menu.Renderer = null;
            _menu.ForeColor = SystemColors.ControlText;
        }

        RecolorMenuItems();
    }

    private void RecolorMenuItems()
    {
        Color text = _menu.ForeColor;

        foreach (ToolStripItem item in _menu.Items)
        {
            item.ForeColor = text;
        }

        if (_forceMenu is not null)
        {
            foreach (ToolStripItem item in _forceMenu.DropDownItems)
            {
                item.ForeColor = text;
            }
        }
    }

    private void Refresh()
    {
        try
        {
            _icon.Text = BuildTooltip();
            _headerItem.Text = BuildHeaderText();
            _icon.Icon = PickIcon();

            _autoItem.Checked = _state.ForcedPlan is null;
            _pauseHourItem.Enabled = !_state.Paused;
            _pauseRestartItem.Enabled = !_state.Paused;
            _resumeItem.Enabled = _state.Paused;

            RebuildForceMenuIfNeeded();
            UpdateForceChecks();

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

    private void RebuildForceMenuIfNeeded()
    {
        string key = _state.PlansKey;

        if (key == _lastPlanKey)
        {
            return;
        }

        _lastPlanKey = key;
        _forceMenu.DropDownItems.Clear();

        if (_state.Plans.Count == 0)
        {
            _forceMenu.DropDownItems.Add(new ToolStripMenuItem("No plans found") { Enabled = false });
            return;
        }

        foreach (PowerPlanInfo plan in _state.Plans)
        {
            var item = new ToolStripMenuItem(plan.Name)
            {
                Tag = plan.Guid,
                ForeColor = _menu.ForeColor,
            };
            item.Click += (_, _) => ForcePlanRequested?.Invoke(plan.Guid);
            _forceMenu.DropDownItems.Add(item);
        }
    }

    private void UpdateForceChecks()
    {
        foreach (ToolStripItem item in _forceMenu.DropDownItems)
        {
            if (item is ToolStripMenuItem menuItem && menuItem.Tag is Guid guid)
            {
                menuItem.Checked = _state.ForcedPlan == guid;
            }
        }
    }

    private Icon PickIcon()
    {
        if (_state.Paused)
        {
            return _pausedIcon;
        }

        if (_state.ForcedPlan is not null)
        {
            return _boostIcon;
        }

        return _state.Target?.Activity == Core.Activity.Idle ? _idleIcon : _activeIcon;
    }

    private string BuildHeaderText()
    {
        string plan = _state.ActivePlan?.Name ?? "no plan active";

        if (_state.Paused)
        {
            return $"{plan} (paused)";
        }

        string mode = _state.ForcedPlan is null ? "auto" : "forced";

        if (_state.Target is null)
        {
            return $"{plan} ({mode})";
        }

        string activity = _state.Target.Activity == Core.Activity.Idle ? "idle" : "active";
        string power = _state.Target.PowerSource switch
        {
            Core.PowerSource.Battery => "on battery",
            Core.PowerSource.Ac => "on AC",
            _ => "power source unknown",
        };

        return $"{plan}: {activity}, {power} ({mode})";
    }

    private string BuildTooltip()
    {
        string text = BuildHeaderText();

        // NotifyIcon.Text allows 127 characters on current Windows.
        if (text.Length <= 127)
        {
            return text;
        }

        string cut = text[..127];
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
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        }
        catch (Exception)
        {
        }

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

    /// <summary>Paints the tray menu in the same dark palette as the window.</summary>
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? Color.FromArgb(0xF2, 0xF2, 0xF4)
                : Color.FromArgb(0x93, 0x96, 0x9E);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false
                ? Color.FromArgb(0x93, 0x96, 0x9E)
                : Color.FromArgb(0xF2, 0xF2, 0xF4);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle box = e.ImageRectangle;
            if (box.Width <= 0 || box.Height <= 0)
            {
                base.OnRenderItemCheck(e);
                return;
            }

            using var pen = new Pen(Color.FromArgb(0x4C, 0x9B, 0xE8), 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new Point(box.Left + 3, box.Top + (box.Height / 2)),
                new Point(box.Left + (box.Width / 2), box.Bottom - 3),
                new Point(box.Right - 2, box.Top + 3),
            });
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(0x2A, 0x2A, 0x2E);
        private static readonly Color SurfaceHover = Color.FromArgb(0x33, 0x34, 0x3A);

        public override Color ToolStripDropDownBackground => Surface;

        public override Color ImageMarginGradientBegin => Surface;

        public override Color ImageMarginGradientMiddle => Surface;

        public override Color ImageMarginGradientEnd => Surface;

        public override Color MenuBorder => Color.FromArgb(0x3A, 0x3B, 0x41);

        public override Color MenuItemBorder => Color.FromArgb(0x3A, 0x3B, 0x41);

        public override Color MenuItemSelected => SurfaceHover;

        public override Color MenuItemSelectedGradientBegin => SurfaceHover;

        public override Color MenuItemSelectedGradientEnd => SurfaceHover;

        public override Color CheckBackground => SurfaceHover;

        public override Color CheckSelectedBackground => SurfaceHover;

        public override Color SeparatorDark => Color.FromArgb(0x3A, 0x3B, 0x41);

        public override Color SeparatorLight => Color.FromArgb(0x3A, 0x3B, 0x41);
    }
}
