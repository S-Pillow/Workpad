using System;
using System.Windows;

namespace WorkNotes.Dialogs
{
    /// <summary>
    /// Confirmation dialog for opening links.
    /// </summary>
    public partial class ConfirmLinkDialog : Window
    {
        public bool DontAskAgain { get; private set; }

        public ConfirmLinkDialog(string url)
        {
            InitializeComponent();

            // Set URL
            UrlTextBox.Text = url;

            // Parse and display domain
            try
            {
                var uri = new Uri(url);
                DomainTextBlock.Text = uri.Host;

                // Check for warnings
                CheckForWarnings(uri);
            }
            catch
            {
                DomainTextBlock.Text = "(invalid URL)";
                ShowWarning("This URL appears to be malformed.");
            }

            // Focus on Open button
            Loaded += (s, e) => Focus();
        }

        private void CheckForWarnings(Uri uri)
        {
            // Warning for HTTP (not HTTPS)
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                ShowWarning("This connection is not secure (HTTP).");
                return;
            }

            // Warning for punycode domains (potential homograph attack)
            if (uri.Host.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
            {
                ShowWarning("This domain uses international characters (punycode).");
                return;
            }

            // Warning for non-standard ports
            if (!uri.IsDefaultPort && uri.Port != 80 && uri.Port != 443)
            {
                ShowWarning($"This URL uses a non-standard port ({uri.Port}).");
            }
        }

        private void ShowWarning(string message)
        {
            WarningPanel.Visibility = Visibility.Visible;
            WarningTextBlock.Text = message;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(UrlTextBox.Text);
                MessageBox.Show("URL copied to clipboard.", "Copied", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy URL: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
