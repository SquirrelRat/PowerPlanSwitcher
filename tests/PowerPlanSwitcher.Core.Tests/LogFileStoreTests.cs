using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class LogFileStoreTests : IDisposable
{
    private readonly string _directory;
    private DateTime _now = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Local);

    public LogFileStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "pps-log-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
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

    private LogFileStore CreateStore() =>
        new(_directory, () => _now, retentionDays: 3, maxBytes: 120);

    [Fact]
    public void Append_WritesToTheDatedFile()
    {
        var store = CreateStore();

        store.Append("hello");

        string path = Path.Combine(_directory, "app-2026-11-09.log");
        Assert.True(File.Exists(path));
        Assert.Contains("hello", File.ReadAllText(path));
    }

    [Fact]
    public void Append_AfterMidnight_StartsANewFile()
    {
        var store = CreateStore();
        store.Append("before");

        _now = _now.AddDays(1);
        store.Append("after");

        string before = Path.Combine(_directory, "app-2026-11-09.log");
        string after = Path.Combine(_directory, "app-2026-11-10.log");
        Assert.Contains("before", File.ReadAllText(before));
        Assert.DoesNotContain("after", File.ReadAllText(before));
        Assert.Contains("after", File.ReadAllText(after));
    }

    [Fact]
    public void Append_PrunesFilesOlderThanRetention()
    {
        var store = CreateStore();
        store.Append("old");

        _now = _now.AddDays(4);
        store.Append("new");

        Assert.False(File.Exists(Path.Combine(_directory, "app-2026-11-09.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "app-2026-11-13.log")));
    }

    [Fact]
    public void ListFiles_ReturnsNewestFirst()
    {
        var store = CreateStore();
        store.Append("one");
        _now = _now.AddDays(1);
        store.Append("two");

        var files = store.ListFiles();

        Assert.Equal(2, files.Count);
        Assert.Equal(new DateTime(2026, 11, 10), files[0].Date);
        Assert.Equal(new DateTime(2026, 11, 9), files[1].Date);
    }

    [Fact]
    public void ReadTail_ReturnsTheLastLines()
    {
        var store = CreateStore();
        store.Append("one");
        store.Append("two");
        store.Append("three");

        string text = store.ReadTail(store.CurrentPath, 2);

        Assert.Equal("two" + Environment.NewLine + "three", text);
    }

    [Fact]
    public void ReadTail_MissingFile_ReturnsEmpty()
    {
        var store = CreateStore();

        Assert.Equal(string.Empty, store.ReadTail(store.CurrentPath, 10));
    }

    [Fact]
    public void Append_RollsTheFileAtTheSizeCap()
    {
        var store = CreateStore();
        store.Append(new string('x', 200));
        store.Append("second");

        string path = store.CurrentPath;
        Assert.True(File.Exists(path + ".1"));
        Assert.Contains("second", File.ReadAllText(path));
        Assert.DoesNotContain("second", File.ReadAllText(path + ".1"));
    }

    [Fact]
    public void ListFiles_IncludesTheRolledPart()
    {
        var store = CreateStore();
        store.Append(new string('x', 200));
        store.Append("second");

        var files = store.ListFiles();

        Assert.Equal(2, files.Count);
        Assert.Equal(0, files[0].Part);
        Assert.Equal(1, files[1].Part);
        Assert.Contains("part 2", files[1].ToString());
    }
}
