using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WorkNotes.Controls;
using WorkNotes.Models;
using WorkNotes.Services;

namespace WorkNotes
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<DocumentTab> _tabs = new ObservableCollection<DocumentTab>();

        public MainWindow()
        {
            InitializeComponent();

            // Set up keyboard shortcuts
            SetupKeyboardShortcuts();

            // Subscribe to settings changes for live updates
            App.Settings.SettingChanged += Settings_Changed;

            // Create initial tab
            CreateNewTab();

            UpdateViewModeUI(App.Settings.DefaultEditorView);
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+N - New Tab
            CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (s, e) => New_Click(s, e)));
            InputBindings.Add(new KeyBinding(ApplicationCommands.New, Key.N, ModifierKeys.Control));

            // Ctrl+O - Open
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (s, e) => Open_Click(s, e)));
            InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));

            // Ctrl+S - Save
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (s, e) => Save_Click(s, e)));
            InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));

            // Ctrl+Shift+S - Save As
            var saveAsCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(saveAsCommand, (s, e) => SaveAs_Click(s, e)));
            InputBindings.Add(new KeyBinding(saveAsCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift));

            // Ctrl+W - Close Tab
            var closeTabCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(closeTabCommand, (s, e) => CloseTab_Click(s, e)));
            InputBindings.Add(new KeyBinding(closeTabCommand, Key.W, ModifierKeys.Control));

            // Ctrl+B - Bold
            var boldCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(boldCommand, (s, e) => Bold_Click(s, e)));
            InputBindings.Add(new KeyBinding(boldCommand, Key.B, ModifierKeys.Control));

            // Ctrl+I - Italic
            var italicCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(italicCommand, (s, e) => Italic_Click(s, e)));
            InputBindings.Add(new KeyBinding(italicCommand, Key.I, ModifierKeys.Control));

            // Ctrl+K - Insert Link
            var insertLinkCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(insertLinkCommand, (s, e) => InsertLink_Click(s, e)));
            InputBindings.Add(new KeyBinding(insertLinkCommand, Key.K, ModifierKeys.Control));

            // Ctrl+F - Find
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (s, e) => Find_Click(s, e)));
            InputBindings.Add(new KeyBinding(ApplicationCommands.Find, Key.F, ModifierKeys.Control));

            // Ctrl+H - Replace
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Replace, (s, e) => Replace_Click(s, e)));
            InputBindings.Add(new KeyBinding(ApplicationCommands.Replace, Key.H, ModifierKeys.Control));

            // Ctrl+Shift+M - Toggle View Mode
            var toggleViewCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(toggleViewCommand, (s, e) => ViewModeToggle_Click(s, e)));
            InputBindings.Add(new KeyBinding(toggleViewCommand, Key.M, ModifierKeys.Control | ModifierKeys.Shift));
        }

        private DocumentTab? GetCurrentTab()
        {
            if (TabControl.SelectedIndex >= 0 && TabControl.SelectedIndex < _tabs.Count)
            {
                return _tabs[TabControl.SelectedIndex];
            }
            return null;
        }

        private void CreateNewTab()
        {
            var document = new Document();
            var tab = new DocumentTab(document, App.Settings.DefaultEditorView);

            // Create editor control
            var editor = new EditorControl
            {
                Document = document,
                ViewMode = tab.ViewMode
            };
            tab.EditorControl = editor;

            // Create TabItem
            var tabItem = new TabItem
            {
                Content = editor,
                Tag = tab
            };

            // Bind header to document name
            var binding = new System.Windows.Data.Binding("HeaderText")
            {
                Source = tab
            };
            tabItem.SetBinding(TabItem.HeaderProperty, binding);

            // Wire up close button
            tabItem.Loaded += (s, e) =>
            {
                var closeButton = FindVisualChild<Button>(tabItem);
                if (closeButton != null)
                {
                    closeButton.Click += (sender, args) =>
                    {
                        args.Handled = true; // Prevent tab selection
                        CloseTab(tab);
                    };
                }
            };

            _tabs.Add(tab);
            TabControl.Items.Add(tabItem);
            TabControl.SelectedItem = tabItem;

            StatusText.Text = "New tab created";
        }

        private void OpenFileInNewTab(string filePath)
        {
            try
            {
                // Check if file is already open
                var existingTab = _tabs.FirstOrDefault(t => 
                    t.Document.FilePath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);

                if (existingTab != null)
                {
                    // Switch to existing tab
                    var index = _tabs.IndexOf(existingTab);
                    TabControl.SelectedIndex = index;
                    StatusText.Text = $"Switched to: {existingTab.Document.FileName}";
                    return;
                }

                // Create new tab with document
                var document = new Document { FilePath = filePath };
                document.Load();

                var tab = new DocumentTab(document, App.Settings.DefaultEditorView);
                var editor = new EditorControl 
                { 
                    Document = document,
                    ViewMode = tab.ViewMode
                };
                tab.EditorControl = editor;

                var tabItem = new TabItem
                {
                    Content = editor,
                    Tag = tab
                };

                var binding = new System.Windows.Data.Binding("HeaderText")
                {
                    Source = tab
                };
                tabItem.SetBinding(TabItem.HeaderProperty, binding);

                // Wire up close button
                tabItem.Loaded += (s, e) =>
                {
                    var closeButton = FindVisualChild<Button>(tabItem);
                    if (closeButton != null)
                    {
                        closeButton.Click += (sender, args) =>
                        {
                            args.Handled = true;
                            CloseTab(tab);
                        };
                    }
                };

                _tabs.Add(tab);
                TabControl.Items.Add(tabItem);
                TabControl.SelectedItem = tabItem;

                StatusText.Text = $"Opened: {document.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseTab(DocumentTab tab)
        {
            if (!PromptSaveIfDirty(tab))
                return;

            var index = _tabs.IndexOf(tab);
            if (index >= 0)
            {
                _tabs.RemoveAt(index);
                TabControl.Items.RemoveAt(index);

                // If no tabs left, create a new one
                if (_tabs.Count == 0)
                {
                    CreateNewTab();
                }
                else
                {
                    // Select adjacent tab
                    if (index >= TabControl.Items.Count)
                        index = TabControl.Items.Count - 1;
                    TabControl.SelectedIndex = index;
                }

                StatusText.Text = "Tab closed";
            }
        }

        private bool PromptSaveIfDirty(DocumentTab tab)
        {
            if (!tab.Document.IsDirty)
                return true;

            var result = MessageBox.Show(
                $"Do you want to save changes to {tab.Document.FileName}?",
                "Work Notes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                return SaveDocument(tab);
            }

            return result == MessageBoxResult.No;
        }

        private bool SaveDocument(DocumentTab tab)
        {
            if (tab.Document.FilePath == null)
            {
                return SaveDocumentAs(tab);
            }

            try
            {
                tab.EditorControl?.SaveToDocument();
                StatusText.Text = $"Saved: {tab.Document.FileName}";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool SaveDocumentAs(DocumentTab tab)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = tab.Document.FileName
            };

            if (dialog.ShowDialog() == true)
            {
                tab.Document.FilePath = dialog.FileName;
                return SaveDocument(tab);
            }

            return false;
        }

        // Helper to find visual children
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        // Event Handlers
        private void New_Click(object sender, RoutedEventArgs e)
        {
            CreateNewTab();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    OpenFileInNewTab(file);
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab != null)
            {
                SaveDocument(tab);
            }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab != null)
            {
                SaveDocumentAs(tab);
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab != null)
            {
                CloseTab(tab);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source && tab.EditorControl.Editor.CanUndo)
            {
                tab.EditorControl.Editor.Undo();
            }
            else if (tab.ViewMode == EditorViewMode.Formatted)
            {
                // RichTextBox undo
                var rtb = tab.EditorControl.FindName("FormattedEditor") as RichTextBox;
                if (rtb?.CanUndo == true)
                {
                    rtb.Undo();
                }
            }
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source && tab.EditorControl.Editor.CanRedo)
            {
                tab.EditorControl.Editor.Redo();
            }
            else if (tab.ViewMode == EditorViewMode.Formatted)
            {
                // RichTextBox redo
                var rtb = tab.EditorControl.FindName("FormattedEditor") as RichTextBox;
                if (rtb?.CanRedo == true)
                {
                    rtb.Redo();
                }
            }
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source)
            {
                tab.EditorControl.Editor.Cut();
            }
            else
            {
                var rtb = tab.EditorControl.FindName("FormattedEditor") as RichTextBox;
                rtb?.Cut();
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            // Use custom copy handler (already wired in EditorControl)
            if (tab.ViewMode == EditorViewMode.Source)
            {
                tab.EditorControl.Editor.Copy();
            }
            else
            {
                var rtb = tab.EditorControl.FindName("FormattedEditor") as RichTextBox;
                rtb?.Copy();
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source)
            {
                tab.EditorControl.Editor.Paste();
            }
            else
            {
                var rtb = tab.EditorControl.FindName("FormattedEditor") as RichTextBox;
                rtb?.Paste();
            }
        }

        private void Find_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            var dialog = new Dialogs.FindReplaceDialog(tab.EditorControl);
            dialog.Owner = this;
            dialog.Show(); // Non-modal so user can interact with editor
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            var dialog = new Dialogs.FindReplaceDialog(tab.EditorControl);
            dialog.Owner = this;
            dialog.Show(); // Non-modal so user can interact with editor
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab != null)
            {
                Title = $"{tab.Document.DisplayName} - Work Notes";
                UpdateViewModeUI(tab.ViewMode);
            }
        }

        // View mode switching
        private void ViewModeToggle_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            // Toggle mode
            var newMode = tab.ViewMode == EditorViewMode.Formatted 
                ? EditorViewMode.Source 
                : EditorViewMode.Formatted;

            tab.ViewMode = newMode;
            tab.EditorControl.ViewMode = newMode;
            UpdateViewModeUI(newMode);

            StatusText.Text = $"Switched to {newMode} view";
        }

        private void BionicReading_Click(object sender, RoutedEventArgs e)
        {
            // Toggle bionic reading
            App.Settings.EnableBionicReading = !App.Settings.EnableBionicReading;
            App.Settings.Save();

            // Update menu checkmark
            MenuBionicReading.IsChecked = App.Settings.EnableBionicReading;

            // Refresh all tabs
            foreach (TabItem tabItem in TabControl.Items)
            {
                if (tabItem.Tag is DocumentTab tab && tab.EditorControl != null)
                {
                    tab.EditorControl.RefreshBionicReading();
                }
            }

            StatusText.Text = App.Settings.EnableBionicReading ? "Bionic Reading enabled" : "Bionic Reading disabled";
        }

        private void ViewFormatted_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            tab.ViewMode = EditorViewMode.Formatted;
            tab.EditorControl.ViewMode = EditorViewMode.Formatted;
            UpdateViewModeUI(EditorViewMode.Formatted);
            StatusText.Text = "Switched to Formatted view";
        }

        private void ViewSource_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl == null)
                return;

            tab.ViewMode = EditorViewMode.Source;
            tab.EditorControl.ViewMode = EditorViewMode.Source;
            UpdateViewModeUI(EditorViewMode.Source);
            StatusText.Text = "Switched to Source view";
        }

        private void UpdateViewModeUI(EditorViewMode mode)
        {
            if (mode == EditorViewMode.Formatted)
            {
                ViewModeIcon.Text = "Aa";
                ViewModeText.Text = "Formatted";
                MenuViewFormatted.IsChecked = true;
                MenuViewSource.IsChecked = false;
            }
            else
            {
                ViewModeIcon.Text = "</>";
                ViewModeText.Text = "Markdown";
                MenuViewFormatted.IsChecked = false;
                MenuViewSource.IsChecked = true;
            }

            // Update bionic reading menu check
            MenuBionicReading.IsChecked = App.Settings.EnableBionicReading;
        }

        // Theme switching
        private void Preferences_Click(object sender, RoutedEventArgs e)
        {
            if (App.SpellCheckService == null)
            {
                MessageBox.Show("Spell check service is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new WorkNotes.Dialogs.SettingsWindow(App.SpellCheckService)
            {
                Owner = this
            };

            dialog.ShowDialog();
        }

        // Settings change handler for live updates
        private void Settings_Changed(object? sender, SettingChangedEventArgs e)
        {
            switch (e.SettingName)
            {
                case "FontFamily":
                case "FontSize":
                case "WordWrap":
                    // Update all editors
                    foreach (var tab in _tabs)
                    {
                        tab.EditorControl?.ApplyFontSettings();
                    }
                    break;

                case "EnableSpellCheck":
                case "CustomDictionary":
                    // Refresh spellcheck in all editors
                    foreach (var tab in _tabs)
                    {
                        tab.EditorControl?.RefreshSpellCheck();
                    }
                    break;

                case "EnableBionicReading":
                case "BionicStrength":
                    // Refresh bionic reading in all editors
                    foreach (var tab in _tabs)
                    {
                        tab.EditorControl?.RefreshBionicReading();
                    }
                    // Update menu checkmark
                    MenuBionicReading.IsChecked = App.Settings.EnableBionicReading;
                    break;
            }
        }

        // Formatting commands
        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl != null)
            {
                tab.EditorControl.ApplyBold();
                StatusText.Text = "Applied bold formatting";
            }
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl != null)
            {
                tab.EditorControl.ApplyItalic();
                StatusText.Text = "Applied italic formatting";
            }
        }

        private void InsertLink_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab?.EditorControl != null)
            {
                var selectedText = tab.EditorControl.GetSelectedText();
                var dialog = new Dialogs.InsertLinkDialog(selectedText)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    tab.EditorControl.InsertLink(dialog.LinkUrl, dialog.LinkLabel);
                    StatusText.Text = $"Inserted link: {dialog.LinkUrl}";
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Check all tabs for unsaved changes
            foreach (var tab in _tabs.ToList())
            {
                if (tab.Document.IsDirty)
                {
                    // Switch to the dirty tab so user knows which file
                    var index = _tabs.IndexOf(tab);
                    TabControl.SelectedIndex = index;

                    if (!PromptSaveIfDirty(tab))
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            base.OnClosing(e);
        }
    }
}
