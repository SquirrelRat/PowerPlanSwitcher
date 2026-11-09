using System.IO;

namespace PowerPlanSwitcher.Services;

/// <summary>Where the app keeps its settings and logs.</summary>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                {
                    local = Path.GetTempPath();
                }

                return Path.Combine(local, "PowerPlanSwitcher");
            }
            catch (Exception)
            {
                return Path.Combine(Path.GetTempPath(), "PowerPlanSwitcher");
            }
        }
    }

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
}
