using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;
using WorkNotes.Models;
using ThemeMode = WorkNotes.Models.ThemeMode;

namespace WorkNotes.Services
{
    /// <summary>
    /// Manages theme switching and persistence.
    /// </summary>
    public static class ThemeManager
    {
        // Curated theme dictionaries — Workpad Default (Light/Dark) plus
        // owner-reviewed palettes: Ocean, Forest, Warm Neutral (WP-07).
        private static readonly Dictionary<ThemeMode, string> ThemeUris = new()
        {
            [ThemeMode.Light] = "Resources/Themes/Theme.Light.xaml",
            [ThemeMode.Dark] = "Resources/Themes/Theme.Dark.xaml",
            [ThemeMode.Ocean] = "Resources/Themes/Theme.Ocean.xaml",
            [ThemeMode.Forest] = "Resources/Themes/Theme.Forest.xaml",
            [ThemeMode.WarmNeutral] = "Resources/Themes/Theme.WarmNeutral.xaml",
        };

        /// <summary>
        /// Applies the theme based on the specified mode.
        /// </summary>
        public static void ApplyTheme(ThemeMode mode)
        {
            var actualTheme = mode == ThemeMode.System ? GetSystemTheme() : mode;
            var themeUri = ThemeUris.TryGetValue(actualTheme, out var uri)
                ? uri : ThemeUris[ThemeMode.Light];

            var app = Application.Current;
            if (app?.Resources == null) return;

            // Find and remove existing theme dictionary
            ResourceDictionary? existingTheme = null;
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source?.OriginalString.Contains("Themes/Theme.") == true)
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
