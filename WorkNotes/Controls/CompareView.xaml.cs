using System;
using System.Windows;
using System.Windows.Controls;
using WorkNotes.Services;

namespace WorkNotes.Controls
{
    /// <summary>Renders a CompareResult as a line diff (removed/added tinted),
    /// with a cancellable busy state while the diff runs off the UI thread.</summary>
    public partial class CompareView : UserControl
    {
        private Action? _onCancel;

        public CompareView()
        {
            InitializeComponent();
        }

        /// <summary>Busy state shown while CompareAsync runs off the UI thread.
        /// <paramref name="onCancel"/> cancels the in-flight job.</summary>
        public void ShowBusy(Action onCancel)
        {
            _onCancel = onCancel;
            DiffList.Items.Clear();
            BusyOverlay.Visibility = Visibility.Visible;
        }

        public void ShowResult(CompareResult result, string leftLabel, string rightLabel)
        {
            _onCancel = null;
            BusyOverlay.Visibility = Visibility.Collapsed;
            DiffList.Items.Clear();
            DiffList.Items.Add(new DiffLine { Kind = DiffLineKind.Same, Text = $"── {leftLabel} ── vs ── {rightLabel} ──" });
            if (result.Identical)
            {
                DiffList.Items.Add(new DiffLine { Kind = DiffLineKind.Same, Text = "(identical)" });
                return;
            }
            foreach (var line in result.Lines)
            {
                var prefix = line.Kind == DiffLineKind.Removed ? "- " : line.Kind == DiffLineKind.Added ? "+ " : "  ";
                DiffList.Items.Add(new DiffLine { Kind = line.Kind, Text = prefix + line.Text });
            }
        }

        private void CancelCompare_Click(object sender, RoutedEventArgs e) => _onCancel?.Invoke();
    }
}
