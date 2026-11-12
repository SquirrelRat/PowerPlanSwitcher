using System.Reflection;

namespace PowerPlanSwitcher.Services;

/// <summary>The version shown in the app and diagnostics.</summary>
public static class AppInfo
{
    public static string Version
    {
        get
        {
            Version? version = typeof(AppInfo).Assembly.GetName().Version;
            return version is null ? "0.2.1" : version.ToString(3);
        }
    }

    public static string VersionText => "Version " + Version;
}
