using System.Windows;
using WorkNotes.Models;
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
        }
    }
}
