using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly ConfigStore _store;

    public ConfigStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "pps-config-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _store = new ConfigStore(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best effort cleanup in a temp directory
        }
    }

    [Fact]
    public void MissingFile_LoadsDefaults()
    {
        var config = _store.Load();

        Assert.Equal(1, config.Version);
        Assert.False(config.StartWithWindows);
        Assert.Equal(5, config.Idle.InputTimeoutMinutes);
        Assert.Empty(config.Rules.UnsetSlots);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var config = new AppConfig
        {
            StartWithWindows = false,
        };
        config.Idle.InputTimeoutMinutes = 12;
        config.Idle.OnScreensaver = false;
        config.Rules.Set(Activity.Idle, PowerSource.Battery, new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));

        _store.Save(config);
        var loaded = _store.Load();

        Assert.False(loaded.StartWithWindows);
        Assert.Equal(12, loaded.Idle.InputTimeoutMinutes);
        Assert.False(loaded.Idle.OnScreensaver);
        Assert.True(loaded.Idle.OnLock);
        Assert.Equal(
            new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
            loaded.Rules.Get(Activity.Idle, PowerSource.Battery));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        _store.Save(new AppConfig());

        Assert.Single(Directory.GetFiles(_directory, "settings.json"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CorruptFile_LoadsDefaults_AndRenamesTheBadFile_AndReportsAProblem()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ this is not json");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.Equal(5, config.Idle.InputTimeoutMinutes);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json.bad")));
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
        Assert.False(string.IsNullOrEmpty(reported));
    }

    [Fact]
    public void UnknownFields_AreIgnored_AndSurviveASave()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"version":1,"someFutureField":42,"idle":{"inputTimeoutMinutes":3}}""");

        var config = _store.Load();

        Assert.Equal(3, config.Idle.InputTimeoutMinutes);

        _store.Save(config);
        var reloaded = _store.Load();

        Assert.Equal(3, reloaded.Idle.InputTimeoutMinutes);
        Assert.True(reloaded.ExtensionData?.ContainsKey("someFutureField") == true);
    }

    [Fact]
    public void MissingFields_TakeTheirDefaults()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"startWithWindows":false}""");

        var config = _store.Load();

        Assert.False(config.StartWithWindows);
        Assert.Equal(5, config.Idle.InputTimeoutMinutes);
        Assert.True(config.Idle.OnScreensaver);
    }

    [Fact]
    public void OutOfRangeTimeout_IsClampedOnLoad()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"idle":{"inputTimeoutMinutes":9999}}""");

        var config = _store.Load();

        Assert.Equal(120, config.Idle.InputTimeoutMinutes);
    }

    [Fact]
    public void UnsetCell_RoundTripsAsNull()
    {
        var config = new AppConfig();
        config.Rules.Set(Activity.Active, PowerSource.Ac, null);

        _store.Save(config);
        var loaded = _store.Load();

        Assert.Null(loaded.Rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Contains(PlanSlot.AcActive, loaded.Rules.UnsetSlots);
        Assert.Equal(
            new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
            loaded.Rules.Get(Activity.Idle, PowerSource.Ac));
    }

    [Fact]
    public void ExplicitNullSections_FallBackToDefaults()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"rules":null,"idle":null}""");

        var config = _store.Load();

        Assert.NotNull(config.Rules);
        Assert.NotNull(config.Idle);
        Assert.Empty(config.Rules.UnsetSlots);
        Assert.Equal(5, config.Idle.InputTimeoutMinutes);
    }

    [Fact]
    public void ScanInterval_RoundTrips()
    {
        var config = new AppConfig();
        config.ScanIntervalSeconds = 10;

        _store.Save(config);
        var loaded = _store.Load();

        Assert.Equal(10, loaded.ScanIntervalSeconds);
    }

    [Fact]
    public void MissingScanInterval_TakesTheDefault()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"version":1}""");

        var config = _store.Load();

        Assert.Equal(2, config.ScanIntervalSeconds);
    }

    [Fact]
    public void NullScalar_KeepsOtherFields_AndUsesFieldDefault()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"startWithWindows":false,"scanIntervalSeconds":null}""");

        var config = _store.Load();

        Assert.False(config.StartWithWindows);
        Assert.Equal(2, config.ScanIntervalSeconds);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.bad")));
    }

    [Fact]
    public void MissingFile_WritesDefaultsToDisk()
    {
        var config = _store.Load();

        Assert.Equal(5, config.Idle.InputTimeoutMinutes);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public void SingleBadGuidCell_DegradesToUnset_NotWholeFileReset()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"rules":{"acActive":"not-a-guid","acIdle":"a1841308-3541-4fab-bc81-f71556f20b4a"}}""");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.Null(config.Rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Equal(
            new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
            config.Rules.Get(Activity.Idle, PowerSource.Ac));
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.bad")));
        Assert.NotNull(reported);
        Assert.Contains("acActive", reported);
    }

    [Fact]
    public void NestedSpecShape_IsAccepted()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"rules":{"active":{"ac":"381b4222-f694-41f0-9685-ff5bb260df2e","battery":"a1841308-3541-4fab-bc81-f71556f20b4a"},"idle":{"ac":"a1841308-3541-4fab-bc81-f71556f20b4a","battery":null}}}""");

        var config = _store.Load();

        Assert.Equal(
            new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
            config.Rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Null(config.Rules.Get(Activity.Idle, PowerSource.Battery));
    }

    [Fact]
    public void Version_IsPreservedAsMigrationSignal()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"version":7}""");

        var config = _store.Load();

        Assert.Equal(7, config.Version);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9999, 60)]
    public void ScanInterval_LowAndHighClampOnLoad(int input, int expected)
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"scanIntervalSeconds\":" + input + "}");

        var config = _store.Load();

        Assert.Equal(expected, config.ScanIntervalSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9999, 120)]
    public void IdleTimeout_LowAndHighClampOnLoad(int input, int expected)
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            "{\"idle\":{\"inputTimeoutMinutes\":" + input + "}}");

        var config = _store.Load();

        Assert.Equal(expected, config.Idle.InputTimeoutMinutes);
    }

    [Fact]
    public void ThrowingProblemListener_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ broken");
        _store.ProblemReported += m => throw new InvalidOperationException("listener boom");

        var ex = Record.Exception(() => _store.Load());

        Assert.Null(ex);
    }

    [Fact]
    public void CheckForUpdates_LoadsFalseAndRoundTrips()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"checkForUpdates":false}""");

        var config = _store.Load();

        Assert.False(config.CheckForUpdates);
        _store.Save(config);
        Assert.False(_store.Load().CheckForUpdates);
    }

    [Fact]
    public void CheckForUpdates_MissingFieldDefaultsToOnWithoutAProblem()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"startWithWindows":false}""");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.True(config.CheckForUpdates);
        Assert.Null(reported);
    }

    [Fact]
    public void CheckForUpdates_WrongTypeReportsTheFieldAndKeepsTheDefault()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"checkForUpdates":"yes"}""");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.True(config.CheckForUpdates);
        Assert.NotNull(reported);
        Assert.Contains("checkForUpdates", reported);
    }

    [Fact]
    public void WrongScalarType_KeepsTheOtherSettings()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"version":1,"startWithWindows":false,"scanIntervalSeconds":"10","idle":{"inputTimeoutMinutes":3}}""");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.False(config.StartWithWindows);
        Assert.Equal(2, config.ScanIntervalSeconds);
        Assert.Equal(3, config.Idle.InputTimeoutMinutes);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.bad")));
        Assert.NotNull(reported);
        Assert.Contains("scanIntervalSeconds", reported);
    }

    [Fact]
    public void UnknownSectionFields_SurviveASave()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"version":1,"someFutureField":42,"rules":{"acActive":"381b4222-f694-41f0-9685-ff5bb260df2e","futureRule":"keep"},"idle":{"onLock":false,"futureFlag":true}}""");

        var config = _store.Load();
        Assert.False(config.Idle.OnLock);

        _store.Save(config);
        string saved = File.ReadAllText(Path.Combine(_directory, "settings.json"));

        Assert.Contains("someFutureField", saved);
        Assert.Contains("futureRule", saved);
        Assert.Contains("futureFlag", saved);
    }

    [Fact]
    public void NewerVersion_LoadsKnownFieldsAndReports()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"version":7,"startWithWindows":false}""");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.Equal(7, config.Version);
        Assert.False(config.StartWithWindows);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.bad")));
        Assert.NotNull(reported);
        Assert.Contains("newer", reported, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrongTypedRuleCell_IsUnsetAndReported()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"rules":{"acActive":123,"acIdle":"a1841308-3541-4fab-bc81-f71556f20b4a"}}""");
        string? reported = null;
        _store.ProblemReported += m => reported = m;

        var config = _store.Load();

        Assert.Null(config.Rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Equal(
            new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
            config.Rules.Get(Activity.Idle, PowerSource.Ac));
        Assert.NotNull(reported);
        Assert.Contains("acActive", reported);
    }

    [Fact]
    public void NestedRulesOverrideFlatRules()
    {
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"rules":{"acActive":"381b4222-f694-41f0-9685-ff5bb260df2e","active":{"ac":"a1841308-3541-4fab-bc81-f71556f20b4a"}}}""");

        var config = _store.Load();

        Assert.Equal(
            new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
            config.Rules.Get(Activity.Active, PowerSource.Ac));
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        string path = Path.Combine(_directory, "blocked");
        File.WriteAllText(path, "not a directory");
        string? reported = null;
        var store = new ConfigStore(path);
        store.ProblemReported += m => reported = m;

        store.Save(new AppConfig());

        Assert.NotNull(reported);
        Assert.Contains("saved", reported, StringComparison.OrdinalIgnoreCase);
    }
}
