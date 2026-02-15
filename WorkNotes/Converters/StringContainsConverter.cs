using System;
using System.Globalization;
using System.Windows.Data;

namespace WorkNotes.Converters
{
    /// <summary>
    /// Converts a string to bool by checking if it contains a specified substring.
    /// Used to detect unsaved indicator (•) in tab headers.
    /// </summary>
    public class StringContainsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && parameter is string searchStr)
            {
                return str.Contains(searchStr);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
