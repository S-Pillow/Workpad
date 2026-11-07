using System;
using System.Windows;
using System.Windows.Controls;
using WorkNotes.Services;

namespace WorkNotes.Controls
{
    /// <summary>EPP Inspector pane (WP-11): read-only summary — envelope,
    /// command/result, object context, TRIDs; unknown extensions listed as
    /// opaque entries, never interpreted.</summary>
    public partial class EppInspectorView : UserControl
    {
        private Action? _onCancel;

        public EppInspectorView()
        {
            InitializeComponent();
        }

        public void ShowBusy(Action onCancel)
        {
            _onCancel = onCancel;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        public void ShowSummary(EppSummary summary)
        {
            _onCancel = null;
            BusyOverlay.Visibility = Visibility.Collapsed;
            if (summary.Kind == "malformed" || summary.Kind == "unsupported")
            {
                StatusText.Text = summary.Error ?? "Unsupported input.";
                FieldsList.ItemsSource = null;
                return;
            }
            StatusText.Text = summary.Kind == "epp-command" ? "EPP command" : "EPP response";
            FieldsList.ItemsSource = summary.Fields;
            if (summary.UnknownExtensions.Count > 0)
            {
                UnknownHeader.Visibility = Visibility.Visible;
                UnknownList.ItemsSource = summary.UnknownExtensions;
            }
        }

        public void ShowError(string message)
        {
            _onCancel = null;
            BusyOverlay.Visibility = Visibility.Collapsed;
            StatusText.Text = message;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _onCancel?.Invoke();
    }
}
