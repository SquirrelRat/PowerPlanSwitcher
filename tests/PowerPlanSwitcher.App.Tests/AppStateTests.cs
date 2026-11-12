using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;

namespace PowerPlanSwitcher.App.Tests;

public sealed class AppStateTests
{
    private static AppState Create() => new()
    {
        Config = new AppConfig(),
        Plans = new[]
        {
            new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced"),
            new PowerPlanInfo(WellKnownPlans.PowerSaver, "Power saver"),
        },
    };

    [Fact]
    public void PauseWithDuration_SetsAnExpiry()
    {
        var state = Create();

        state.Pause(TimeSpan.FromHours(1));

        Assert.True(state.Paused);
        Assert.NotNull(state.PausedUntil);
        Assert.False(state.IsPauseExpired(DateTimeOffset.Now));
    }

    [Fact]
    public void PauseWithoutDuration_LastsUntilRestart()
    {
        var state = Create();

        state.Pause(null);

        Assert.True(state.Paused);
        Assert.Null(state.PausedUntil);
        Assert.False(state.IsPauseExpired(DateTimeOffset.Now.AddDays(1)));
    }

    [Fact]
    public void PauseExpiresAfterItsTime()
    {
        var state = Create();
        state.Pause(TimeSpan.FromMinutes(5));

        Assert.True(state.IsPauseExpired(DateTimeOffset.Now.AddMinutes(6)));
    }

    [Fact]
    public void Resume_ClearsPauseAndExpiry()
    {
        var state = Create();
        state.Pause(TimeSpan.FromHours(1));

        state.Resume();

        Assert.False(state.Paused);
        Assert.Null(state.PausedUntil);
    }

    [Fact]
    public void ClearSwitchProblemOnly_LeavesSettingsProblemsAlone()
    {
        var state = Create();
        state.LastProblem = "settings could not be saved";
        state.LastProblemKind = ProblemKind.SettingsWriteFailed;

        state.ClearSwitchProblemOnly();

        Assert.NotNull(state.LastProblem);
        Assert.Equal(ProblemKind.SettingsWriteFailed, state.LastProblemKind);
    }

    [Fact]
    public void ClearSwitchProblemOnly_ClearsSwitchProblems()
    {
        var state = Create();
        state.LastProblem = "the plan is gone";
        state.LastProblemKind = ProblemKind.TargetMissing;

        state.ClearSwitchProblemOnly();

        Assert.Null(state.LastProblem);
        Assert.Null(state.LastProblemKind);
    }

    [Fact]
    public void ChangeSignature_TracksPlanNamesAndPause()
    {
        var state = Create();
        string before = state.GetChangeSignature();

        state.Plans = new[]
        {
            new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced renamed"),
        };
        string renamed = state.GetChangeSignature();

        state.Pause(null);
        string paused = state.GetChangeSignature();

        Assert.NotEqual(before, renamed);
        Assert.NotEqual(renamed, paused);
    }

    [Fact]
    public void PlansKey_ChangesWhenAPlanIsRenamedOrRemoved()
    {
        var state = Create();
        string before = state.PlansKey;

        state.Plans = new[]
        {
            new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced renamed"),
            new PowerPlanInfo(WellKnownPlans.PowerSaver, "Power saver"),
        };
        string renamed = state.PlansKey;

        state.Plans = new[] { new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced") };
        string removed = state.PlansKey;

        Assert.NotEqual(before, renamed);
        Assert.NotEqual(before, removed);
    }

    [Fact]
    public void ChangeSignature_TracksPendingUpdateAndStatus()
    {
        var state = Create();
        string before = state.GetChangeSignature();

        state.PendingUpdate = new PendingUpdate("0.3.1", "https://exe", "https://hash", "https://page");
        string pending = state.GetChangeSignature();

        state.UpdateStatus = "Downloading update";
        string downloading = state.GetChangeSignature();

        state.UpdateStatus = null;
        string settled = state.GetChangeSignature();

        Assert.NotEqual(before, pending);
        Assert.NotEqual(pending, downloading);
        Assert.Equal(pending, settled);
    }

    [Fact]
    public void History_KeepsTheNewestTwenty()
    {
        var state = Create();

        for (int i = 0; i < 25; i++)
        {
            state.AddHistory(new SwitchRecord(
                DateTimeOffset.Now,
                "plan " + i,
                Activity.Active,
                PowerSource.Ac,
                "test",
                false));
        }

        Assert.Equal(20, state.History.Count);
        Assert.Equal("plan 5", state.History[0].PlanName);
        Assert.Equal("plan 24", state.History[^1].PlanName);
    }
}
