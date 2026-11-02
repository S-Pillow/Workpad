using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkNotes.Models;
using ThemeMode = WorkNotes.Models.ThemeMode;
using WorkNotes.Services;

namespace WorkNotes.Dialogs
{
    public partial class SettingsWindow : Window
    {
        private readonly SpellCheckService _spellCheckService;
        private bool _isInitializing = true;

        public SettingsWindow(SpellCheckService spellCheckService)
        {
            InitializeComponent();
            _spellCheckService = spellCheckService;
            
            LoadSettings();
            _isInitializing = false;
        }

        private void LoadSettings()
        {
            // Theme
            switch (App.Settings.ThemeMode)
            {
                case Models.ThemeMode.Light:
                    ThemeLight.IsChecked = true;
                    break;
                case Models.ThemeMode.Dark:
                    ThemeDark.IsChecked = true;
                    break;
                case Models.ThemeMode.System:
                    ThemeSystem.IsChecked = true;
                    break;
            }

            // Restore tabs
            RestoreTabsCheckBox.IsChecked = App.Settings.RestoreOpenTabs;

            // Font Family
            var fonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            FontFamilyComboBox.ItemsSource = fonts.Select(f => f.Source);
            FontFamilyComboBox.SelectedItem = App.Settings.FontFamily;

            // Font Size
            var sizes = new List<double> { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72 };
            FontSizeComboBox.ItemsSource = sizes;
            FontSizeComboBox.SelectedItem = App.Settings.FontSize;

            // Update font preview
            UpdateFontPreview();

            // Word Wrap
            WordWrapCheckBox.IsChecked = App.Settings.WordWrap;

            // Bionic Reading
            if (!App.Settings.EnableBionicReading)
            {
                BionicStrengthComboBox.SelectedIndex = 0; // Off
            }
            else
            {
                BionicStrengthComboBox.SelectedIndex = App.Settings.BionicStrength switch
                {
                    BionicStrength.Light => 1,
                    BionicStrength.Medium => 2,
                    BionicStrength.Strong => 3,
                    _ => 2
                };
            }

            // Source editor font
            SourceFontFamilyComboBox.ItemsSource = fonts.Select(f => f.Source);
            SourceFontFamilyComboBox.SelectedItem = App.Settings.SourceFontFamily;
            SourceFontSizeComboBox.ItemsSource = sizes;
            SourceFontSizeComboBox.SelectedItem = App.Settings.SourceFontSize;
            UpdateSourceFontPreview();

            // Line numbers
            LineNumbersCheckBox.IsChecked = App.Settings.ShowLineNumbers;

            // Recovery
            PreserveUnsavedCheckBox.IsChecked = App.Settings.PreserveUnsavedContent;

            // Spell Check
            SpellCheckCheckBox.IsChecked = App.Settings.EnableSpellCheck;

            // Custom Dictionary
            LoadCustomWords();
        }

        private void UpdateSourceFontPreview()
        {
            SourceFontPreview.Text = $"{App.Settings.SourceFontFamily}, {App.Settings.SourceFontSize:0.#}pt";
        }

        private void SourceFontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (SourceFontFamilyComboBox.SelectedItem is string fontFamily)
            {
                App.Settings.SourceFontFamily = fontFamily;
                UpdateSourceFontPreview();
            }
        }

        private void SourceFontSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (SourceFontSizeComboBox.SelectedItem is double fontSize)
            {
                App.Settings.SourceFontSize = fontSize;
                UpdateSourceFontPreview();
            }
        }

        private void LineNumbers_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            App.Settings.ShowLineNumbers = LineNumbersCheckBox.IsChecked == true;
        }

        private void PreserveUnsaved_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            App.Settings.PreserveUnsavedContent = PreserveUnsavedCheckBox.IsChecked == true;
        }

        private void ClearRecovery_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new Services.SessionRecoveryService().ClearAll();
                MessageBox.Show("Recovery snapshots deleted.", "Recovery", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not clear recovery data: {ex.Message}", "Recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateFontPreview()
        {
            // FontPreview is now a directly-named element in the Expander header, so it is a
            // real generated field. (It previously lived in a HeaderTemplate and was looked up
            // through Template.FindName, which searches the CONTROL template — always null,
            // so the summary line never updated.)
            FontPreview.Text = $"{App.Settings.FontFamily}, {App.Settings.FontSize:0.#}pt";
        }

        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (ThemeLight.IsChecked == true)
            {
                App.Settings.ThemeMode = Models.ThemeMode.Light;
            }
            else if (ThemeDark.IsChecked == true)
            {
                App.Settings.ThemeMode = Models.ThemeMode.Dark;
            }
            else if (ThemeSystem.IsChecked == true)
            {
                App.Settings.ThemeMode = Models.ThemeMode.System;
            }

            App.Settings.Save();
            ThemeManager.ApplyTheme(App.Settings.ThemeMode);
        }

        private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (FontFamilyComboBox.SelectedItem is string fontFamily)
            {
                App.Settings.FontFamily = fontFamily;
                App.Settings.Save();
                UpdateFontPreview();
            }
        }

        private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (FontSizeComboBox.SelectedItem is double fontSize)
            {
                App.Settings.FontSize = fontSize;
                App.Settings.Save();
                UpdateFontPreview();
            }
        }

        private void WordWrap_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            App.Settings.WordWrap = WordWrapCheckBox.IsChecked == true;
            App.Settings.Save();
        }

        private void BionicStrength_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            switch (BionicStrengthComboBox.SelectedIndex)
            {
                case 0: // Off
                    App.Settings.EnableBionicReading = false;
                    break;
                case 1: // Light
                    App.Settings.EnableBionicReading = true;
                    App.Settings.BionicStrength = BionicStrength.Light;
                    break;
                case 2: // Medium
                    App.Settings.EnableBionicReading = true;
                    App.Settings.BionicStrength = BionicStrength.Medium;
                    break;
                case 3: // Strong
                    App.Settings.EnableBionicReading = true;
                    App.Settings.BionicStrength = BionicStrength.Strong;
                    break;
            }

            App.Settings.Save();
        }

        private void SpellCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            App.Settings.EnableSpellCheck = SpellCheckCheckBox.IsChecked == true;
            App.Settings.Save();
        }

        private void LoadCustomWords()
        {
            var customWords = _spellCheckService.GetCustomWords();
            CustomWordsListBox.ItemsSource = customWords.OrderBy(w => w).ToList();
        }

        private void AddWord_Click(object sender, RoutedEventArgs e)
        {
            var word = AddWordTextBox.Text.Trim();
            if (string.IsNullOrEmpty(word))
                return;

            _spellCheckService.AddToCustomDictionary(word);
            AddWordTextBox.Clear();
            LoadCustomWords();
            
            // Trigger refresh of all editors
            App.Settings.OnSettingChanged("CustomDictionary");
        }

        private void RemoveWord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string word)
            {
                _spellCheckService.RemoveFromCustomDictionary(word);
                LoadCustomWords();
                
                // Trigger refresh of all editors
                App.Settings.OnSettingChanged("CustomDictionary");
            }
        }

        private void RestoreTabs_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            App.Settings.RestoreOpenTabs = RestoreTabsCheckBox.IsChecked == true;
            App.Settings.Save();
        }
    }
}
