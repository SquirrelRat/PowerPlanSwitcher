using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PowerPlanSwitcher.Theme;

/// <summary>
/// Applies the light or dark surface to a window, following the system setting.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";

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

        UserPreferenceChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General)
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
        string path = IsDarkMode() ? "Theme/FluentTheme.Dark.xaml" : "Theme/FluentTheme.xaml";

        ResourceDictionary? theme = new ResourceDictionary
        {
            Source = new Uri(path, UriKind.Relative),
        };

        window.Resources.MergedDictionaries.Clear();
        window.Resources.MergedDictionaries.Add(theme);

        if (window.Resources["WindowBackground"] is System.Windows.Media.SolidColorBrush background)
        {
            window.Background = background;
        }
    }

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
}
