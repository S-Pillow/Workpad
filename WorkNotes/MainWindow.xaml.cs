using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        private Stack<ClosedTabInfo> _closedTabs = new Stack<ClosedTabInfo>();
        private const int MaxClosedTabsHistory = 10;
        private Dialogs.FindReplaceDialog? _findReplaceDialog;

        // Stored event handlers per-editor so they can be unsubscribed on tab close (prevents memory leaks)
        private readonly Dictionary<EditorControl, (EventHandler caretHandler, EventHandler textHandler, RoutedEventHandler selectionHandler)> _editorEventHandlers = new();

        // Drag-and-drop tab reorder state
        private Point _tabDragStartPoint;
        private bool _tabDragInProgress;

        // View mode state machine (owns window chrome + element visibility)
        // Why Hybrid: new MVVM-friendly service for new features; existing code-behind stays untouched.
        private readonly ViewModeManager _viewModeManager = new ViewModeManager();
        public ViewModeManager ViewModeManager => _viewModeManager;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize view mode manager (must be before XAML bindings resolve)
            _viewModeManager.Initialize(this);
            DataContext = this; // Allows XAML to bind to ViewModeManager via {Binding ViewModeManager.xxx}

            // Restore AlwaysOnTop from settings
            _viewModeManager.IsAlwaysOnTop = App.Settings.AlwaysOnTop;

            // Set up keyboard shortcuts (includes view mode shortcuts)
            SetupKeyboardShortcuts();

            // Esc handling: PreviewKeyDown so we can intercept before editors/dialogs if needed
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Subscribe to settings changes for live updates
            App.Settings.SettingChanged += Settings_Changed;

            // Restore session or create initial tab
            if (App.Settings.RestoreOpenTabs && !RestoreSession())
            {
                CreateNewTab();
            }
            else if (!App.Settings.RestoreOpenTabs)
            {
                CreateNewTab();
            }

            UpdateViewModeUI(App.Settings.DefaultEditorView);
            UpdateRecentFilesMenu();

            // Sync caption button state on load
            StateChanged += MainWindow_StateChanged;

            // Wire "+" new tab button from TabControl template once loaded
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Wire the "+" button inside the TabControl template
            if (TabControl.Template?.FindName("NewTabButton", TabControl) is Button newTabButton)
            {
                newTabButton.Click += (s, args) => CreateNewTab();
            }

            // Enable mouse-wheel horizontal scrolling on the tab strip
            if (TabControl.Template?.FindName("TabScrollViewer", TabControl) is ScrollViewer tabScroll)
            {
                tabScroll.PreviewMouseWheel += (s, args) =>
                {
                    tabScroll.ScrollToHorizontalOffset(
                        tabScroll.HorizontalOffset - args.Delta * 0.4);
                    args.Handled = true;
                };
            }

            // Wire drag-and-drop tab reorder on the TabControl
            TabControl.AllowDrop = true;
            TabControl.PreviewMouseLeftButtonDown += TabControl_PreviewMouseLeftButtonDown;
            TabControl.PreviewMouseMove += TabControl_PreviewMouseMove;
            TabControl.Drop += TabControl_Drop;
        }

        // --- Drag-and-drop tab reorder ---

        private void TabControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tabDragStartPoint = e.GetPosition(TabControl);
            _tabDragInProgress = false;
        }

        private void TabControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _tabDragInProgress)
                return;

            var pos = e.GetPosition(TabControl);
            var diff = pos - _tabDragStartPoint;

            // Only start drag after minimum distance (avoids accidental drags on click)
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // Find the TabItem being dragged
            var tabItem = FindAncestor<TabItem>((DependencyObject)e.OriginalSource);
            if (tabItem == null) return;

            // Don't drag if the source is a close button
            if (FindAncestor<Button>((DependencyObject)e.OriginalSource) != null)
                return;

            _tabDragInProgress = true;
            DragDrop.DoDragDrop(tabItem, tabItem, DragDropEffects.Move);
            _tabDragInProgress = false;
        }

        private void TabControl_Drop(object sender, DragEventArgs e)
        {
            var droppedTabItem = e.Data.GetData(typeof(TabItem)) as TabItem;
            if (droppedTabItem == null) return;

            // Find the TabItem we're dropping onto
            var targetTabItem = FindAncestor<TabItem>((DependencyObject)e.OriginalSource);
            if (targetTabItem == null || targetTabItem == droppedTabItem) return;

            var sourceIndex = TabControl.Items.IndexOf(droppedTabItem);
            var targetIndex = TabControl.Items.IndexOf(targetTabItem);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

            // Move in backing collection
            var tab = _tabs[sourceIndex];
            _tabs.RemoveAt(sourceIndex);
            _tabs.Insert(targetIndex, tab);

            // Move in TabControl.Items
            TabControl.Items.RemoveAt(sourceIndex);
            TabControl.Items.Insert(targetIndex, droppedTabItem);

            // Reselect the dragged tab
            TabControl.SelectedIndex = targetIndex;
        }

        /// <summary>Walks up the visual tree to find an ancestor of the specified type.</summary>
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // --- Esc key handling for view modes ---

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            // Safety: don't intercept Esc if Find/Replace dialog is open (let it close that first)
            if (_findReplaceDialog != null && _findReplaceDialog.IsLoaded)
                return;

            // Safety: if a modal dialog is active, Esc should close that, not exit view mode
            foreach (Window ownedWindow in OwnedWindows)
            {
                if (ownedWindow.IsActive)
                    return;
            }

            // Safety: if a context menu or popup is open, let Esc close that first
            // (PreviewKeyDown fires before the popup handles Esc, so we must yield)
            if (System.Windows.Input.FocusManager.GetFocusedElement(this) is DependencyObject focused)
            {
                var parent = focused;
                while (parent != null)
                {
                    if (parent is System.Windows.Controls.Primitives.Popup ||
                        parent is ContextMenu)
                        return;
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                }
            }

            // If we're in a special view mode, exit to Normal
            if (_viewModeManager.ShouldEscExitMode())
            {
                _viewModeManager.ApplyViewMode(Models.AppViewMode.Normal);
                e.Handled = true;
            }
        }

        // --- Custom title bar / caption button handlers ---

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Compensate for the hidden resize border when maximized
                RootBorder.Padding = new Thickness(7);
                MaximizeButton.ToolTip = "Restore Down";
                // Overlapping rectangles icon for restore
                MaximizeIcon.Data = System.Windows.Media.Geometry.Parse(
                    "M 2,0 L 10,0 L 10,8 L 8,8 L 8,10 L 0,10 L 0,2 L 2,2 Z M 2,2 L 8,2 L 8,8 L 2,8 Z");
            }
            else
            {
                RootBorder.Padding = new Thickness(0);
                MaximizeButton.ToolTip = "Maximize";
                // Simple rectangle icon for maximize
                MaximizeIcon.Data = System.Windows.Media.Geometry.Parse(
                    "M 0,0 L 10,0 L 10,10 L 0,10 Z");
            }
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

            // Ctrl+T - New Tab
            var newTabCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(newTabCommand, (s, e) => CreateNewTab()));
            InputBindings.Add(new KeyBinding(newTabCommand, Key.T, ModifierKeys.Control));

            // Ctrl+Shift+T - Reopen Closed Tab
            var reopenTabCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(reopenTabCommand, (s, e) => ReopenClosedTab_Click(s, e)));
            InputBindings.Add(new KeyBinding(reopenTabCommand, Key.T, ModifierKeys.Control | ModifierKeys.Shift));

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

            // F11 - Toggle Full Screen
            var fullScreenCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(fullScreenCommand, (s, e) => _viewModeManager.ToggleFullScreenCommand.Execute(null)));
            InputBindings.Add(new KeyBinding(fullScreenCommand, Key.F11, ModifierKeys.None));

            // F12 - Toggle Post-It Mode
            var postItCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(postItCommand, (s, e) => _viewModeManager.TogglePostItCommand.Execute(null)));
            InputBindings.Add(new KeyBinding(postItCommand, Key.F12, ModifierKeys.None));

            // Ctrl+Shift+F - Toggle Distraction Free Mode
            var distractionFreeCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(distractionFreeCommand, (s, e) => _viewModeManager.ToggleDistractionFreeCommand.Execute(null)));
            InputBindings.Add(new KeyBinding(distractionFreeCommand, Key.F, ModifierKeys.Control | ModifierKeys.Shift));

            // --- Tab navigation shortcuts ---

            // Ctrl+Tab - Next tab (wraps)
            var nextTabCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(nextTabCommand, (s, e) => SelectNextTab()));
            InputBindings.Add(new KeyBinding(nextTabCommand, Key.Tab, ModifierKeys.Control));

            // Ctrl+Shift+Tab - Previous tab (wraps)
            var prevTabCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(prevTabCommand, (s, e) => SelectPreviousTab()));
            InputBindings.Add(new KeyBinding(prevTabCommand, Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));

            // Ctrl+1..9 - Jump to tab by index (Ctrl+9 always goes to last tab)
            for (int i = 1; i <= 9; i++)
            {
                var tabIndex = i; // capture for closure
                var jumpCommand = new RoutedCommand();
                CommandBindings.Add(new CommandBinding(jumpCommand, (s, e) => SelectTabByNumber(tabIndex)));
                InputBindings.Add(new KeyBinding(jumpCommand, Key.D0 + tabIndex, ModifierKeys.Control));
            }

            // Ctrl+Shift+Left - Move tab left
            var moveLeftCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(moveLeftCommand, (s, e) => MoveCurrentTab(-1)));
            InputBindings.Add(new KeyBinding(moveLeftCommand, Key.Left, ModifierKeys.Control | ModifierKeys.Shift));

            // Ctrl+Shift+Right - Move tab right
            var moveRightCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(moveRightCommand, (s, e) => MoveCurrentTab(1)));
            InputBindings.Add(new KeyBinding(moveRightCommand, Key.Right, ModifierKeys.Control | ModifierKeys.Shift));
        }

        private DocumentTab? GetCurrentTab()
        {
            if (TabControl.SelectedIndex >= 0 && TabControl.SelectedIndex < _tabs.Count)
            {
                return _tabs[TabControl.SelectedIndex];
            }
            return null;
        }

        private void HookEditorEvents(EditorControl editor)
        {
            // Store delegates so they can be unsubscribed in UnhookEditorEvents
            EventHandler caretHandler = (s, e) =>
            {
                UpdateLineColIndicator();
                UpdateWordCount();
            };

            EventHandler textHandler = (s, e) => UpdateWordCount();

            RoutedEventHandler selectionHandler = (s, e) =>
            {
                UpdateLineColIndicator();
                UpdateWordCount();
            };

            editor.SourceEditor.TextArea.Caret.PositionChanged += caretHandler;
            editor.SourceEditor.TextChanged += textHandler;
            editor.FormattedEditor.SelectionChanged += selectionHandler;

            _editorEventHandlers[editor] = (caretHandler, textHandler, selectionHandler);
        }

        private void UnhookEditorEvents(EditorControl editor)
        {
            if (_editorEventHandlers.TryGetValue(editor, out var handlers))
            {
                editor.SourceEditor.TextArea.Caret.PositionChanged -= handlers.caretHandler;
                editor.SourceEditor.TextChanged -= handlers.textHandler;
                editor.FormattedEditor.SelectionChanged -= handlers.selectionHandler;
                _editorEventHandlers.Remove(editor);
            }
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

            // Hook editor events for status bar updates
            HookEditorEvents(editor);

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

            // Wire up close button.
            // Use a flag to prevent stacking duplicate handlers on each Loaded event
            // (Loaded fires every time the element re-enters the visual tree).
            bool closeButtonWired = false;
            tabItem.Loaded += (s, e) =>
            {
                if (closeButtonWired) return;
                var closeButton = FindVisualChild<Button>(tabItem);
                if (closeButton != null)
                {
                    closeButtonWired = true;
                    closeButton.Click += (sender, args) =>
                    {
                        args.Handled = true; // Prevent tab selection
                        CloseTab(tab);
                    };
                }
            };

            // Middle-click closes tab
            tabItem.PreviewMouseDown += (s, e) =>
            {
                if (e.MiddleButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                    CloseTab(tab);
                }
            };

            _tabs.Add(tab);
            TabControl.Items.Add(tabItem);
            TabControl.SelectedItem = tabItem;
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

                // Hook editor events for status bar updates
                HookEditorEvents(editor);

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

                // Wire up close button (with duplicate prevention)
                bool closeButtonWired = false;
                tabItem.Loaded += (s, e) =>
                {
                    if (closeButtonWired) return;
                    var closeButton = FindVisualChild<Button>(tabItem);
                    if (closeButton != null)
                    {
                        closeButtonWired = true;
                        closeButton.Click += (sender, args) =>
                        {
                            args.Handled = true;
                            CloseTab(tab);
                        };
                    }
                };

                // Middle-click closes tab
                tabItem.PreviewMouseDown += (s, e) =>
                {
                    if (e.MiddleButton == MouseButtonState.Pressed)
                    {
                        e.Handled = true;
                        CloseTab(tab);
                    }
                };

                _tabs.Add(tab);
                TabControl.Items.Add(tabItem);
                TabControl.SelectedItem = tabItem;

                // Add to recent files
                App.Settings.AddRecentFile(filePath);
                UpdateRecentFilesMenu();
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

            // Add to closed tabs history
            AddToClosedTabsHistory(tab);

            var index = _tabs.IndexOf(tab);
            if (index >= 0)
            {
                // Unsubscribe MainWindow's event handlers, then EditorControl's own cleanup
                if (tab.EditorControl != null)
                    UnhookEditorEvents(tab.EditorControl);
                tab.EditorControl?.Cleanup();
                tab.SplitViewContainer?.Dispose();

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
            }
        }

        // --- Tab navigation and reorder methods (Phase 2) ---

        /// <summary>Selects the next tab, wrapping to the first if at the end.</summary>
        private void SelectNextTab()
        {
            if (_tabs.Count <= 1) return;
            var next = (TabControl.SelectedIndex + 1) % _tabs.Count;
            TabControl.SelectedIndex = next;
        }

        /// <summary>Selects the previous tab, wrapping to the last if at the beginning.</summary>
        private void SelectPreviousTab()
        {
            if (_tabs.Count <= 1) return;
            var prev = (TabControl.SelectedIndex - 1 + _tabs.Count) % _tabs.Count;
            TabControl.SelectedIndex = prev;
        }

        /// <summary>Jumps to tab by 1-based number. Ctrl+9 always goes to the last tab.</summary>
        private void SelectTabByNumber(int number)
        {
            if (_tabs.Count == 0) return;
            if (number == 9 || number > _tabs.Count)
            {
                // Ctrl+9 always selects last tab, as does any number beyond count
                TabControl.SelectedIndex = _tabs.Count - 1;
            }
            else
            {
                TabControl.SelectedIndex = number - 1;
            }
        }

        /// <summary>
        /// Moves the current tab left (-1) or right (+1) in the tab strip.
        /// Updates both _tabs and TabControl.Items to keep them in sync.
        /// </summary>
        private void MoveCurrentTab(int direction)
        {
            var currentIndex = TabControl.SelectedIndex;
            if (currentIndex < 0 || _tabs.Count <= 1) return;

            var newIndex = currentIndex + direction;
            if (newIndex < 0 || newIndex >= _tabs.Count) return;

            // Swap in backing collection
            var tab = _tabs[currentIndex];
            _tabs.RemoveAt(currentIndex);
            _tabs.Insert(newIndex, tab);

            // Swap in TabControl.Items (must match)
            var tabItem = (TabItem)TabControl.Items[currentIndex];
            TabControl.Items.RemoveAt(currentIndex);
            TabControl.Items.Insert(newIndex, tabItem);

            // Reselect the moved tab
            TabControl.SelectedIndex = newIndex;
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
                // Handle both single and split view
                if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
                {
                    tab.SplitViewContainer.SaveToDocument();
                }
                else if (tab.EditorControl != null)
                {
                    tab.EditorControl.SaveToDocument();
                }
                
                // Add to recent files
                if (!string.IsNullOrEmpty(tab.Document.FilePath))
                {
                    App.Settings.AddRecentFile(tab.Document.FilePath);
                    UpdateRecentFilesMenu();
                }
                
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
            var editor = tab?.GetActiveEditorControl();
            if (tab == null || editor == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source && editor.Editor.CanUndo)
            {
                editor.Editor.Undo();
            }
            else if (tab.ViewMode == EditorViewMode.Formatted)
            {
                var rtb = editor.GetFormattedEditorControl();
                if (rtb?.CanUndo == true)
                {
                    rtb.Undo();
                }
            }
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (tab == null || editor == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source && editor.Editor.CanRedo)
            {
                editor.Editor.Redo();
            }
            else if (tab.ViewMode == EditorViewMode.Formatted)
            {
                var rtb = editor.GetFormattedEditorControl();
                if (rtb?.CanRedo == true)
                {
                    rtb.Redo();
                }
            }
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (tab == null || editor == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source)
            {
                editor.Editor.Cut();
            }
            else
            {
                var rtb = editor.GetFormattedEditorControl();
                rtb?.Cut();
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (tab == null || editor == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source)
            {
                editor.Editor.Copy();
            }
            else
            {
                var rtb = editor.GetFormattedEditorControl();
                rtb?.Copy();
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (tab == null || editor == null)
                return;

            if (tab.ViewMode == EditorViewMode.Source)
            {
                editor.Editor.Paste();
            }
            else
            {
                var rtb = editor.GetFormattedEditorControl();
                rtb?.Paste();
            }
        }

        private void Find_Click(object sender, RoutedEventArgs e)
        {
            ShowFindReplaceDialog();
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            ShowFindReplaceDialog();
        }

        /// <summary>
        /// Shows a single Find/Replace dialog, reusing an existing one if open.
        /// Previously each Ctrl+F/Ctrl+H created a new dialog instance,
        /// allowing unlimited stacked dialogs.
        /// </summary>
        private void ShowFindReplaceDialog()
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (editor == null)
                return;

            if (_findReplaceDialog != null && _findReplaceDialog.IsLoaded)
            {
                _findReplaceDialog.UpdateEditor(editor);
                _findReplaceDialog.Activate();
                return;
            }

            _findReplaceDialog = new Dialogs.FindReplaceDialog(editor);
            _findReplaceDialog.Owner = this;
            _findReplaceDialog.Closed += (s, args) => _findReplaceDialog = null;
            _findReplaceDialog.Show();
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab != null)
            {
                Title = $"{tab.Document.DisplayName} - Work Notes";
                UpdateViewModeUI(tab.ViewMode);
                UpdateLineColIndicator();
                UpdateWordCount();
                UpdateStatusIndicators();

                // Keep the Find/Replace dialog in sync with the active tab's editor
                if (_findReplaceDialog != null && _findReplaceDialog.IsLoaded && tab.EditorControl != null)
                {
                    _findReplaceDialog.UpdateEditor(tab.EditorControl);
                }
            }
        }

        // View mode switching
        private void ViewModeToggle_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab == null)
                return;

            // Toggle mode
            var newMode = tab.ViewMode == EditorViewMode.Formatted 
                ? EditorViewMode.Source 
                : EditorViewMode.Formatted;

            tab.ViewMode = newMode;
            
            // Handle both single and split view
            if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
            {
                tab.SplitViewContainer.SwitchViewMode(newMode);
            }
            else if (tab.EditorControl != null)
            {
                tab.EditorControl.ViewMode = newMode;
            }
            
            UpdateViewModeUI(newMode);
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
        }

        private void ViewFormatted_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab == null)
                return;

            tab.ViewMode = EditorViewMode.Formatted;
            
            // Handle both single and split view
            if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
            {
                tab.SplitViewContainer.SwitchViewMode(EditorViewMode.Formatted);
            }
            else if (tab.EditorControl != null)
            {
                tab.EditorControl.ViewMode = EditorViewMode.Formatted;
            }
            
            UpdateViewModeUI(EditorViewMode.Formatted);
        }

        private void ViewSource_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab == null)
                return;

            tab.ViewMode = EditorViewMode.Source;
            
            // Handle both single and split view
            if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
            {
                tab.SplitViewContainer.SwitchViewMode(EditorViewMode.Source);
            }
            else if (tab.EditorControl != null)
            {
                tab.EditorControl.ViewMode = EditorViewMode.Source;
            }
            
            UpdateViewModeUI(EditorViewMode.Source);
        }

        private void UpdateViewModeUI(EditorViewMode mode)
        {
            if (mode == EditorViewMode.Formatted)
            {
                ViewModeIcon.Text = "Aa";
                ViewModeText.Text = "Formatted";
                ViewModeToggle.ToolTip = "Switch to Markdown view (Ctrl+Shift+M)";
                MenuViewFormatted.IsChecked = true;
                MenuViewSource.IsChecked = false;
            }
            else
            {
                ViewModeIcon.Text = "</>";
                ViewModeText.Text = "Markdown";
                ViewModeToggle.ToolTip = "Switch to Formatted view (Ctrl+Shift+M)";
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
                    // Update all editors (both single and split view)
                    foreach (var tab in _tabs)
                    {
                        if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
                        {
                            // Apply to both panes in split view
                            tab.SplitViewContainer.TopPane?.EditorControl?.ApplyFontSettings();
                            tab.SplitViewContainer.BottomPane?.EditorControl?.ApplyFontSettings();
                        }
                        else
                        {
                            tab.EditorControl?.ApplyFontSettings();
                        }
                    }
                    break;

                case "EnableSpellCheck":
                case "CustomDictionary":
                    // Refresh spellcheck in all editors (both single and split view)
                    foreach (var tab in _tabs)
                    {
                        if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
                        {
                            // Apply to both panes in split view
                            tab.SplitViewContainer.TopPane?.EditorControl?.RefreshSpellCheck();
                            tab.SplitViewContainer.BottomPane?.EditorControl?.RefreshSpellCheck();
                        }
                        else
                        {
                            tab.EditorControl?.RefreshSpellCheck();
                        }
                    }
                    UpdateStatusIndicators();
                    break;

                case "EnableBionicReading":
                case "BionicStrength":
                    // Refresh bionic reading in all editors (both single and split view)
                    foreach (var tab in _tabs)
                    {
                        if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
                        {
                            // Apply to both panes in split view
                            tab.SplitViewContainer.TopPane?.EditorControl?.RefreshBionicReading();
                            tab.SplitViewContainer.BottomPane?.EditorControl?.RefreshBionicReading();
                        }
                        else
                        {
                            tab.EditorControl?.RefreshBionicReading();
                        }
                    }
                    // Update menu checkmark
                    MenuBionicReading.IsChecked = App.Settings.EnableBionicReading;
                    UpdateStatusIndicators();
                    break;
            }
        }

        // Split View toggle
        private void SplitView_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab == null) return;

            // Check Pro feature access
            if (!Services.ProFeatures.SplitViewEnabled)
            {
                Services.ProFeatures.ShowUpgradeDialog("Split View");
                MenuSplitView.IsChecked = false;
                return;
            }

            // Toggle split view
            bool enableSplit = !tab.IsSplitViewEnabled;

            if (enableSplit)
            {
                // Enable split view
                EnableSplitViewForTab(tab);
            }
            else
            {
                // Disable split view
                DisableSplitViewForTab(tab);
            }

            MenuSplitView.IsChecked = enableSplit;
            UpdateStatusIndicators();
        }

        private void EnableSplitViewForTab(DocumentTab tab)
        {
            if (tab.IsSplitViewEnabled) return;

            try
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] EnableSplitViewForTab starting...");
                
                // Sync current editor content to document (but don't save to disk if unsaved)
                if (tab.EditorControl != null)
                {
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Syncing current editor content to document...");
                    // Get current text and update document content (in-memory only)
                    var currentText = tab.EditorControl.GetText();
                    tab.Document.Content = currentText;
                    // Only save to disk if document has a file path
                    if (!string.IsNullOrEmpty(tab.Document.FilePath))
                    {
                        tab.EditorControl.SaveToDocument();
                    }
                }

                // Create split view container
                System.Diagnostics.Debug.WriteLine("[MainWindow] Creating SplitViewContainer...");
                var splitContainer = new SplitViewContainer();
                
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Initializing with document and view mode {tab.ViewMode}...");
                splitContainer.Initialize(tab.Document, tab.ViewMode);

                // Replace single editor with split container in tab
                var tabItem = TabControl.Items[_tabs.IndexOf(tab)] as TabItem;
                if (tabItem != null)
                {
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Replacing editor with split container...");
                    
                    // Remove old editor
                    if (tab.EditorControl != null)
                    {
                        tabItem.Content = null;
                    }

                    // Add split container
                    tabItem.Content = splitContainer;
                    tab.SplitViewContainer = splitContainer;
                    tab.IsSplitViewEnabled = true;
                    
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Split view enabled successfully!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] ERROR enabling split view: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error enabling split view:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                    "Split View Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Revert menu state
                MenuSplitView.IsChecked = false;
            }
        }

        private void DisableSplitViewForTab(DocumentTab tab)
        {
            if (!tab.IsSplitViewEnabled || tab.SplitViewContainer == null) return;

            // Save from split view (only if document has a file path)
            if (!string.IsNullOrEmpty(tab.Document.FilePath))
            {
                tab.SplitViewContainer.SaveToDocument();
            }

            // Create single editor
            var editor = new EditorControl
            {
                Document = tab.Document,
                ViewMode = tab.ViewMode
            };

            // Replace split container with single editor
            var tabItem = TabControl.Items[_tabs.IndexOf(tab)] as TabItem;
            if (tabItem != null)
            {
                tab.SplitViewContainer.Dispose();
                tabItem.Content = editor;
                tab.EditorControl = editor;
                tab.SplitViewContainer = null;
                tab.IsSplitViewEnabled = false;
            }
        }

        // Formatting commands
        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (editor != null)
            {
                editor.ApplyBold();
            }
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (editor != null)
            {
                editor.ApplyItalic();
            }
        }

        private void InsertLink_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (editor != null)
            {
                var selectedText = editor.GetSelectedText();
                var dialog = new Dialogs.InsertLinkDialog(selectedText)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    editor.InsertLink(dialog.LinkUrl, dialog.LinkLabel);
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

            // Save session if enabled
            if (App.Settings.RestoreOpenTabs)
            {
                SaveCurrentSession();
            }
            else
            {
                AppSettings.ClearSession();
            }

            // Unsubscribe events to break references
            App.Settings.SettingChanged -= Settings_Changed;
            StateChanged -= MainWindow_StateChanged;
            this.Loaded -= MainWindow_Loaded;
            PreviewKeyDown -= MainWindow_PreviewKeyDown;
            TabControl.PreviewMouseLeftButtonDown -= TabControl_PreviewMouseLeftButtonDown;
            TabControl.PreviewMouseMove -= TabControl_PreviewMouseMove;
            TabControl.Drop -= TabControl_Drop;

            // Clean up all tabs (stop timers, unsubscribe events)
            foreach (var tab in _tabs)
            {
                tab.EditorControl?.Cleanup();
                tab.SplitViewContainer?.Dispose();
            }

            base.OnClosing(e);
        }

        // Session management
        private void SaveCurrentSession()
        {
            var session = new TabSession
            {
                Tabs = new List<TabSessionState>(),
                ActiveTabIndex = TabControl.SelectedIndex
            };

            foreach (var tab in _tabs)
            {
                // Only save tabs with file paths (skip unsaved Untitled tabs)
                if (!string.IsNullOrEmpty(tab.Document.FilePath))
                {
                    var state = new TabSessionState
                    {
                        FilePath = tab.Document.FilePath,
                        ViewMode = tab.ViewMode,
                        CursorPosition = tab.EditorControl?.Editor.CaretOffset ?? 0,
                        ScrollOffset = tab.EditorControl?.Editor.VerticalOffset ?? 0
                    };
                    session.Tabs.Add(state);
                }
            }

            AppSettings.SaveSession(session);
        }

        private bool RestoreSession()
        {
            var session = AppSettings.LoadSession();
            if (session == null || session.Tabs.Count == 0)
                return false;

            var anyTabOpened = false;

            foreach (var tabState in session.Tabs)
            {
                if (string.IsNullOrEmpty(tabState.FilePath))
                    continue;

                // Check if file still exists
                if (!File.Exists(tabState.FilePath))
                {
                    // Remove from recent files if missing
                    App.Settings.RemoveRecentFile(tabState.FilePath);
                    continue;
                }

                try
                {
                    OpenFileInNewTab(tabState.FilePath);
                    
                    var tab = _tabs.LastOrDefault();
                    if (tab != null && tab.EditorControl != null)
                    {
                        // Restore view mode
                        tab.ViewMode = tabState.ViewMode;
                        tab.EditorControl.ViewMode = tabState.ViewMode;
                        
                        // Restore cursor position (delay to ensure editor is loaded)
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (tab.EditorControl.Editor.Text.Length >= tabState.CursorPosition)
                            {
                                tab.EditorControl.Editor.CaretOffset = tabState.CursorPosition;
                                tab.EditorControl.Editor.ScrollToVerticalOffset(tabState.ScrollOffset);
                            }
                        }, System.Windows.Threading.DispatcherPriority.Loaded);
                    }

                    anyTabOpened = true;
                }
                catch
                {
                    // Skip tabs that fail to open
                }
            }

            // Restore active tab
            if (anyTabOpened && session.ActiveTabIndex >= 0 && session.ActiveTabIndex < TabControl.Items.Count)
            {
                TabControl.SelectedIndex = session.ActiveTabIndex;
            }

            return anyTabOpened;
        }

        // Recent files menu
        private void UpdateRecentFilesMenu()
        {
            RecentFilesMenu.Items.Clear();

            if (App.Settings.RecentFiles.Count == 0)
            {
                RecentFilesMenu.Items.Add(NoRecentFilesPlaceholder);
                return;
            }

            foreach (var filePath in App.Settings.RecentFiles)
            {
                var menuItem = new MenuItem
                {
                    Header = filePath,
                    Tag = filePath
                };
                menuItem.Click += RecentFile_Click;
                RecentFilesMenu.Items.Add(menuItem);
            }
        }

        private void RecentFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string filePath)
            {
                if (!File.Exists(filePath))
                {
                    var result = MessageBox.Show(
                        $"File not found:\n{filePath}\n\nRemove from recent files?",
                        "File Not Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Settings.RemoveRecentFile(filePath);
                        UpdateRecentFilesMenu();
                    }
                    return;
                }

                OpenFileInNewTab(filePath);
            }
        }

        // Closed tab management
        private void ReopenClosedTab_Click(object sender, RoutedEventArgs e)
        {
            if (_closedTabs.Count == 0)
            {
                return;
            }

            var closedTab = _closedTabs.Pop();
            UpdateReopenMenuState();

            if (!string.IsNullOrEmpty(closedTab.FilePath))
            {
                // Reopen saved file
                if (File.Exists(closedTab.FilePath))
                {
                    OpenFileInNewTab(closedTab.FilePath);
                    var tab = _tabs.LastOrDefault();
                    if (tab != null)
                    {
                        tab.ViewMode = closedTab.ViewMode;
                        if (tab.EditorControl != null)
                        {
                            tab.EditorControl.ViewMode = closedTab.ViewMode;
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"File no longer exists:\n{closedTab.FilePath}",
                        "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (!string.IsNullOrEmpty(closedTab.Content))
            {
                // Reopen untitled tab with content
                CreateNewTab();
                var tab = _tabs.LastOrDefault();
                if (tab?.EditorControl != null)
                {
                    tab.Document.Content = closedTab.Content;
                    tab.EditorControl.Document = tab.Document;
                    tab.ViewMode = closedTab.ViewMode;
                    tab.EditorControl.ViewMode = closedTab.ViewMode;
                }
            }
        }

        private void AddToClosedTabsHistory(DocumentTab tab)
        {
            var closedInfo = new ClosedTabInfo
            {
                FilePath = tab.Document.FilePath,
                Content = string.IsNullOrEmpty(tab.Document.FilePath) ? tab.Document.Content : null,
                ViewMode = tab.ViewMode,
                ClosedAt = DateTime.Now
            };

            _closedTabs.Push(closedInfo);

            // Limit history size
            while (_closedTabs.Count > MaxClosedTabsHistory)
            {
                var items = _closedTabs.ToList();
                items.RemoveAt(items.Count - 1);
                _closedTabs = new Stack<ClosedTabInfo>(items.AsEnumerable().Reverse());
            }

            UpdateReopenMenuState();
        }

        private void UpdateReopenMenuState()
        {
            ReopenClosedTabMenu.IsEnabled = _closedTabs.Count > 0;
        }

        // Zoom controls
        private double _zoomLevel = 1.0;
        private const double ZoomMin = 0.5;
        private const double ZoomMax = 3.0;
        private const double ZoomStep = 0.1;

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomLevel < ZoomMax)
            {
                _zoomLevel += ZoomStep;
                ApplyZoom();
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomLevel > ZoomMin)
            {
                _zoomLevel -= ZoomStep;
                ApplyZoom();
            }
        }

        private void ApplyZoom()
        {
            var tab = GetCurrentTab();
            if (tab?.GetActiveEditorControl() != null)
            {
                var editor = tab.GetActiveEditorControl()!;
                editor.SourceEditor.FontSize = App.Settings.FontSize * _zoomLevel;
                editor.FormattedEditor.FontSize = App.Settings.FontSize * _zoomLevel;
            }

            ZoomText.Text = $"{(int)(_zoomLevel * 100)}%";
        }

        // Line/Column tracking
        private void UpdateLineColIndicator()
        {
            var tab = GetCurrentTab();
            if (tab?.GetActiveEditorControl() != null)
            {
                var editor = tab.GetActiveEditorControl()!;
                if (editor.ViewMode == EditorViewMode.Source)
                {
                    var line = editor.SourceEditor.TextArea.Caret.Line;
                    var col = editor.SourceEditor.TextArea.Caret.Column;
                    LineColText.Text = $"Ln {line}, Col {col}";
                }
                else
                {
                    // For formatted view, calculate line and column
                    var caret = editor.FormattedEditor.CaretPosition;
                    var paragraph = caret.Paragraph;
                    if (paragraph != null)
                    {
                        // Calculate line number (which paragraph)
                        var lineNumber = 1;
                        var block = editor.FormattedEditor.Document.Blocks.FirstBlock;
                        while (block != null && block != paragraph)
                        {
                            lineNumber++;
                            block = block.NextBlock;
                        }
                        
                        // Calculate column (character offset within paragraph)
                        var paragraphStart = paragraph.ContentStart;
                        var column = 1;
                        if (paragraphStart != null && caret != null)
                        {
                            var textRange = new TextRange(paragraphStart, caret);
                            // Add 1 because columns are 1-based
                            column = textRange.Text.Length + 1;
                        }
                        
                        LineColText.Text = $"Ln {lineNumber}, Col {column}";
                    }
                    else
                    {
                        LineColText.Text = "Ln 1, Col 1";
                    }
                }
            }
            else
            {
                LineColText.Text = "Ln 1, Col 1";
            }
        }

        private void UpdateWordCount()
        {
            var tab = GetCurrentTab();
            if (tab?.GetActiveEditorControl() != null)
            {
                var editor = tab.GetActiveEditorControl()!;
                string text = "";
                
                if (editor.ViewMode == EditorViewMode.Source)
                {
                    text = editor.SourceEditor.Text;
                }
                else
                {
                    var textRange = new TextRange(editor.FormattedEditor.Document.ContentStart, editor.FormattedEditor.Document.ContentEnd);
                    text = textRange.Text;
                }
                
                var wordCount = string.IsNullOrWhiteSpace(text) ? 0 :
                    text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                
                WordCountText.Text = wordCount == 1 ? "1 word" : $"{wordCount} words";
                
                // Update save state indicator
                SaveStateIndicator.Visibility = tab.Document.IsDirty ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                WordCountText.Text = "0 words";
                SaveStateIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void LineColButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for "Go to line" dialog functionality
            // Future: implement a simple input dialog to jump to a specific line
            var tab = GetCurrentTab();
            if (tab?.GetActiveEditorControl() != null)
            {
                var editor = tab.GetActiveEditorControl()!;
                // For now, just focus the editor
                if (editor.ViewMode == EditorViewMode.Source)
                {
                    editor.SourceEditor.Focus();
                }
                else
                {
                    editor.FormattedEditor.Focus();
                }
            }
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            _zoomLevel = 1.0;
            ApplyZoom();
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            // Smart toggle: if system theme, switch to explicit opposite
            var currentTheme = App.Settings.ThemeMode;
            var effectiveTheme = ThemeManager.GetEffectiveTheme(currentTheme);
            
            // Toggle to opposite
            var newTheme = effectiveTheme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
            App.Settings.ThemeMode = newTheme;
            
            // Apply the theme immediately
            ThemeManager.ApplyTheme(newTheme);
        }

        // Status indicators
        private void UpdateStatusIndicators()
        {
            // Spell check indicator
            SpellCheckIndicator.Visibility = App.Settings.EnableSpellCheck 
                ? Visibility.Visible 
                : Visibility.Collapsed;

            // Bionic reading indicator
            BionicIndicator.Visibility = App.Settings.EnableBionicReading 
                ? Visibility.Visible 
                : Visibility.Collapsed;

            // Split view indicator
            var tab = GetCurrentTab();
            SplitViewIndicator.Visibility = (tab?.IsSplitViewEnabled == true) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
    }
}
