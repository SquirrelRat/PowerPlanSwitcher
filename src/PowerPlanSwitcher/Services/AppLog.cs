using System.Diagnostics;
using System.IO;
using PowerPlanSwitcher.Core;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Appends to one file per day through <see cref="LogFileStore"/>. Never throws:
/// a log problem must not take the app down.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static LogFileStore _store = CreateDefaultStore();

    /// <summary>Sets the log directory, creating it on the first write.</summary>
    public static void Configure(string directory)
    {
        try
        {
            lock (Gate)
            {
                _store = new LogFileStore(directory);
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down; keep the previous store.
        }
    }

    /// <summary>Full path of the current day's log.</summary>
    public static string LogPath
    {
        get
        {
            lock (Gate)
            {
                return _store.CurrentPath;
            }
        }
    }

    public static string LogDirectory
    {
        get
        {
            lock (Gate)
            {
                return _store.DirectoryPath;
            }
        }
    }

    public static IReadOnlyList<LogFileInfo> ListFiles()
    {
        lock (Gate)
        {
            return _store.ListFiles();
        }
    }

    public static string ReadTail(string path, int maxLines)
    {
        lock (Gate)
        {
            return _store.ReadTail(path, maxLines);
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    /// <summary>Opens the current day's log in whatever owns .log files.</summary>
    public static void Open() => OpenPath(LogPath);

    /// <summary>Opens one log file in whatever owns .log files.</summary>
    public static void OpenFile(string path) => OpenPath(path);

    /// <summary>Opens the log folder in Explorer.</summary>
    public static void OpenFolder() => OpenPath(LogDirectory);

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // No handler, or the file is gone. Nothing useful to do.
        }
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        try
        {
            lock (Gate)
            {
                _store.Append(line);
            }
        }
        catch (Exception)
        {
            // Nothing useful to do: the message is lost rather than fatal.
        }
    }

    private static LogFileStore CreateDefaultStore()
    {
        try
        {
            return new LogFileStore(AppPaths.LogDirectory);
        }
        catch (Exception)
        {
            return new LogFileStore(Path.Combine(Path.GetTempPath(), "PowerPlanSwitcher", "logs"));
        }
    }
}
