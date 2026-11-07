using System;
using System.Windows;
using System.Windows.Controls;
using WorkNotes.Services;

namespace WorkNotes.Controls
{
    /// <summary>XML Tree Inspector pane (WP-10): navigable tree of elements,
    /// attributes, namespaces, comments, CDATA and text. Source positions are
    /// surfaced when the parser reports them; the jump button stays disabled
    /// (honest) when a node has no position.</summary>
    public partial class XmlTreeView : UserControl
    {
        private Action? _onCancel;
        private Action<int, int>? _onJump;
        private XmlTreeNode? _selected;

        public XmlTreeView()
        {
            InitializeComponent();
        }

        public void ShowBusy(Action onCancel)
        {
            _onCancel = onCancel;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        public void ShowTree(XmlTreeNode root, Action<int, int>? onJump)
        {
            _onCancel = null;
            BusyOverlay.Visibility = Visibility.Collapsed;
            _onJump = onJump;
            Tree.ItemsSource = root.Children;
            StatusText.Text = _onJump == null
                ? "Source mapping unavailable in this view."
                : "Select an element, then Jump to source.";
        }

        public void ShowError(string message)
        {
            _onCancel = null;
            BusyOverlay.Visibility = Visibility.Collapsed;
            StatusText.Text = message;
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selected = e.NewValue as XmlTreeNode;
            JumpButton.IsEnabled = _selected?.Line != null && _onJump != null;
        }

        private void JumpToSource_Click(object sender, RoutedEventArgs e)
        {
            if (_selected?.Line is int line && _selected.Position is int col)
                _onJump?.Invoke(line, col);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _onCancel?.Invoke();
    }
}
