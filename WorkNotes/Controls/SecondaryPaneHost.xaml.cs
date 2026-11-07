using System.Windows;
using System.Windows.Controls;

namespace WorkNotes.Controls
{
    /// <summary>
    /// Per-tab host for the conflict banner and the reusable secondary pane
    /// (compare / inspector / tree views). Closing the pane returns the tab to
    /// the simple single-editor layout.
    /// </summary>
    public partial class SecondaryPaneHost : UserControl
    {
        public SecondaryPaneHost() => InitializeComponent();

        public event RoutedEventHandler? CompareRequested;
        public event RoutedEventHandler? ReloadRequested;
        public event RoutedEventHandler? KeepRequested;
        public event RoutedEventHandler? SaveCopyRequested;

        public bool IsPaneOpen => SecondaryPane.Visibility == Visibility.Visible;

        public void ShowConflict(string message)
        {
            ConflictText.Text = message;
            ConflictBanner.Visibility = Visibility.Visible;
        }

        public void HideConflict() => ConflictBanner.Visibility = Visibility.Collapsed;

        public bool IsConflictVisible => ConflictBanner.Visibility == Visibility.Visible;

        public void OpenPane(string title, UIElement content)
        {
            PaneTitle.Text = title;
            PaneContent.Content = content;
            SecondaryPane.Visibility = Visibility.Visible;
        }

        public void ClosePane()
        {
            SecondaryPane.Visibility = Visibility.Collapsed;
            PaneContent.Content = null;
        }

        private void Compare_Click(object s, RoutedEventArgs e) => CompareRequested?.Invoke(s, e);
        private void Reload_Click(object s, RoutedEventArgs e) => ReloadRequested?.Invoke(s, e);
        private void Keep_Click(object s, RoutedEventArgs e) => KeepRequested?.Invoke(s, e);
        private void SaveCopy_Click(object s, RoutedEventArgs e) => SaveCopyRequested?.Invoke(s, e);
        private void Dismiss_Click(object s, RoutedEventArgs e) => HideConflict();
        private void ClosePane_Click(object s, RoutedEventArgs e) => ClosePane();
    }
}
