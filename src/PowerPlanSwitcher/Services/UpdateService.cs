using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Checks GitHub for a newer release and installs it on request. Every
/// network call and file move is best effort: a failure sets a short status
/// line and nothing else happens.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/SquirrelRat/PowerPlanSwitcher/releases/latest";
    private const string DownloadFileName = "update-download.exe";

    private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromHours(24);

    private static HttpClient? _sharedClient;

    private readonly AppState _state;
    private readonly string _dataDirectory;
    private readonly Func<DateTimeOffset> _now;
    private readonly HttpClient _client;
    private readonly Dispatcher _dispatcher;
    private readonly UpdateStateStore _store;
    private readonly Func<string?> _exePath;
    private bool _applying;

    public UpdateService(
        AppState state,
        string dataDirectory,
        Func<DateTimeOffset>? now = null,
        HttpClient? client = null,
        Dispatcher? dispatcher = null,
        Func<string?>? exePath = null)
    {
        _state = state;
        _dataDirectory = dataDirectory;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _client = client ?? (_sharedClient ??= CreateClient());
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _store = new UpdateStateStore(dataDirectory);
        _exePath = exePath ?? (() => Environment.ProcessPath);
    }

    /// <summary>
    /// The exe to start after the app exits, set by a successful
    /// <see cref="ApplyAsync"/>. Starting it earlier would trip the
    /// single-instance check against the process still shutting down.
    /// </summary>
    public string? RestartPath { get; private set; }

    /// <summary>
    /// Looks for a newer release when the user allows it and the last check
    /// is over 24 hours old. Never throws.
    /// </summary>
    public async Task CheckAsync()
    {
        try
        {
            if (!_state.Config.CheckForUpdates)
            {
                return;
            }

            DateTimeOffset now = _now();
            DateTimeOffset? last = _store.Load().LastCheckUtc;

            if (last is not null && now - last.Value < MinimumCheckInterval)
            {
                return;
            }

            _store.Save(new UpdateState(now));

            string json = await _client.GetStringAsync(LatestReleaseUrl).ConfigureAwait(false);

            ReleaseAssets? release = ReleaseFeed.Parse(json);

            if (release is null
                || !UpdateVersion.TryParse(release.Tag, out Version? available))
            {
                OnUi(() =>
                {
                    _state.PendingUpdate = null;
                    _state.UpdateStatus = null;
                    _state.NotifyChanged();
                });
                return;
            }

            bool newer = UpdateVersion.IsNewer(release.Tag, AppInfo.Version);

            try
            {
                AppLog.Info(newer
                    ? "update available: " + release.Tag
                    : "update check: up to date");
            }
            catch (Exception)
            {
            }

            OnUi(() =>
            {
                _state.PendingUpdate = newer
                    ? new PendingUpdate(available.ToString(), release.ExeUrl, release.HashUrl, release.ReleasePageUrl)
                    : null;
                _state.UpdateStatus = null;
                _state.NotifyChanged();
            });
        }
        catch (Exception)
        {
            // Offline, GitHub down, or anything else: quiet, try tomorrow.
            OnUi(() =>
            {
                _state.UpdateStatus = "Could not check for updates; will try again tomorrow.";
                _state.NotifyChanged();
            });
        }
    }

    /// <summary>
    /// Downloads the pending update, checks its hash, and swaps it in.
    /// Returns true when the new version is in place and the app should
    /// restart; <see cref="RestartPath"/> names the exe to start on the way
    /// out. Never throws.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        PendingUpdate? update = _state.PendingUpdate;

        if (update is null || _applying)
        {
            return false;
        }

        _applying = true;

        string downloadPath = Path.Combine(_dataDirectory, DownloadFileName);
        string hashPath = downloadPath + ".sha256";

        try
        {
            OnUi(() =>
            {
                _state.UpdateStatus = "Downloading update...";
                _state.NotifyChanged();
            });

            byte[] exeBytes = await _client.GetByteArrayAsync(update.ExeUrl).ConfigureAwait(false);
            string hashText = await _client.GetStringAsync(update.HashUrl).ConfigureAwait(false);

            if (!HashCheck.Verify(hashText, exeBytes))
            {
                DeleteQuietly(downloadPath);
                DeleteQuietly(hashPath);
                OnUi(() =>
                {
                    _state.UpdateStatus = "The downloaded update did not match its checksum; nothing was changed.";
                    _state.NotifyChanged();
                });
                return false;
            }

            Directory.CreateDirectory(_dataDirectory);
            await File.WriteAllBytesAsync(downloadPath, exeBytes).ConfigureAwait(false);

            string? exePath = _exePath();

            if (exePath is null)
            {
                OnUi(() =>
                {
                    _state.UpdateStatus = "The update could not be installed; opening the release page instead.";
                    _state.NotifyChanged();
                });
                OpenPage(update.ReleasePageUrl);
                return false;
            }

            string targetPath = exePath;

            string oldPath = exePath + ".old";

            try
            {
                File.Move(exePath, oldPath, overwrite: true);
            }
            catch (Exception)
            {
                // The exe sits somewhere we cannot rename (for example
                // Program Files): point the user at the download instead.
                DeleteQuietly(downloadPath);
                OnUi(() =>
                {
                    _state.UpdateStatus = "The app cannot update itself here; opening the release page instead.";
                    _state.NotifyChanged();
                });
                OpenPage(update.ReleasePageUrl);
                return false;
            }

            try
            {
                File.Move(downloadPath, exePath);
            }
            catch (Exception)
            {
                // Put the old exe back before giving up, so nothing is lost.
                try
                {
                    File.Move(oldPath, exePath, overwrite: true);
                }
                catch (Exception)
                {
                    // Both moves failed: the install needs manual repair, and
                    // saying "nothing was changed" would hide that.
                    try
                    {
                        AppLog.Error(
                            "update swap failed halfway; rename " + oldPath + " back to " + exePath +
                            " by hand to repair the install");
                    }
                    catch (Exception)
                    {
                    }

                    OnUi(() =>
                    {
                        _state.UpdateStatus =
                            "The update failed halfway. Open the settings folder and rename " +
                            Path.GetFileName(oldPath) + " back to " + Path.GetFileName(targetPath) +
                            " to repair the install.";
                        _state.NotifyChanged();
                    });
                    return false;
                }

                DeleteQuietly(downloadPath);
                OnUi(() =>
                {
                    _state.UpdateStatus = "The update could not be installed; nothing was changed.";
                    _state.NotifyChanged();
                });
                return false;
            }

            OnUi(() =>
            {
                _state.UpdateStatus = $"Updated to {update.Version}; restarting.";
                _state.NotifyChanged();
            });

            try
            {
                AppLog.Info("update installed: " + update.Version);
            }
            catch (Exception)
            {
            }

            RestartPath = exePath;

            return true;
        }
        catch (Exception)
        {
            DeleteQuietly(downloadPath);
            DeleteQuietly(hashPath);
            OnUi(() =>
            {
                _state.UpdateStatus = "The update could not be downloaded; will try again when you click update.";
                _state.NotifyChanged();
            });
            return false;
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Removes the leftover exe from a previous self-update.</summary>
    public static void TryDeleteOldExe()
    {
        try
        {
            string? exePath = Environment.ProcessPath;

            if (exePath is not null)
            {
                DeleteQuietly(exePath + ".old");
            }
        }
        catch (Exception)
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerPlanSwitcher/" + AppInfo.Version);

        return client;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    /// <summary>State is single threaded on the UI thread; keep writes there.</summary>
    private void OnUi(Action action)
    {
        try
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.BeginInvoke(action);
        }
        catch (Exception)
        {
        }
    }
}
