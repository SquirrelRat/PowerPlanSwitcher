using System.IO;
using Microsoft.Win32;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Turns "Start with Windows" on and off through the current user's Run key.
/// </summary>
public sealed class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerPlanSwitcher";

    /// <summary>The command line Windows runs at logon, quoted for paths with spaces.</summary>
    public static string StartupCommand =>
        Environment.ProcessPath is string path ? "\"" + path + "\"" : string.Empty;

    /// <summary>False when the exe path is unknown, so nothing can be registered.</summary>
    public static bool CanRegister => !string.IsNullOrEmpty(Environment.ProcessPath);

    /// <summary>The command currently registered, or null when autostart is off.</summary>
    public string? RegisteredCommand()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !CanRegister)
        {
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, StartupCommand, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // The user toggle simply does not take effect.
        }
        catch (Exception)
        {
            // Registry access must never throw to the UI.
        }
    }
}
