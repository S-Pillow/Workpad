using System;
using System.Windows;
using Microsoft.Win32;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    /// <summary>
    /// Manages theme switching and persistence.
    /// </summary>
    public static class ThemeManager
    {
        private const string LightThemeUri = "Resources/Themes/Theme.Light.xaml";
        private const string DarkThemeUri = "Resources/Themes/Theme.Dark.xaml";

        /// <summary>
        /// Applies the theme based on the specified mode.
        /// </summary>
        public static void ApplyTheme(ThemeMode mode)
        {
            var actualTheme = mode == ThemeMode.System ? GetSystemTheme() : mode;
            var themeUri = actualTheme == ThemeMode.Dark ? DarkThemeUri : LightThemeUri;

            var app = Application.Current;
            if (app?.Resources == null) return;

            // Find and remove existing theme dictionary
            ResourceDictionary? existingTheme = null;
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source?.OriginalString.Contains("Theme.Light") == true ||
                    dict.Source?.OriginalString.Contains("Theme.Dark") == true)
                {
                    existingTheme = dict;
                    break;
                }
            }

            if (existingTheme != null)
            {
                app.Resources.MergedDictionaries.Remove(existingTheme);
            }

            // Add new theme dictionary at the beginning (so it can be overridden)
            var newTheme = new ResourceDictionary
            {
                Source = new Uri(themeUri, UriKind.Relative)
            };

            app.Resources.MergedDictionaries.Insert(0, newTheme);
        }

        /// <summary>
        /// Gets the current Windows system theme (Light or Dark).
        /// </summary>
        private static ThemeMode GetSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 0 ? ThemeMode.Dark : ThemeMode.Light;
                }
            }
            catch
            {
                // If we can't read registry, default to Light
            }

            return ThemeMode.Light;
        }

        /// <summary>
        /// Gets the effective theme mode (resolves System to Light/Dark).
        /// </summary>
        public static ThemeMode GetEffectiveTheme(ThemeMode mode)
        {
            return mode == ThemeMode.System ? GetSystemTheme() : mode;
        }
    }
}
