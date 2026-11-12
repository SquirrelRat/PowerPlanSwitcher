using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlanSwitcher.Core;

/// <summary>When the app last looked for an update.</summary>
public sealed record UpdateState(
    [property: JsonPropertyName("lastCheckUtc")] DateTimeOffset? LastCheckUtc);

/// <summary>
/// Reads and writes update.json next to the settings file. Runtime state,
/// not user configuration: a missing or broken file just means "never
/// checked"; writes are atomic and never throw.
/// </summary>
public sealed class UpdateStateStore
{
    public const string FileName = "update.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;

    public UpdateStateStore(string directory)
    {
        _directory = directory;
    }

    public UpdateState Load()
    {
        try
        {
            string path = Path.Combine(_directory, FileName);

            if (!File.Exists(path))
            {
                return new UpdateState(null);
            }

            UpdateState? state = JsonSerializer.Deserialize<UpdateState>(
                File.ReadAllText(path),
                Options);

            return state ?? new UpdateState(null);
        }
        catch (Exception)
        {
            return new UpdateState(null);
        }
    }

    public void Save(UpdateState state)
    {
        try
        {
            string path = Path.Combine(_directory, FileName);
            string tempPath = path + ".tmp";

            Directory.CreateDirectory(_directory);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, Options));

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch (Exception)
        {
            // Best effort: the check simply runs again sooner than planned.
        }
    }
}
