using System;
using System.Windows;
using System.Windows.Controls;

namespace WorkNotes.Dialogs
{
    /// <summary>
    /// Dialog for inserting a Markdown link.
    /// </summary>
    public partial class InsertLinkDialog : Window
    {
        public string LinkUrl { get; private set; } = string.Empty;
        public string LinkLabel { get; private set; } = string.Empty;

        public InsertLinkDialog(string? selectedText = null)
        {
            InitializeComponent();
            
            // Pre-fill label with selected text if provided
            if (!string.IsNullOrEmpty(selectedText))
            {
                LabelTextBox.Text = selectedText;
                UrlTextBox.Focus();
            }
            else
            {
                LabelTextBox.Focus();
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            LinkUrl = UrlTextBox.Text.Trim();
            LinkLabel = LabelTextBox.Text.Trim();

            // Basic validation
            if (string.IsNullOrWhiteSpace(LinkUrl))
            {
                MessageBox.Show("URL cannot be empty.", "Invalid Input", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                UrlTextBox.Focus();
                return;
            }

            // Minimal URL validation
            if (!IsValidUrl(LinkUrl))
            {
                var result = MessageBox.Show(
                    "The URL doesn't look valid. Insert anyway?", 
                    "Confirm", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                {
                    UrlTextBox.Focus();
                    return;
                }
            }

            // If no label, use URL as label
            if (string.IsNullOrWhiteSpace(LinkLabel))
            {
                LinkLabel = LinkUrl;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool IsValidUrl(string url)
        {
            // Minimal validation: starts with http(s):// or looks like a domain
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if it looks like a domain (contains a dot and no spaces)
            if (url.Contains(".") && !url.Contains(" "))
            {
                return true;
            }

            return false;
        }
    }
}
