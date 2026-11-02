using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using WorkNotes.Models;

namespace WorkNotes.Dialogs
{
    /// <summary>Minimal picker for "Compare with Another Tab".</summary>
    public partial class PickTabWindow : Window
    {
        public DocumentTab? SelectedTab { get; private set; }

        public PickTabWindow(IEnumerable<DocumentTab> tabs)
        {
            InitializeComponent();
            TabList.ItemsSource = tabs;
            if (TabList.Items.Count > 0)
                TabList.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedTab = TabList.SelectedItem as DocumentTab;
            DialogResult = true;
        }

        private void List_DoubleClick(object sender, MouseButtonEventArgs e) => Ok_Click(sender, e);
    }
}
