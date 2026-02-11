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

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Load settings
            Settings = AppSettings.Load();

            // Apply theme on startup
            ThemeManager.ApplyTheme(Settings.ThemeMode);
        }
    }
}
