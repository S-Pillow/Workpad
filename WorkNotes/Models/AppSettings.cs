using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WorkNotes.Models
{
    /// <summary>
    /// Application settings with persistence.
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkNotes",
            "settings.json");

        private ThemeMode _themeMode = ThemeMode.System;
        private EditorViewMode _defaultEditorView = EditorViewMode.Formatted;
        private bool _confirmBeforeOpeningLinks = true;
        private bool _enableAutoLinkDetection = true;
        private bool _enableSpellCheck = true;
        private bool _enableBionicReading = false;
        private BionicStrength _bionicStrength = BionicStrength.Medium;
        private string _fontFamily = "Consolas";
        private double _fontSize = 12.0;
        private bool _wordWrap = true;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        /// <summary>
        /// Gets or sets the theme mode.
        /// </summary>
        public ThemeMode ThemeMode
        {
            get => _themeMode;
            set
            {
                if (_themeMode != value)
                {
                    _themeMode = value;
                    OnPropertyChanged();
                    OnSettingChanged("ThemeMode");
                }
            }
        }

        /// <summary>
        /// Gets or sets the default editor view mode.
        /// </summary>
        public EditorViewMode DefaultEditorView
        {
            get => _defaultEditorView;
            set
            {
                if (_defaultEditorView != value)
                {
                    _defaultEditorView = value;
                    OnPropertyChanged();
                    OnSettingChanged("DefaultEditorView");
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to confirm before opening links.
        /// </summary>
        public bool ConfirmBeforeOpeningLinks
        {
            get => _confirmBeforeOpeningLinks;
            set
            {
                if (_confirmBeforeOpeningLinks != value)
                {
                    _confirmBeforeOpeningLinks = value;
                    OnPropertyChanged();
                    OnSettingChanged("ConfirmBeforeOpeningLinks");
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to enable auto-link detection for bare URLs/domains.
        /// </summary>
        public bool EnableAutoLinkDetection
        {
            get => _enableAutoLinkDetection;
            set
            {
                if (_enableAutoLinkDetection != value)
                {
                    _enableAutoLinkDetection = value;
                    OnPropertyChanged();
                    OnSettingChanged("EnableAutoLinkDetection");
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to enable spell checking.
        /// </summary>
        public bool EnableSpellCheck
        {
            get => _enableSpellCheck;
            set
            {
                if (_enableSpellCheck != value)
                {
                    _enableSpellCheck = value;
                    OnPropertyChanged();
                    OnSettingChanged("EnableSpellCheck");
                }
            }
        }

        /// <summary>
        /// Gets or sets whether to enable Bionic Reading mode.
        /// </summary>
        public bool EnableBionicReading
        {
            get => _enableBionicReading;
            set
            {
                if (_enableBionicReading != value)
                {
                    _enableBionicReading = value;
                    OnPropertyChanged();
                    OnSettingChanged("EnableBionicReading");
                }
            }
        }

        /// <summary>
        /// Gets or sets the Bionic Reading strength preset.
        /// </summary>
        public BionicStrength BionicStrength
        {
            get => _bionicStrength;
            set
            {
                if (_bionicStrength != value)
                {
                    _bionicStrength = value;
                    OnPropertyChanged();
                    OnSettingChanged("BionicStrength");
                }
            }
        }

        /// <summary>
        /// Gets or sets the editor font family.
        /// </summary>
        public string FontFamily
        {
            get => _fontFamily;
            set
            {
                if (_fontFamily != value)
                {
                    _fontFamily = value;
                    OnPropertyChanged();
                    OnSettingChanged("FontFamily");
                }
            }
        }

        /// <summary>
        /// Gets or sets the editor font size.
        /// </summary>
        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (Math.Abs(_fontSize - value) > 0.01)
                {
                    _fontSize = value;
                    OnPropertyChanged();
                    OnSettingChanged("FontSize");
                }
            }
        }

        /// <summary>
        /// Gets or sets whether word wrap is enabled.
        /// </summary>
        public bool WordWrap
        {
            get => _wordWrap;
            set
            {
                if (_wordWrap != value)
                {
                    _wordWrap = value;
                    OnPropertyChanged();
                    OnSettingChanged("WordWrap");
                }
            }
        }

        /// <summary>
        /// Loads settings from disk.
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch
            {
                // If load fails, return defaults
            }

            return new AppSettings();
        }

        /// <summary>
        /// Saves settings to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Silently fail - don't block app if settings can't save
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void OnSettingChanged(string settingName)
        {
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(settingName));
        }
    }

    /// <summary>
    /// Event args for setting changes.
    /// </summary>
    public class SettingChangedEventArgs : EventArgs
    {
        public string SettingName { get; }

        public SettingChangedEventArgs(string settingName)
        {
            SettingName = settingName;
        }
    }

    /// <summary>
    /// Theme mode options.
    /// </summary>
    public enum ThemeMode
    {
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Editor view mode options.
    /// </summary>
    public enum EditorViewMode
    {
        Formatted,
        Source
    }

    /// <summary>
    /// Bionic Reading strength presets.
    /// </summary>
    public enum BionicStrength
    {
        Light,    // Bold first 1-2 letters
        Medium,   // Bold first 2-3 letters (default)
        Strong    // Bold first 3-4 letters
    }
}
