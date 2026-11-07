using System;
using System.Windows;
using WorkNotes.Models;
using ThemeMode = WorkNotes.Models.ThemeMode;
using WorkNotes.Services;

namespace WorkNotes
{
    public partial class App : Application
    {
        /// <summary>
        /// Gets the current application settings.
        /// </summary>
        public static AppSettings Settings { get; private set; } = new AppSettings();

        /// <summary>
        /// Gets the shared spell check service instance.
        /// </summary>
        public static SpellCheckService? SpellCheckService { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Load settings
            Settings = AppSettings.Load();

            // Initialize spell check service once
            try
            {
                SpellCheckService = new SpellCheckService();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize SpellCheckService: {ex}");
                // Non-critical, app can run without spell check
            }

            // Apply theme on startup
            ThemeManager.ApplyTheme(Settings.ThemeMode);

            // Last line of defense: a fatal UI-thread exception still gets a
            // recovery snapshot before the process dies.
            DispatcherUnhandledException += (s, args) =>
            {
                try { EmergencySnapshotRequested?.Invoke(); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try { EmergencySnapshotRequested?.Invoke(); } catch { }
            };
        }

        /// <summary>Raised by the global exception handlers; MainWindow
        /// subscribes its snapshot flush. Best-effort — handlers must swallow
        /// their own exceptions.</summary>
        public static event Action? EmergencySnapshotRequested;
    }
}
