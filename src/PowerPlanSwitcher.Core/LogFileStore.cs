using System.IO;
using System.Text;

namespace PowerPlanSwitcher.Core;

/// <summary>One daily log file on disk.</summary>
public sealed record LogFileInfo(DateTime Date, string Path, long Length, int Part = 0)
{
    public override string ToString() =>
        Part > 0 ? $"{Date:yyyy-MM-dd} (part {Part + 1})" : Date.ToString("yyyy-MM-dd");
}

/// <summary>
/// Writes one log file per day. Keeps a small number of days and a size cap
/// per day, so a noisy failure cannot fill the disk or wipe the history.
/// </summary>
public sealed class LogFileStore
{
    public const int DefaultRetentionDays = 14;
    public const long DefaultMaxBytes = 5L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Func<DateTime> _now;
    private readonly int _retentionDays;
    private readonly long _maxBytes;

    public LogFileStore(
        string directory,
        Func<DateTime>? now = null,
        int retentionDays = DefaultRetentionDays,
        long maxBytes = DefaultMaxBytes)
    {
        _directory = directory;
        _now = now ?? (() => DateTime.Now);
        _retentionDays = Math.Max(1, retentionDays);
        _maxBytes = Math.Max(1, maxBytes);
    }

    public string DirectoryPath => _directory;

    public string CurrentPath => PathFor(_now());

    public string PathFor(DateTime date) =>
        Path.Combine(_directory, $"app-{date:yyyy-MM-dd}.log");

    public void Append(string line)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            DateTime now = _now();
            PruneCore(now);

            string path = PathFor(now);
            RollIfTooLarge(path);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    public IReadOnlyList<LogFileInfo> ListFiles()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                return Array.Empty<LogFileInfo>();
            }

            var files = new List<LogFileInfo>();

            foreach (string path in Directory.GetFiles(_directory, "app-*.log*"))
            {
                if (!TryReadFile(path, out DateTime date, out int part))
                {
                    continue;
                }

                long length;
                try
                {
                    length = new FileInfo(path).Length;
                }
                catch (Exception)
                {
                    length = 0;
                }

                files.Add(new LogFileInfo(date, path, length, part));
            }

            return files
                .OrderByDescending(f => f.Date)
                .ThenBy(f => f.Part)
                .ToArray();
        }
    }

    public string ReadTail(string path, int maxLines)
    {
        lock (_gate)
        {
            if (maxLines <= 0 || !File.Exists(path))
            {
                return string.Empty;
            }

            var lines = new Queue<string>(maxLines);

            try
            {
                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (lines.Count == maxLines)
                    {
                        lines.Dequeue();
                    }

                    lines.Enqueue(line);
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public void Prune() => PruneCore(_now());

    private void PruneCore(DateTime now)
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DateTime oldest = now.Date.AddDays(-(_retentionDays - 1));

            foreach (string path in Directory.GetFiles(_directory, "app-*.log*"))
            {
                if (TryReadFile(path, out DateTime date, out _) && date.Date < oldest)
                {
                    TryDelete(path);
                }
            }
        }
        catch (Exception)
        {
            // Pruning is housekeeping. A failure must never stop logging.
        }
    }

    private void RollIfTooLarge(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            if (new FileInfo(path).Length < _maxBytes)
            {
                return;
            }

            string older = path + ".1";
            if (File.Exists(older))
            {
                File.Delete(older);
            }

            File.Move(path, older);
        }
        catch (Exception)
        {
            // Keep writing to the current file if it cannot be rolled.
        }
    }

    private static bool TryReadFile(string path, out DateTime date, out int part)
    {
        date = default;
        part = 0;

        string name = Path.GetFileName(path);

        if (name.Length < 14 || !name.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string text = name.Substring(4, 10);
        if (!DateTime.TryParseExact(
                text,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date))
        {
            return false;
        }

        int marker = name.IndexOf(".log.", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            int.TryParse(name.Substring(marker + 5), out part);
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A file held open by a viewer can be removed on the next run.
        }
    }
}
