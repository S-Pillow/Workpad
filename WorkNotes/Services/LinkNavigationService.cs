using System;
using System.Diagnostics;
using System.Windows;
using WorkNotes.Dialogs;

namespace WorkNotes.Services
{
    /// <summary>
    /// Centralized service for safe link navigation with confirmation dialogs.
    /// </summary>
    public class LinkNavigationService
    {
        private readonly Func<bool> _getConfirmSetting;
        private readonly Action<bool> _setConfirmSetting;

        public LinkNavigationService(
            Func<bool> getConfirmSetting,
            Action<bool> setConfirmSetting)
        {
            _getConfirmSetting = getConfirmSetting;
            _setConfirmSetting = setConfirmSetting;
        }

        /// <summary>
        /// Attempts to navigate to a URL with optional confirmation.
        /// </summary>
        /// <param name="url">The URL to navigate to</param>
        /// <param name="owner">Owner window for the confirmation dialog</param>
        /// <returns>True if navigation was attempted, false if cancelled</returns>
        public bool TryNavigate(string url, Window? owner = null)
        {
            try
            {
                // Normalize and validate URL
                url = NormalizeUrl(url);

                if (!IsAllowedScheme(url))
                {
                    MessageBox.Show(
                        $"Cannot open this type of link.\n\nOnly HTTP and HTTPS links are supported for security reasons.",
                        "Unsupported Link Type",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                // Check if confirmation is needed
                bool needsConfirmation = _getConfirmSetting();

                if (needsConfirmation)
                {
                    var dialog = new ConfirmLinkDialog(url)
                    {
                        Owner = owner ?? Application.Current.MainWindow
                    };

                    var result = dialog.ShowDialog();

                    if (result != true)
                    {
                        return false; // User cancelled
                    }

                    // Check if user selected "Don't ask again"
                    if (dialog.DontAskAgain)
                    {
                        _setConfirmSetting(false);
                    }
                }

                // Navigate
                OpenInBrowser(url);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open link: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Normalizes a URL by ensuring it has a proper scheme.
        /// </summary>
        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            url = url.Trim();

            // Already has a scheme
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            // Email address
            if (url.Contains("@") && !url.Contains("/"))
            {
                return "mailto:" + url;
            }

            // Bare domain or URL - default to HTTPS
            return "https://" + url;
        }

        /// <summary>
        /// Checks if the URL scheme is allowed.
        /// </summary>
        private bool IsAllowedScheme(string url)
        {
            try
            {
                var uri = new Uri(url);
                var scheme = uri.Scheme.ToLowerInvariant();

                // Only allow HTTP and HTTPS by default
                // Mailto is allowed but goes through confirmation
                return scheme == "http" || scheme == "https" || scheme == "mailto";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Opens URL in the default browser.
        /// </summary>
        private void OpenInBrowser(string url)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(processStartInfo);
        }
    }
}
