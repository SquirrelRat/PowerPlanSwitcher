using System.IO;
using System.Net;
using System.Net.Http;
using PowerPlanSwitcher.Core;
using PowerPlanSwitcher.Platform;
using PowerPlanSwitcher.Services;

namespace PowerPlanSwitcher.App.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _logDirectory;

    public UpdateServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pps-update-service-" + Guid.NewGuid().ToString("n"));
        _logDirectory = Path.Combine(_root, "logs");
        Directory.CreateDirectory(_root);
        AppLog.Configure(_logDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    private static AppState CreateState(string exeUrl, string hashUrl) => new()
    {
        Config = new AppConfig(),
        Plans = new[]
        {
            new PowerPlanInfo(WellKnownPlans.Balanced, "Balanced"),
        },
        PendingUpdate = new PendingUpdate("9.9.9", exeUrl, hashUrl, "https://example.com/page"),
    };

    private static byte[] NewExeBytes => "the new version"u8.ToArray();

    private static string HashAssetFor(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant() + "  PowerPlanSwitcher.exe";

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly byte[] _exeBytes;
        private readonly string _hashText;
        private readonly TimeSpan _delay;

        public FakeHandler(byte[] exeBytes, string hashText, TimeSpan delay = default)
        {
            _exeBytes = exeBytes;
            _hashText = hashText;
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.EndsWith(".sha256", StringComparison.Ordinal))
            {
                return Text(_hashText);
            }

            return Bytes(_exeBytes);
        }

        private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(text),
        };

        private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
    }

    [Fact]
    public async Task ApplyAsync_SwapsTheExeAndKeepsTheOldOne()
    {
        string exePath = Path.Combine(_root, "app.exe");
        File.WriteAllText(exePath, "old version");
        var state = CreateState("https://example.com/exe", "https://example.com/exe.sha256");
        var service = new UpdateService(
            state,
            _root,
            client: new HttpClient(new FakeHandler(NewExeBytes, HashAssetFor(NewExeBytes))),
            exePath: () => exePath);

        bool applied = await service.ApplyAsync();

        Assert.True(applied);
        Assert.Equal("the new version", File.ReadAllText(exePath));
        Assert.Equal("old version", File.ReadAllText(exePath + ".old"));
        Assert.Equal(exePath, service.RestartPath);
        Assert.False(File.Exists(Path.Combine(_root, "update-download.exe")));
    }

    [Fact]
    public async Task ApplyAsync_BadHashCancelsAndLeavesTheExeAlone()
    {
        string exePath = Path.Combine(_root, "app.exe");
        File.WriteAllText(exePath, "old version");
        var state = CreateState("https://example.com/exe", "https://example.com/exe.sha256");
        var service = new UpdateService(
            state,
            _root,
            client: new HttpClient(new FakeHandler(NewExeBytes, "deadbeef  PowerPlanSwitcher.exe")),
            exePath: () => exePath);

        bool applied = await service.ApplyAsync();

        Assert.False(applied);
        Assert.Equal("old version", File.ReadAllText(exePath));
        Assert.False(File.Exists(exePath + ".old"));
        Assert.False(File.Exists(Path.Combine(_root, "update-download.exe")));
        Assert.Contains("checksum", state.UpdateStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_SecondCallWhileOneIsRunningIsIgnored()
    {
        string exePath = Path.Combine(_root, "app.exe");
        File.WriteAllText(exePath, "old version");
        var state = CreateState("https://example.com/exe", "https://example.com/exe.sha256");
        var service = new UpdateService(
            state,
            _root,
            client: new HttpClient(new FakeHandler(NewExeBytes, HashAssetFor(NewExeBytes), TimeSpan.FromMilliseconds(400))),
            exePath: () => exePath);

        Task<bool> first = Task.Run(() => service.ApplyAsync());
        await Task.Delay(100);
        Task<bool> second = Task.Run(() => service.ApplyAsync());

        bool secondResult = await second;

        Assert.False(secondResult);
        Assert.True(await first);
        Assert.Equal("the new version", File.ReadAllText(exePath));
    }
}
