using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PowerPlanSwitcher.Theme;

/// <summary>
/// Applies the light, dark, or high-contrast surface to a window, following the
/// system setting.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int size);

    /// <summary>True when Windows is currently using dark mode for apps.</summary>
    public static bool IsDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            object? value = key?.GetValue(AppsUseLightTheme);

            if (value is int asInt)
            {
                return asInt == 0;
            }
        }
        catch (Exception)
        {
            // Fall back to light.
        }

        return false;
    }

    /// <summary>
    /// True when the dark surfaces should be used: dark mode on and no high
    /// contrast (high contrast supplies its own colors).
    /// </summary>
    public static bool UseDarkSurfaces() => !SystemParameters.HighContrast && IsDarkMode();

    /// <summary>
    /// Replaces the themed dictionaries on <paramref name="window"/> with the ones
    /// matching the current system theme, then applies the window background.
    /// Subscribes once per window; unsubscribes when the window closes.
    /// </summary>
    public static void Apply(Window window)
    {
        try
        {
            ApplyTheme(window);
        }
        catch (Exception)
        {
        }

        window.SourceInitialized += (_, _) => ApplyTitleBar(window);

        UserPreferenceChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.Category is UserPreferenceCategory.General
                or UserPreferenceCategory.Accessibility
                or UserPreferenceCategory.Color)
            {
                Reload(window);
            }
        };

        SystemEvents.UserPreferenceChanged += handler;

        window.Closed += (_, _) =>
        {
            try
            {
                if (handler is not null)
                {
                    SystemEvents.UserPreferenceChanged -= handler;
                }
            }
            catch (Exception)
            {
            }
        };
    }

    private static void ApplyTheme(Window window)
    {
        string path = SystemParameters.HighContrast
            ? "HighContrastTheme.xaml"
            : IsDarkMode()
                ? "FluentTheme.Dark.xaml"
                : "FluentTheme.xaml";

        window.Resources.MergedDictionaries.Clear();
        window.Resources.MergedDictionaries.Add(LoadTheme("Controls.xaml"));
        window.Resources.MergedDictionaries.Add(LoadTheme(path));

        if (window.Resources["WindowBackground"] is SolidColorBrush background)
        {
            window.Background = background;
        }

        ApplyTitleBar(window);
    }

    private static ResourceDictionary LoadTheme(string fileName) => new()
    {
        Source = new Uri(
            "pack://application:,,,/PowerPlanSwitcher;component/Theme/" + fileName,
            UriKind.Absolute),
    };

    private static void Reload(Window window)
    {
        try
        {
            if (window.Dispatcher.CheckAccess())
            {
                ApplyTheme(window);
                return;
            }

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ApplyTheme(window);
                }
                catch (Exception)
                {
                }
            }));
        }
        catch (Exception)
        {
        }
    }

    private static void ApplyTitleBar(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int dark = !SystemParameters.HighContrast && IsDarkMode() ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch (Exception)
        {
            // The title bar keeps the system default.
        }
    }
}
