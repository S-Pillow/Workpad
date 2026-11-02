using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkNotes.Services;

namespace WorkNotes.Dialogs
{
    /// <summary>VS Code-style command palette (Ctrl+Shift+P).</summary>
    public partial class CommandPaletteWindow : Window
    {
        private readonly CommandRegistry _registry;

        public CommandPaletteWindow(CommandRegistry registry)
        {
            _registry = registry;
            InitializeComponent();
            Refresh("");
            Loaded += (_, _) => SearchBox.Focus();
        }

        private void Refresh(string query)
        {
            ResultsList.ItemsSource = _registry.Search(query).ToList();
            if (ResultsList.Items.Count > 0)
                ResultsList.SelectedIndex = 0;
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
            Refresh(SearchBox.Text);

        private void ExecuteSelected()
        {
            if (ResultsList.SelectedItem is CommandEntry entry)
            {
                Close();
                entry.Execute();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { ExecuteSelected(); e.Handled = true; }
            else if (e.Key == Key.Down)
            {
                if (ResultsList.SelectedIndex < ResultsList.Items.Count - 1)
                    ResultsList.SelectedIndex++;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (ResultsList.SelectedIndex > 0)
                    ResultsList.SelectedIndex--;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
            }
        }

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ExecuteSelected();
    }
}
