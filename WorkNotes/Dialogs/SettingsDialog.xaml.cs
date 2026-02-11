using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WorkNotes.Models;
using WorkNotes.Services;

namespace WorkNotes.Dialogs
{
    /// <summary>
    /// Modern settings dialog with toggle switches.
    /// </summary>
    public partial class SettingsDialog : Window
    {
        private readonly Action _onSettingsChanged;

        public SettingsDialog(Action onSettingsChanged)
        {
            InitializeComponent();
            _onSettingsChanged = onSettingsChanged;

            // Initialize theme radio buttons
            switch (App.Settings.ThemeMode)
            {
                case ThemeMode.System:
                    ThemeSystem.IsChecked = true;
                    break;
                case ThemeMode.Light:
                    ThemeLight.IsChecked = true;
                    break;
                case ThemeMode.Dark:
                    ThemeDark.IsChecked = true;
                    break;
            }

            // Initialize toggle switches
            UpdateToggleSwitch(ConfirmLinksBorder, ConfirmLinksThumb, App.Settings.ConfirmBeforeOpeningLinks);
            UpdateToggleSwitch(AutoLinkBorder, AutoLinkThumb, App.Settings.EnableAutoLinkDetection);
            UpdateToggleSwitch(SpellCheckBorder, SpellCheckThumb, App.Settings.EnableSpellCheck);
            UpdateToggleSwitch(BionicBorder, BionicThumb, App.Settings.EnableBionicReading);

            // Initialize bionic strength radio buttons
            switch (App.Settings.BionicStrength)
            {
                case BionicStrength.Light:
                    BionicLight.IsChecked = true;
                    break;
                case BionicStrength.Medium:
                    BionicMedium.IsChecked = true;
                    break;
                case BionicStrength.Strong:
                    BionicStrong.IsChecked = true;
                    break;
            }
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ThemeSystem.IsChecked == true)
            {
                App.Settings.ThemeMode = ThemeMode.System;
            }
            else if (ThemeLight.IsChecked == true)
            {
                App.Settings.ThemeMode = ThemeMode.Light;
            }
            else if (ThemeDark.IsChecked == true)
            {
                App.Settings.ThemeMode = ThemeMode.Dark;
            }

            App.Settings.Save();
            ThemeManager.ApplyTheme(App.Settings.ThemeMode);
            _onSettingsChanged?.Invoke();
        }

        private void ConfirmLinksToggle_Click(object sender, MouseButtonEventArgs e)
        {
            App.Settings.ConfirmBeforeOpeningLinks = !App.Settings.ConfirmBeforeOpeningLinks;
            App.Settings.Save();
            UpdateToggleSwitch(ConfirmLinksBorder, ConfirmLinksThumb, App.Settings.ConfirmBeforeOpeningLinks);
        }

        private void AutoLinkToggle_Click(object sender, MouseButtonEventArgs e)
        {
            App.Settings.EnableAutoLinkDetection = !App.Settings.EnableAutoLinkDetection;
            App.Settings.Save();
            UpdateToggleSwitch(AutoLinkBorder, AutoLinkThumb, App.Settings.EnableAutoLinkDetection);
            _onSettingsChanged?.Invoke();
        }

        private void SpellCheckToggle_Click(object sender, MouseButtonEventArgs e)
        {
            App.Settings.EnableSpellCheck = !App.Settings.EnableSpellCheck;
            App.Settings.Save();
            UpdateToggleSwitch(SpellCheckBorder, SpellCheckThumb, App.Settings.EnableSpellCheck);
            _onSettingsChanged?.Invoke();
        }

        private void BionicToggle_Click(object sender, MouseButtonEventArgs e)
        {
            App.Settings.EnableBionicReading = !App.Settings.EnableBionicReading;
            App.Settings.Save();
            UpdateToggleSwitch(BionicBorder, BionicThumb, App.Settings.EnableBionicReading);
            _onSettingsChanged?.Invoke();
        }

        private void BionicStrength_Checked(object sender, RoutedEventArgs e)
        {
            if (BionicLight.IsChecked == true)
            {
                App.Settings.BionicStrength = BionicStrength.Light;
            }
            else if (BionicMedium.IsChecked == true)
            {
                App.Settings.BionicStrength = BionicStrength.Medium;
            }
            else if (BionicStrong.IsChecked == true)
            {
                App.Settings.BionicStrength = BionicStrength.Strong;
            }

            App.Settings.Save();
            _onSettingsChanged?.Invoke();
        }

        private void UpdateToggleSwitch(System.Windows.Controls.Border border, System.Windows.Shapes.Ellipse thumb, bool isOn)
        {
            var accentBrush = TryFindResource("App.Accent") as SolidColorBrush ?? Brushes.DodgerBlue;
            var borderBrush = TryFindResource("App.Border") as SolidColorBrush ?? Brushes.Gray;

            // Animate the background color
            var colorAnimation = new ColorAnimation
            {
                To = isOn ? accentBrush.Color : borderBrush.Color,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            border.Background = new SolidColorBrush(border.Background is SolidColorBrush sb ? sb.Color : borderBrush.Color);
            border.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);

            // Animate the thumb position
            var marginAnimation = new ThicknessAnimation
            {
                To = new Thickness(isOn ? 27 : 3, 0, 0, 0),
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            thumb.BeginAnimation(System.Windows.FrameworkElement.MarginProperty, marginAnimation);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
