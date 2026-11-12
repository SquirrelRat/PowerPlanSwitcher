using System.IO;
using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;
using PowerPlanSwitcher.Settings;

namespace PowerPlanSwitcher.App.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private static readonly Guid Missing = new("11111111-1111-1111-1111-111111111111");
    private readonly string _logDirectory;

    public SettingsViewModelTests()
    {
        _logDirectory = Path.Combine(
            Path.GetTempPath(),
            "pps-app-tests-" + Guid.NewGuid().ToString("n"));
        AppLog.Configure(_logDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
        catch
        {
            // best effort cleanup in a temp directory
        }
    }

    private sealed class FakeMonitor : IPlanMonitor
    {
        public int Calls { get; private set; }

        public void RunNow() => Calls++;
    }

    private static (AppState State, SettingsViewModel ViewModel, FakeMonitor Monitor) Create()
    {
        var state = new AppState
        {
            Config = new AppConfig(),
            Plans = new[]
            {
                new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced"),
                new PowerPlanInfo(WellKnownPlans.PowerSaver, "Power saver"),
            },
            ActivePlan = new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced"),
            Target = new PlanTarget(
                WellKnownPlans.Balanced,
                Activity.Active,
                PowerSource.Ac,
                "user active",
                false),
            HasBattery = true,
        };

        var monitor = new FakeMonitor();
        var viewModel = new SettingsViewModel(state, monitor);
        viewModel.RefreshFromState();
        return (state, viewModel, monitor);
    }

    [Fact]
    public void CheckForUpdates_RoundTripsIntoTheConfig()
    {
        var (state, viewModel, _) = Create();

        Assert.True(viewModel.CheckForUpdates);

        viewModel.CheckForUpdates = false;

        Assert.False(state.Config.CheckForUpdates);
    }

    [Fact]
    public void UpdateLine_ShowsThePendingVersionAndFollowsState()
    {
        var (state, viewModel, _) = Create();

        Assert.Contains("up to date", viewModel.UpdateLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanUpdate);

        state.PendingUpdate = new PendingUpdate("0.9.9", "exe", "hash", "page");
        viewModel.RefreshFromState();

        Assert.Contains("0.9.9", viewModel.UpdateLine);
        Assert.True(viewModel.CanUpdate);

        state.PendingUpdate = null;
        state.UpdateStatus = "Downloading update";
        viewModel.RefreshFromState();

        Assert.Contains("Downloading update", viewModel.UpdateNote);
        Assert.True(viewModel.HasUpdateNote);
    }

    [Fact]
    public void MissingPlan_IsShownAsMissing_NotAsNotSet()
    {
        var (state, viewModel, _) = Create();
        state.Config.Rules.Set(Activity.Active, PowerSource.Ac, Missing);

        viewModel.RefreshFromState();

        Assert.Equal(Missing, viewModel.SelectedActiveAc?.Guid);
        Assert.Equal("Missing plan", viewModel.SelectedActiveAc?.Name);
    }

    [Fact]
    public void CommitRules_WritesAllFourCellsAndRunsTheMonitor()
    {
        var (state, viewModel, monitor) = Create();
        viewModel.ActiveAcPlan = WellKnownPlans.Balanced;
        viewModel.ActiveBatteryPlan = WellKnownPlans.PowerSaver;
        viewModel.IdleAcPlan = WellKnownPlans.PowerSaver;
        viewModel.IdleBatteryPlan = null;

        bool saved = viewModel.CommitRules();

        Assert.True(saved);
        Assert.Equal(WellKnownPlans.Balanced, state.Config.Rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Equal(WellKnownPlans.PowerSaver, state.Config.Rules.Get(Activity.Active, PowerSource.Battery));
        Assert.Equal(WellKnownPlans.PowerSaver, state.Config.Rules.Get(Activity.Idle, PowerSource.Ac));
        Assert.Null(state.Config.Rules.Get(Activity.Idle, PowerSource.Battery));
        Assert.Equal(1, monitor.Calls);
    }

    [Fact]
    public void ReturnToAutomatic_ClearsTheForcedPlan()
    {
        var (state, viewModel, monitor) = Create();
        state.ForcedPlan = WellKnownPlans.HighPerformance;
        viewModel.RefreshFromState();
        Assert.True(viewModel.IsForced);

        viewModel.ReturnToAutomatic();

        Assert.Null(state.ForcedPlan);
        Assert.False(viewModel.IsForced);
        Assert.Equal(1, monitor.Calls);
    }

    [Fact]
    public void StatusLine_ShowsPaused()
    {
        var (state, viewModel, _) = Create();
        state.Pause(null);

        viewModel.RefreshFromState();

        Assert.EndsWith("(paused)", viewModel.StatusLine);
        Assert.Equal("Paused", viewModel.ModeLabel);
        Assert.True(viewModel.IsPaused);
        Assert.False(viewModel.CanPause);
    }

    [Fact]
    public void StatusDetail_UnknownPowerSource_IsReadable()
    {
        var (state, viewModel, _) = Create();
        state.Target = new PlanTarget(
            null,
            Activity.Idle,
            PowerSource.Unknown,
            "power source unknown",
            false);

        viewModel.RefreshFromState();

        Assert.Contains("power source unknown", viewModel.StatusDetail);
        Assert.DoesNotContain("  ", viewModel.StatusDetail);
    }

    [Fact]
    public void RecentChanges_AreNewestFirst()
    {
        var (state, viewModel, _) = Create();
        state.AddHistory(new SwitchRecord(
            DateTimeOffset.Now.AddMinutes(-1), "first", Activity.Active, PowerSource.Ac, "test", false));
        state.AddHistory(new SwitchRecord(
            DateTimeOffset.Now, "second", Activity.Idle, PowerSource.Battery, "test", false));

        viewModel.RefreshFromState();

        Assert.Equal("second", viewModel.RecentChanges[0].PlanName);
        Assert.Equal("first", viewModel.RecentChanges[1].PlanName);
    }

    [Fact]
    public void ShowBattery_FollowsTheLiveState()
    {
        var (state, viewModel, _) = Create();
        state.HasBattery = false;

        viewModel.RefreshFromState();

        Assert.False(viewModel.ShowBattery);
    }
}
