using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class UpdateStateStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "pps-update-state-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WithoutAFile_ReturnsNoCheckTime()
    {
        var store = new UpdateStateStore(_directory);

        UpdateState state = store.Load();

        Assert.Null(state.LastCheckUtc);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheCheckTime()
    {
        var store = new UpdateStateStore(_directory);
        var saved = new UpdateState(new DateTimeOffset(2026, 11, 11, 9, 30, 0, TimeSpan.Zero));

        store.Save(saved);
        UpdateState loaded = store.Load();

        Assert.Equal(saved.LastCheckUtc, loaded.LastCheckUtc);
    }

    [Fact]
    public void SaveTwice_LeavesALoadableFile()
    {
        var store = new UpdateStateStore(_directory);
        var first = new UpdateState(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        var second = new UpdateState(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));

        store.Save(first);
        store.Save(second);

        Assert.Equal(second.LastCheckUtc, store.Load().LastCheckUtc);
    }

    [Fact]
    public void Load_WithCorruptJson_ReturnsNoCheckTime()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "update.json"), "{ not json");

        var store = new UpdateStateStore(_directory);

        Assert.Null(store.Load().LastCheckUtc);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
        }
    }
}
