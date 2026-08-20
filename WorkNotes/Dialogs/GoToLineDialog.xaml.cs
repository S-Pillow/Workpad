using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace WorkNotes.Dialogs
{
    public partial class GoToLineDialog : Window
    {
        private static readonly Regex DigitsOnly = new("^[0-9]+$", RegexOptions.Compiled);
        private readonly int _maximumLine;

        public int LineNumber { get; private set; }

        public GoToLineDialog(int currentLine, int maximumLine)
        {
            InitializeComponent();

            _maximumLine = Math.Max(1, maximumLine);
            LineNumberTextBox.Text = Math.Clamp(currentLine, 1, _maximumLine)
                .ToString(CultureInfo.InvariantCulture);
            RangeText.Text = $"Enter a line number from 1 to {_maximumLine:N0}.";

            Loaded += (_, _) =>
            {
                LineNumberTextBox.Focus();
                LineNumberTextBox.SelectAll();
            };
        }

        private void LineNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnly.IsMatch(e.Text);
        }

        private void Go_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(LineNumberTextBox.Text, out var lineNumber) ||
                lineNumber < 1 || lineNumber > _maximumLine)
            {
                MessageBox.Show(
                    $"Enter a line number from 1 to {_maximumLine:N0}.",
                    "Line out of range",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LineNumberTextBox.Focus();
                LineNumberTextBox.SelectAll();
                return;
            }

            LineNumber = lineNumber;
            DialogResult = true;
        }
    }
}
