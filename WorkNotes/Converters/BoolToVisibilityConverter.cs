using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkNotes.Converters
{
    /// <summary>
    /// Converts bool to Visibility. Pass "Invert" as parameter to invert the logic.
    /// true → Visible (or Collapsed when inverted)
    /// false → Collapsed (or Visible when inverted)
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool b && b;
            bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);

            if (invert) boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
