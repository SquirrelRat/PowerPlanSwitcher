using System.IO;
using System.Text;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Appends to one log file and rolls it once at a size cap, keeping a single
/// older copy. Never throws: a log problem must not take the app down.
/// </summary>
public static class AppLog
{
    private const long RollAtBytes = 1024 * 1024;
    private const int MaxGenerations = 1;

    private static readonly object Gate = new();
    private static string _path = "app.log";

    /// <summary>Sets the log file location, creating the directory if needed.</summary>
    public static void Configure(string directory, string fileName = "app.log")
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                _path = Path.Combine(directory, fileName);
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down; keep the previous path.
        }
    }

    /// <summary>Full path of the current log.</summary>
    public static string LogPath
    {
        get
        {
            lock (Gate)
            {
                return _path;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    /// <summary>Opens the log in whatever program owns .log files.</summary>
    public static void Open()
    {
        try
        {
            string path = LogPath;
            if (!File.Exists(path))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // No handler for .log, or the file is gone. Nothing to do.
        }
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        lock (Gate)
        {
            try
            {
                RollIfNeeded();
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Nothing useful to do: the message is lost rather than fatal.
            }
        }
    }

    private static void RollIfNeeded()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var info = new FileInfo(_path);
            if (info.Length < RollAtBytes)
            {
                return;
            }

            string older = _path + ".1";

            try
            {
                if (File.Exists(older))
                {
                    File.Delete(older);
                }

                File.Move(_path, older);
            }
            catch (Exception)
            {
                // Keep writing to the current file if we cannot roll it.
            }
        }
        catch (Exception)
        {
            // Sizing the log must never throw.
        }
    }
}
