using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using WorkNotes.Controls;
using WorkNotes.Models;
using ThemeMode = WorkNotes.Models.ThemeMode;
using WorkNotes.Services;

namespace WorkNotes
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<DocumentTab> _tabs = new ObservableCollection<DocumentTab>();
        private Stack<ClosedTabInfo> _closedTabs = new Stack<ClosedTabInfo>();
        private const int MaxClosedTabsHistory = 10;
        private Dialogs.FindReplaceDialog? _findReplaceDialog;

        // Workpad 2.x services: session recovery, external file monitoring,
        // command palette registry, transient status messaging.
        private readonly SessionRecoveryService _recoveryService = new();
        private ExternalFileMonitor? _fileMonitor;
        private readonly CommandRegistry _commandRegistry = new();
        private readonly Dictionary<DocumentTab, (string Path, Document Document)> _fileConflicts = new();
        private System.Windows.Threading.DispatcherTimer? _statusMessageTimer;
        private System.Windows.Threading.DispatcherTimer? _recoveryIdleTimer;
        private System.Windows.Threading.DispatcherTimer? _recoveryMaxTimer;
        private bool _normalizeXmlCompare;

        // Stored event handlers per-editor so they can be unsubscribed on tab close (prevents memory leaks)
        private readonly Dictionary<EditorControl, (EventHandler caretHandler, EventHandler textHandler, TextChangedEventHandler formattedTextHandler, RoutedEventHandler selectionHandler)> _editorEventHandlers = new();

        // Word-count debounce: recounted on content change only, after a short
        // idle — never on caret or selection movement (WP-01-AC03).
        private readonly System.Windows.Threading.DispatcherTimer _wordCountTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };

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

            _wordCountTimer.Tick += (_, _) =>
            {
                _wordCountTimer.Stop();
                UpdateWordCount();
            };

            // External file monitoring (PRD SYNC-01..06)
            _fileMonitor = new ExternalFileMonitor(Dispatcher);
            _fileMonitor.FileChanged += OnExternalFileChanged;
            _fileMonitor.FileDeleted += OnExternalFileDeleted;

            // Command palette registry
            RegisterCommands();

            // Recovery snapshots (PRD RECOV-01): triggered ~2s after the last
            // edit (snapshots normally run while the typist pauses) with a 30s
            // ceiling so continuous typing can't leave long stretches
            // unrecovered. Both feed the same coalescing background writer.
            _recoveryIdleTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _recoveryIdleTimer.Tick += (_, _) =>
            {
                _recoveryIdleTimer.Stop();
                SaveRecoverySnapshots();
            };
            _recoveryMaxTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _recoveryMaxTimer.Tick += (_, _) => SaveRecoverySnapshots();
            _recoveryMaxTimer.Start();

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

        /// <summary>
        /// Applies Windows 11 native rounded window corners via DWM.
        /// Safe no-op on Windows 10 and earlier (unsupported attribute is ignored).
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int cornerPreference = 2; // DWMWCP_ROUND
                _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */,
                    ref cornerPreference, sizeof(int));
            }
            catch
            {
                // Older Windows: keep square corners.
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Wire the "+" button inside the TabControl template
            if (TabControl.Template?.FindName("NewTabButton", TabControl) is Button newTabButton)
            {
                newTabButton.Click += (s, args) => CreateNewTab();
            }

            // Enable mouse-wheel horizontal scrolling on the tab strip,
            // plus browser-style double-click on empty strip space to open a new tab
            if (TabControl.Template?.FindName("TabScrollViewer", TabControl) is ScrollViewer tabScroll)
            {
                tabScroll.PreviewMouseWheel += (s, args) =>
                {
                    tabScroll.ScrollToHorizontalOffset(
                        tabScroll.HorizontalOffset - args.Delta * 0.4);
                    args.Handled = true;
                };

                tabScroll.PreviewMouseLeftButtonDown += (s, args) =>
                {
                    if (args.ClickCount == 2 &&
                        args.OriginalSource is DependencyObject source &&
                        FindAncestor<TabItem>(source) == null &&
                        FindAncestor<Button>(source) == null)
                    {
                        args.Handled = true;
                        CreateNewTab();
                    }
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
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] filePaths)
            {
                foreach (var filePath in filePaths.Where(File.Exists))
                    OpenFileInNewTab(filePath);

                e.Handled = true;
                return;
            }

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

        /// <summary>Walks up the visual/logical tree to find an ancestor of the specified type.</summary>
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                // VisualTreeHelper.GetParent throws on non-Visual elements such as
                // FlowDocument content, so those walk up via the logical tree instead.
                current = current is System.Windows.Media.Visual visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(visual)
                    : LogicalTreeHelper.GetParent(current);
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

            // Ctrl+G - Go to line
            var goToLineCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(goToLineCommand, (s, e) => LineColButton_Click(s, e)));
            InputBindings.Add(new KeyBinding(goToLineCommand, Key.G, ModifierKeys.Control));

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

            // --- Zoom shortcuts ---
            // These were advertised in the status-bar tooltips but never actually bound;
            // only the buttons worked. Both the number-row and numpad keys are registered
            // because Windows reports them as different keys.
            var zoomInCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(zoomInCommand, (s, e) => ZoomIn_Click(s, e)));
            InputBindings.Add(new KeyBinding(zoomInCommand, Key.OemPlus, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(zoomInCommand, Key.Add, ModifierKeys.Control));

            var zoomOutCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(zoomOutCommand, (s, e) => ZoomOut_Click(s, e)));
            InputBindings.Add(new KeyBinding(zoomOutCommand, Key.OemMinus, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(zoomOutCommand, Key.Subtract, ModifierKeys.Control));

            var zoomResetCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(zoomResetCommand, (s, e) => ZoomReset_Click(s, e)));
            InputBindings.Add(new KeyBinding(zoomResetCommand, Key.D0, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(zoomResetCommand, Key.NumPad0, ModifierKeys.Control));

            // Ctrl+Shift+Left - Move tab left
            var moveLeftCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(moveLeftCommand, (s, e) => MoveCurrentTab(-1)));
            InputBindings.Add(new KeyBinding(moveLeftCommand, Key.Left, ModifierKeys.Control | ModifierKeys.Shift));

            // Ctrl+Shift+Right - Move tab right
            var moveRightCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(moveRightCommand, (s, e) => MoveCurrentTab(1)));

            // Ctrl+Shift+P = Command Palette
            var paletteCommand = new RoutedUICommand("Command Palette", "CommandPalette", typeof(MainWindow));
            InputBindings.Add(new KeyBinding(paletteCommand, Key.P, ModifierKeys.Control | ModifierKeys.Shift));
            CommandBindings.Add(new CommandBinding(paletteCommand, (s, e) => CommandPalette_Click(s, e)));
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
            // Store delegates so they can be unsubscribed in UnhookEditorEvents.
            // Word count is content-revision-driven only (WP-01): caret and
            // selection movement must not trigger a whole-document recount.
            EventHandler caretHandler = (s, e) =>
            {
                UpdateLineColIndicator();
                OnRecoveryActivity();
            };

            EventHandler textHandler = (s, e) =>
            {
                _wordCountTimer.Stop();
                _wordCountTimer.Start();
                OnRecoveryActivity();
            };

            TextChangedEventHandler formattedTextHandler = (s, e) =>
            {
                _wordCountTimer.Stop();
                _wordCountTimer.Start();
            };

            RoutedEventHandler selectionHandler = (s, e) =>
            {
                UpdateLineColIndicator();
            };

            editor.SourceEditor.TextArea.Caret.PositionChanged += caretHandler;
            editor.SourceEditor.TextChanged += textHandler;
            editor.FormattedEditor.TextChanged += formattedTextHandler;
            editor.FormattedEditor.SelectionChanged += selectionHandler;

            _editorEventHandlers[editor] = (caretHandler, textHandler, formattedTextHandler, selectionHandler);
        }

        private void UnhookEditorEvents(EditorControl editor)
        {
            if (_editorEventHandlers.TryGetValue(editor, out var handlers))
            {
                editor.SourceEditor.TextArea.Caret.PositionChanged -= handlers.caretHandler;
                editor.SourceEditor.TextChanged -= handlers.textHandler;
                editor.FormattedEditor.TextChanged -= handlers.formattedTextHandler;
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
            WireTechnicalCommands(editor);

            // Create TabItem (shared factory wires header, tooltip, close, context menu)
            var tabItem = CreateTabItem(tab, editor);

            _tabs.Add(tab);
            TabControl.Items.Add(tabItem);
            TabControl.SelectedItem = tabItem;
        }

        /// <summary>
        /// Builds a fully-wired TabItem for a DocumentTab: header binding, file-path tooltip,
        /// close button, middle-click close, and right-click tab context menu.
        /// </summary>
        private TabItem CreateTabItem(DocumentTab tab, EditorControl editor)
        {
            var host = new SecondaryPaneHost();
            WireSecondaryPane(tab, host);
            tab.PaneHost = host;

            // Content wrapper: editor on top, secondary pane host below (compare,
            // conflict banner). Closing the pane returns to the simple layout.
            var wrapper = new Grid();
            wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wrapper.Children.Add(editor);
            Grid.SetRow(host, 1);
            wrapper.Children.Add(host);

            var tabItem = new TabItem
            {
                Content = wrapper,
                Tag = tab
            };

            // Bind header to document name
            var binding = new System.Windows.Data.Binding("HeaderText")
            {
                Source = tab
            };
            tabItem.SetBinding(TabItem.HeaderProperty, binding);

            // NOTE: the file-path tooltip is declared in the TabItem ControlTemplate, not here.
            // Setting ToolTip on the TabItem made it reachable from the tab's Content, so it
            // appeared as a stray bubble over the editor surface.

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

            // Right-click tab context menu (browser-style tab management)
            tabItem.ContextMenu = BuildTabContextMenu(tab);

            return tabItem;
        }

        /// <summary>Builds the right-click context menu for a tab.</summary>
        private ContextMenu BuildTabContextMenu(DocumentTab tab)
        {
            var menu = new ContextMenu();

            var lockItem = new MenuItem { Header = tab.IsLocked ? "Unlock Tab" : "Lock Tab" };
            lockItem.Click += (s, e) => tab.IsLocked = !tab.IsLocked;
            menu.Items.Add(lockItem);

            menu.Items.Add(new Separator());

            var closeItem = new MenuItem { Header = "Close Tab", InputGestureText = "Ctrl+W" };
            closeItem.Click += (s, e) => CloseTab(tab);
            menu.Items.Add(closeItem);

            var closeOthersItem = new MenuItem { Header = "Close Other Tabs" };
            closeOthersItem.Click += (s, e) => CloseOtherTabs(tab);
            menu.Items.Add(closeOthersItem);

            var closeRightItem = new MenuItem { Header = "Close Tabs to the Right" };
            closeRightItem.Click += (s, e) => CloseTabsToTheRight(tab);
            menu.Items.Add(closeRightItem);

            var closeSavedItem = new MenuItem { Header = "Close Saved Tabs" };
            closeSavedItem.Click += (s, e) => CloseSavedTabs();
            menu.Items.Add(closeSavedItem);

            var closeAllItem = new MenuItem { Header = "Close All Tabs" };
            closeAllItem.Click += (s, e) => CloseAllTabs();
            menu.Items.Add(closeAllItem);

            menu.Items.Add(new Separator());

            var copyPathItem = new MenuItem { Header = "Copy File Path" };
            copyPathItem.Click += (s, e) =>
            {
                if (tab.Document.FilePath != null)
                {
                    try { Clipboard.SetText(tab.Document.FilePath); } catch { /* clipboard busy */ }
                }
            };
            menu.Items.Add(copyPathItem);

            var revealItem = new MenuItem { Header = "Show in File Explorer" };
            revealItem.Click += (s, e) =>
            {
                if (tab.Document.FilePath != null && File.Exists(tab.Document.FilePath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe",
                            $"/select,\"{tab.Document.FilePath}\"");
                    }
                    catch { /* explorer unavailable */ }
                }
            };
            menu.Items.Add(revealItem);

            // Enable/disable file-dependent items each time the menu opens
            menu.Opened += (s, e) =>
            {
                bool hasFile = tab.Document.FilePath != null;
                lockItem.Header = tab.IsLocked ? "Unlock Tab" : "Lock Tab";
                copyPathItem.IsEnabled = hasFile;
                revealItem.IsEnabled = hasFile && File.Exists(tab.Document.FilePath);
                closeOthersItem.IsEnabled = _tabs.Count > 1;
                closeRightItem.IsEnabled = _tabs.IndexOf(tab) < _tabs.Count - 1;
            };

            return menu;
        }

        /// <summary>Closes every tab except the given one (save prompts still apply per tab; locked tabs survive).</summary>
        private void CloseOtherTabs(DocumentTab keep)
        {
            foreach (var t in _tabs.Where(t => t != keep && !t.IsLocked).ToList())
                CloseTab(t);
        }

        /// <summary>Closes all tabs positioned after the given tab (locked tabs survive).</summary>
        private void CloseTabsToTheRight(DocumentTab tab)
        {
            var index = _tabs.IndexOf(tab);
            if (index < 0) return;
            foreach (var t in _tabs.Skip(index + 1).Where(t => !t.IsLocked).ToList())
                CloseTab(t);
        }

        /// <summary>Closes every saved (non-dirty) tab. Locked tabs survive.</summary>
        private void CloseSavedTabs()
        {
            foreach (var t in _tabs.Where(t => !t.IsLocked && !t.HasUnsavedChanges).ToList())
                CloseTab(t);
        }

        /// <summary>Closes all tabs; locked tabs survive (PRD LOCK-03).</summary>
        private void CloseAllTabs()
        {
            foreach (var t in _tabs.Where(t => !t.IsLocked).ToList())
                CloseTab(t);
        }

        /// <summary>File menu: closes all unlocked tabs, leaving a fresh empty tab.</summary>
        private void CloseAllTabs_Click(object sender, RoutedEventArgs e) => CloseAllTabs();

        /// <summary>File menu: resets to a single new tab (dirty prompts still apply).</summary>
        private void StartFresh_Click(object sender, RoutedEventArgs e)
        {
            CloseAllTabs();
            if (_tabs.Count > 0)
                TabControl.SelectedIndex = 0;
        }

        /// <summary>Opens (or selects) the tab for a file. Returns the target
        /// tab — callers must use it rather than assuming it's _tabs.Last(),
        /// because an already-open file selects the existing tab in place.</summary>
        private DocumentTab? OpenFileInNewTab(string filePath)
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
                    return existingTab;
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
                WireTechnicalCommands(editor);

                var tabItem = CreateTabItem(tab, editor);

                _tabs.Add(tab);
                TabControl.Items.Add(tabItem);
                TabControl.SelectedItem = tabItem;

                // Watch for external changes (PRD SYNC-01)
                _fileMonitor?.Watch(filePath);

                // Add to recent files
                App.Settings.AddRecentFile(filePath);
                UpdateRecentFilesMenu();
                return tab;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void CloseTab(DocumentTab tab)
        {
            // PRD LOCK-01..03: a locked tab survives every close path with a
            // subtle, non-modal explanation.
            if (tab.IsLocked)
            {
                ShowStatusMessage("Tab is locked. Unlock it to close.");
                return;
            }

            if (!PromptSaveIfDirty(tab, out var dontSavePanes))
                return;

            // Panes the user explicitly answered "Don't Save" for are excluded
            // — an explicit discard must not resurrect via Reopen Closed Tab.
            AddToClosedTabsHistory(tab, dontSavePanes);

            var index = _tabs.IndexOf(tab);
            if (index >= 0)
            {
                // Unsubscribe MainWindow's event handlers, then EditorControl's own cleanup
                if (tab.EditorControl != null)
                    UnhookEditorEvents(tab.EditorControl);
                if (tab.SplitViewContainer != null)
                {
                    if (tab.SplitViewContainer.TopEditorPane.EditorControl != null)
                        UnhookEditorEvents(tab.SplitViewContainer.TopEditorPane.EditorControl);
                    if (tab.SplitViewContainer.BottomEditorPane.EditorControl != null)
                        UnhookEditorEvents(tab.SplitViewContainer.BottomEditorPane.EditorControl);
                }
                tab.EditorControl?.Cleanup();
                tab.SplitViewContainer?.Dispose();

                _tabs.RemoveAt(index);
                TabControl.Items.RemoveAt(index);

                // Stop watching its files and drop any pending conflict state
                if (tab.Document.FilePath != null)
                    _fileMonitor?.Unwatch(tab.Document.FilePath);
                if (tab.SplitViewContainer?.TopDocument?.FilePath != null)
                    _fileMonitor?.Unwatch(tab.SplitViewContainer.TopDocument.FilePath);
                if (tab.SplitViewContainer?.BottomDocument?.FilePath != null)
                    _fileMonitor?.Unwatch(tab.SplitViewContainer.BottomDocument.FilePath);
                _fileConflicts.Remove(tab);
                if (tab.RecoveryId != null)
                    _recoveryService.DeleteSnapshot(tab.RecoveryId);

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

        private bool PromptSaveIfDirty(DocumentTab tab) => PromptSaveIfDirty(tab, out _);

        /// <summary>dontSavePanes reports which panes the user explicitly
        /// answered "Don't Save" for (0 = primary/top, 1 = bottom) — the close
        /// path excludes exactly those panes from the session snapshot.</summary>
        private bool PromptSaveIfDirty(DocumentTab tab, out HashSet<int> dontSavePanes)
        {
            if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
            {
                return tab.SplitViewContainer.PromptSaveAllDocuments(this, out dontSavePanes);
            }

            dontSavePanes = new HashSet<int>();
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

            if (result == MessageBoxResult.No)
                dontSavePanes.Add(0);
            return result == MessageBoxResult.No;
        }

        private bool SaveDocument(DocumentTab tab)
        {
            if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
            {
                var saved = tab.SplitViewContainer.SaveActiveDocument(this);
                var savedDocument = tab.SplitViewContainer.ActiveDocument;
                if (saved && !string.IsNullOrEmpty(savedDocument?.FilePath))
                {
                    _fileMonitor?.Watch(savedDocument.FilePath);
                    App.Settings.AddRecentFile(savedDocument.FilePath);
                    UpdateRecentFilesMenu();
                }
                return saved;
            }

            if (tab.Document.FilePath == null)
            {
                return SaveDocumentAs(tab);
            }

            try
            {
                if (tab.EditorControl != null)
                {
                    var wasWatched = tab.Document.FilePath != null &&
                        _fileMonitor?.IsWatching(tab.Document.FilePath) == true;
                    tab.EditorControl.SaveToDocument();
                    // Fingerprint of the bytes we just wrote — only events for
                    // that exact content are suppressed, and no reread means no
                    // race with a coincident external write. A Save-As path
                    // isn't watched yet, so no marker is needed there at all.
                    if (wasWatched && tab.Document.FilePath != null &&
                        tab.Document.LastSavedFingerprint != null)
                        _fileMonitor?.BeginSelfWrite(tab.Document.FilePath,
                            tab.Document.LastSavedFingerprint);
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
            if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
                return tab.SplitViewContainer.SaveActiveDocument(this, saveAs: true);

            var dialog = new SaveFileDialog
            {
                Filter = "Notes (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Markdown (*.md;*.markdown)|*.md;*.markdown|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = tab.Document.FileName
            };

            if (dialog.ShowDialog() == true)
            {
                var previousPath = tab.Document.FilePath;
                tab.Document.FilePath = dialog.FileName;
                if (SaveDocument(tab))
                {
                    // The saved file now backs this tab — watch it for external
                    // changes like any other open file. The OLD path is only
                    // unwatched when no other tab/pane still owns it.
                    _fileMonitor?.Watch(tab.Document.FilePath);
                    UnwatchIfOrphaned(previousPath);
                    return true;
                }

                // A failed Save As must not rename the tab to a file that was
                // never successfully written.
                tab.Document.FilePath = previousPath;
            }

            return false;
        }

        /// <summary>Stop watching a path only when no open tab or split pane
        /// still backs onto it — the watcher is per-path, shared by panes.</summary>
        private void UnwatchIfOrphaned(string? previousPath)
        {
            if (string.IsNullOrEmpty(previousPath) || _fileMonitor == null)
                return;
            var stillOpen = _tabs.Any(t =>
                string.Equals(t.Document.FilePath, previousPath, StringComparison.OrdinalIgnoreCase) ||
                (t.SplitViewContainer != null &&
                 (string.Equals(t.SplitViewContainer.TopDocument?.FilePath, previousPath,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(t.SplitViewContainer.BottomDocument?.FilePath, previousPath,
                      StringComparison.OrdinalIgnoreCase))));
            if (!stillOpen)
                _fileMonitor.Unwatch(previousPath);
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
                Filter = "Notes (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Markdown (*.md;*.markdown)|*.md;*.markdown|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
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
                case "SourceFontFamily":
                case "SourceFontSize":
                case "ShowLineNumbers":
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

                    // ApplyFontSettings writes the raw setting size, which would silently drop
                    // the active zoom level while the footer still advertised it. Re-apply zoom
                    // so the indicator and the text agree.
                    ApplyZoom();
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

                case "EnableAutoLinkDetection":
                    foreach (var tab in _tabs)
                    {
                        if (tab.IsSplitViewEnabled && tab.SplitViewContainer != null)
                        {
                            tab.SplitViewContainer.TopPane?.EditorControl?.RefreshAutoLinkDetection();
                            tab.SplitViewContainer.BottomPane?.EditorControl?.RefreshAutoLinkDetection();
                        }
                        else
                        {
                            tab.EditorControl?.RefreshAutoLinkDetection();
                        }
                    }
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
                if (!DisableSplitViewForTab(tab))
                {
                    MenuSplitView.IsChecked = true;
                    return;
                }
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
                    tab.EditorControl.SyncToDocument();
                }

                // Create split view container
                System.Diagnostics.Debug.WriteLine("[MainWindow] Creating SplitViewContainer...");
                var splitContainer = new SplitViewContainer();
                
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Initializing with document and view mode {tab.ViewMode}...");
                splitContainer.Initialize(tab.Document, tab.ViewMode);
                splitContainer.PaneClosed += (_, args) =>
                    CollapseSplitViewForTab(tab, args.RemainingDocument);
                splitContainer.DocumentOpened += (_, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Document.FilePath))
                    {
                        _fileMonitor?.Watch(args.Document.FilePath);
                        App.Settings.AddRecentFile(args.Document.FilePath);
                        UpdateRecentFilesMenu();
                    }
                };
                splitContainer.DocumentSaved += (_, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Document.FilePath))
                    {
                        // Fingerprint of the bytes our split-pane save just
                        // wrote — the watcher's echo is suppressed; a later
                        // external edit with different content still dispatches.
                        if (_fileMonitor?.IsWatching(args.Document.FilePath) == true &&
                            args.Document.LastSavedFingerprint != null)
                            _fileMonitor.BeginSelfWrite(args.Document.FilePath,
                                args.Document.LastSavedFingerprint);
                        _fileMonitor?.Watch(args.Document.FilePath);
                    }
                };

                if (splitContainer.TopEditorPane.EditorControl != null)
                    HookEditorEvents(splitContainer.TopEditorPane.EditorControl);
                if (splitContainer.BottomEditorPane.EditorControl != null)
                    HookEditorEvents(splitContainer.BottomEditorPane.EditorControl);

                // Swap only the main-content slot of the tab's wrapper — the
                // SecondaryPaneHost (conflict banner / compare pane) stays
                // attached through split transitions.
                var tabItem = TabControl.Items[_tabs.IndexOf(tab)] as TabItem;
                if (tabItem?.Content is Grid wrapper)
                {
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Replacing editor with split container...");

                    if (tab.EditorControl != null)
                    {
                        UnhookEditorEvents(tab.EditorControl);
                        tab.EditorControl.Cleanup();
                        wrapper.Children.Remove(tab.EditorControl);
                        tab.EditorControl = null;
                    }

                    Grid.SetRow(splitContainer, 0);
                    wrapper.Children.Insert(0, splitContainer);
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

        private bool DisableSplitViewForTab(DocumentTab tab)
        {
            if (!tab.IsSplitViewEnabled || tab.SplitViewContainer == null) return true;

            if (!tab.SplitViewContainer.TryPrepareCollapse(this, out var remainingDocument))
                return false;

            CollapseSplitViewForTab(tab, remainingDocument);
            return true;
        }

        private void CollapseSplitViewForTab(DocumentTab tab, Document remainingDocument)
        {
            if (!tab.IsSplitViewEnabled || tab.SplitViewContainer == null)
                return;

            // Create single editor
            var editor = new EditorControl
            {
                Document = remainingDocument,
                ViewMode = tab.ViewMode
            };
            HookEditorEvents(editor);

            // Swap only the main-content slot — SecondaryPaneHost persists.
            var tabItem = TabControl.Items[_tabs.IndexOf(tab)] as TabItem;
            if (tabItem?.Content is Grid wrapper)
            {
                if (tab.SplitViewContainer.TopEditorPane.EditorControl != null)
                    UnhookEditorEvents(tab.SplitViewContainer.TopEditorPane.EditorControl);
                if (tab.SplitViewContainer.BottomEditorPane.EditorControl != null)
                    UnhookEditorEvents(tab.SplitViewContainer.BottomEditorPane.EditorControl);
                tab.SplitViewContainer.Dispose();
                wrapper.Children.Remove(tab.SplitViewContainer);

                // A conflict registered against a document that did not survive
                // the collapse must go with it — otherwise Compare / Save a
                // Copy would act on the remaining pane's text under the wrong
                // filename. The survivor's conflict stays valid.
                if (_fileConflicts.TryGetValue(tab, out var collapseConflict) &&
                    !ReferenceEquals(collapseConflict.Document, remainingDocument))
                {
                    _fileConflicts.Remove(tab);
                    tab.PaneHost?.HideConflict();
                }
                tab.Document = remainingDocument;
                Grid.SetRow(editor, 0);
                wrapper.Children.Insert(0, editor);
                tab.EditorControl = editor;
                tab.SplitViewContainer = null;
                tab.IsSplitViewEnabled = false;
            }

            MenuSplitView.IsChecked = false;
            UpdateStatusIndicators();
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
            // Check all tabs for unsaved changes — unless silent session
            // continuation is on (PRD TAB-02: no save prompts when unsaved
            // content is preserved through session snapshots). Prompts are only
            // skipped when snapshots will actually be saved AND restored; with
            // tab restore off the data is deleted below, so prompt as before.
            if (!(App.Settings.PreserveUnsavedContent && App.Settings.RestoreOpenTabs))
            {
                foreach (var tab in _tabs.ToList())
                {
                    if (tab.HasUnsavedChanges)
                    {
                        // Switch to the dirty tab so user knows which file
                        var index = _tabs.IndexOf(tab);
                        TabControl.SelectedIndex = index;

                        if (!PromptSaveIfDirty(tab, out var dontSavePanes))
                        {
                            e.Cancel = true;
                            _dontSavePanes.Clear();
                            return;
                        }
                        if (dontSavePanes.Count > 0)
                            _dontSavePanes[tab] = dontSavePanes;
                    }
                }
            }

            // Stop recovery scheduling and drop any queued payload — the final
            // synchronous save below supersedes them (and its sequence number
            // guarantees an older in-flight write can't land after it).
            _recoveryIdleTimer?.Stop();
            _recoveryMaxTimer?.Stop();
            lock (_recoveryEnqueueLock) { _pendingRecovery = null; }

            // Save session if enabled — a failed final write must not exit
            // silently (data-loss standard): cancel the close unless the user
            // chooses to leave anyway.
            if (App.Settings.RestoreOpenTabs)
            {
                // Explicit "Don't Save" answers must not resurrect:
                // CaptureRecoveryPayload excludes the declined panes from the
                // new manifest, and post-commit orphan cleanup removes their
                // previously committed files. No deletion before commit — if
                // the write fails and the close is cancelled, the prior
                // generation stays intact.
                try
                {
                    SaveCurrentSession();
                }
                catch (Exception ex)
                {
                    var choice = MessageBox.Show(this,
                        "Recovery data could not be saved (" + ex.Message + "). " +
                        "Unsaved notes may not be restored on the next launch.\n\nClose anyway?",
                        "Workpad", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        _dontSavePanes.Clear();
                        // The close was cancelled — recovery must resume:
                        // re-arm the schedule so the write retries instead of
                        // leaving pending changes unsnapshotted.
                        OnRecoveryActivity();
                        return;
                    }
                    // Closing anyway: declined panes must not resurrect — remove
                    // their committed content (surviving panes keep restoring).
                    // Best-effort: a failure here must not veto a close the user
                    // already confirmed.
                    try
                    {
                        _recoveryService.RemovePanes(
                            _dontSavePanes
                                .Where(kv => kv.Key.RecoveryId != null)
                                .SelectMany(kv => kv.Value.Select(p => (kv.Key.RecoveryId!, p))));
                    }
                    catch { /* best-effort; the user chose to leave anyway */ }
                    _dontSavePanes.Clear();
                }
            }
            else
            {
                // Quiesce the background writer and reject every captured
                // payload forever, THEN clear — a dequeued write can otherwise
                // land after ClearAll and resurrect the session on next launch.
                lock (_recoveryIoLock)
                {
                    _lastWrittenSeq = long.MaxValue;
                    AppSettings.ClearSession();
                    _recoveryService.ClearAll();
                }
            }

            _fileMonitor?.Dispose();

            // Unsubscribe events to break references
            App.Settings.SettingChanged -= Settings_Changed;
            StateChanged -= MainWindow_StateChanged;
            this.Loaded -= MainWindow_Loaded;
            PreviewKeyDown -= MainWindow_PreviewKeyDown;
            TabControl.PreviewMouseLeftButtonDown -= TabControl_PreviewMouseLeftButtonDown;
            TabControl.PreviewMouseMove -= TabControl_PreviewMouseMove;
            TabControl.Drop -= TabControl_Drop;
            _viewModeManager.Cleanup();

            // Clean up all tabs (stop timers, unsubscribe events)
            foreach (var tab in _tabs)
            {
                if (tab.EditorControl != null)
                    UnhookEditorEvents(tab.EditorControl);
                if (tab.SplitViewContainer != null)
                {
                    if (tab.SplitViewContainer.TopEditorPane.EditorControl != null)
                        UnhookEditorEvents(tab.SplitViewContainer.TopEditorPane.EditorControl);
                    if (tab.SplitViewContainer.BottomEditorPane.EditorControl != null)
                        UnhookEditorEvents(tab.SplitViewContainer.BottomEditorPane.EditorControl);
                }
                tab.EditorControl?.Cleanup();
                tab.SplitViewContainer?.Dispose();
            }

            base.OnClosing(e);
        }

        // Session management
        //
        // Performance contract (PRD: "typing always wins"): the UI thread only
        // CAPTURES an immutable payload (document references + buffer text).
        // Serialization, hashing and disk I/O run on a thread-pool writer.
        // Overlapping requests coalesce to the newest payload, and the service
        // skips files whose content hash / metadata is unchanged.
        private readonly object _recoveryEnqueueLock = new();
        private readonly object _recoveryIoLock = new();
        private RecoveryWritePayload? _pendingRecovery;
        private bool _recoveryWriteInFlight;
        // Panes the user explicitly answered "Don't Save" for during this
        // close (0 = primary/top, 1 = bottom) — excluded from the final
        // manifest and orphan-cleaned after commit.
        private readonly Dictionary<DocumentTab, HashSet<int>> _dontSavePanes = new();
        private long _recoverySeq;
        private long _lastWrittenSeq;
        // (tabId, pane) -> document version at last COMMITTED snapshot. Versions
        // are recorded on write completion, not capture — a payload superseded
        // by coalescing must not mark its revisions persisted. Guarded by its
        // own lock — the UI thread reads/clears it in capture while the writer
        // records commits; it must NOT use _recoveryIoLock, which the writer
        // holds for the whole disk write ("typing always wins").
        private readonly object _recoveryVersionLock = new();
        private readonly Dictionary<(string TabId, int Pane), ITextSourceVersion> _committedVersions = new();
        // Recovery epoch last seen by capture; a ClearAll on any service
        // instance bumps it, invalidating every committed version so the next
        // capture re-sends text whose files were deleted.
        private long _recoveryEpoch = -1;

        private sealed class RecoveryWritePayload
        {
            public long Seq { get; init; }
            public required TabSession Session { get; init; }
            public required List<RecoveryTabPayload> Tabs { get; init; }
            public required List<(string TabId, int Pane, ITextSourceVersion Version)> Versions { get; init; }
        }

        /// <summary>Synchronous final save (shutdown path).</summary>
        private void SaveCurrentSession()
        {
            var payload = CaptureRecoveryPayload();
            WriteRecoveryPayload(payload);
        }

        /// <summary>UI-thread-only: pull the minimal immutable state out of the
        /// live editors — metadata plus, per dirty/untitled pane, an AvalonEdit
        /// immutable snapshot. Version-gated: a pane whose document version
        /// matches its last committed snapshot emits only an Unchanged marker
        /// (no copying, no serialization, no write). No SyncToDocument call —
        /// the source document is already canonical in both view modes.</summary>
        private RecoveryWritePayload CaptureRecoveryPayload()
        {
            // A ClearAll from any instance (Settings, another window) bumps
            // the epoch: drop every committed version so panes re-capture text
            // whose files were deleted underneath us.
            var epoch = SessionRecoveryService.Epoch;
            lock (_recoveryVersionLock)
            {
                if (epoch != _recoveryEpoch)
                {
                    _committedVersions.Clear();
                    _recoveryEpoch = epoch;
                }
            }

            var session = new TabSession
            {
                Tabs = new List<TabSessionState>(),
                ActiveTabIndex = 0
            };
            var snapshots = new List<RecoveryTabPayload>();
            var versions = new List<(string, int, ITextSourceVersion)>();

            var selectedTab = GetCurrentTab();

            foreach (var tab in _tabs)
            {
                var split = tab.SplitViewContainer;
                var primaryDocument = split?.TopDocument ?? tab.Document;
                var primaryEditor = split?.TopEditorPane.EditorControl ?? tab.EditorControl;
                var secondaryDocument = split?.BottomDocument;
                var secondaryEditor = split?.BottomEditorPane.EditorControl;

                // Explicit "Don't Save" answers from the close prompt. Every
                // pane declined → no snapshot for the tab at all; one split
                // pane declined → the surviving pane persists as a normal
                // single-pane snapshot.
                var declined = _dontSavePanes.TryGetValue(tab, out var dp) ? dp : null;
                if (declined != null && declined.Count >= (secondaryDocument != null ? 2 : 1))
                    continue;
                var primaryDeclined = declined?.Contains(0) == true;
                var secondaryDeclined = declined?.Contains(1) == true;
                if (primaryDeclined && secondaryDocument == null)
                    continue;
                var survivorIsSecondary = primaryDeclined;
                var keptSecondary = secondaryDocument != null && !secondaryDeclined && !survivorIsSecondary;
                var captureDoc = survivorIsSecondary ? secondaryDocument! : primaryDocument;
                var captureEditor = survivorIsSecondary ? secondaryEditor : primaryEditor;
                var capturePaneIndex = survivorIsSecondary ? 1 : 0;

                if (ReferenceEquals(tab, selectedTab))
                {
                    session.ActiveTabIndex = session.Tabs.Count;
                }

                tab.RecoveryId ??= Guid.NewGuid().ToString("N");

                // Promotion must force a fresh capture: the unchanged marker
                // would let SaveSession reuse the committed PRIMARY revision —
                // the declined pane's text — for the surviving bottom buffer.
                var (primarySource, primaryUnchanged, primaryVersion) =
                    CapturePane(tab.RecoveryId, capturePaneIndex, captureDoc, captureEditor,
                        forceCapture: survivorIsSecondary);
                var (secondarySource, secondaryUnchanged, secondaryVersion) =
                    keptSecondary
                        ? CapturePane(tab.RecoveryId, paneIndex: 1, secondaryDocument!, secondaryEditor)
                        : (null, false, (ITextSourceVersion?)null);
                if (primaryVersion != null) versions.Add((tab.RecoveryId, capturePaneIndex, primaryVersion));
                if (secondaryVersion != null) versions.Add((tab.RecoveryId, 1, secondaryVersion));

                var state = new TabSessionState
                {
                    FilePath = captureDoc.FilePath,
                    ViewMode = tab.ViewMode,
                    CursorPosition = captureEditor?.Editor.CaretOffset ?? 0,
                    ScrollOffset = captureEditor?.Editor.VerticalOffset ?? 0,
                    IsSplitViewEnabled = tab.IsSplitViewEnabled && !survivorIsSecondary && keptSecondary,
                    SecondaryFilePath = keptSecondary ? secondaryDocument?.FilePath : null,
                    SecondaryCursorPosition = keptSecondary ? secondaryEditor?.Editor.CaretOffset ?? 0 : 0,
                    SecondaryScrollOffset = keptSecondary ? secondaryEditor?.Editor.VerticalOffset ?? 0 : 0,
                    ActivePaneIndex = keptSecondary ? split?.ActivePaneIndex ?? 0 : 0,
                    TopPaneReadOnly = split?.IsTopPaneReadOnly ?? false,
                    BottomPaneReadOnly = split?.IsBottomPaneReadOnly ?? false,
                    RecoveryId = tab.RecoveryId,
                    IsLocked = tab.IsLocked,
                    Content = null,
                    FileName = captureDoc.FileName
                };
                session.Tabs.Add(state);

                snapshots.Add(new RecoveryTabPayload
                {
                    Meta = new TabSnapshot
                    {
                        Id = tab.RecoveryId,
                        FilePath = captureDoc.FilePath,
                        FileName = captureDoc.FileName,
                        IsDirty = captureDoc.IsDirty || string.IsNullOrEmpty(captureDoc.FilePath),
                        IsLocked = tab.IsLocked,
                        ViewMode = tab.ViewMode.ToString(),
                        CursorPosition = state.CursorPosition,
                        ScrollOffset = state.ScrollOffset,
                        IsSplitViewEnabled = state.IsSplitViewEnabled,
                        SecondaryFilePath = state.SecondaryFilePath,
                        SecondaryIsDirty = keptSecondary && (secondaryDocument?.IsDirty ?? false),
                        SecondaryCursorPosition = state.SecondaryCursorPosition,
                        SecondaryScrollOffset = state.SecondaryScrollOffset,
                        ActivePaneIndex = state.ActivePaneIndex,
                        TopPaneReadOnly = state.TopPaneReadOnly,
                        BottomPaneReadOnly = state.BottomPaneReadOnly,
                    },
                    Content = primarySource,
                    ContentUnchanged = primaryUnchanged,
                    SecondaryContent = secondarySource,
                    SecondaryContentUnchanged = secondaryUnchanged,
                });
            }

            return new RecoveryWritePayload
            {
                Seq = System.Threading.Interlocked.Increment(ref _recoverySeq),
                Session = session,
                Tabs = snapshots,
                Versions = versions,
            };
        }

        /// <summary>Version-gated buffer capture for one pane: an immutable
        /// ITextSource when the document revision moved, an Unchanged marker
        /// when it matches the last committed snapshot, nothing when the pane
        /// is a clean file-backed buffer (restored from disk instead).</summary>
        private (ITextSource? Source, bool Unchanged, ITextSourceVersion? Version)
            CapturePane(string tabId, int paneIndex, Document document, EditorControl? editor,
                bool forceCapture = false)
        {
            // Flush the debounced formatted->source sync before snapshotting —
            // SourceTextDocument must hold the latest edit, not pre-idle-timer
            // content. This is the authoritative flush boundary (WP-01).
            editor?.SyncToDocument();
            var needsContent = string.IsNullOrEmpty(document.FilePath) || document.IsDirty;
            var textDoc = editor?.SourceTextDocument;
            if (!needsContent || textDoc == null)
            {
                // Editor missing but the buffer can hold unsaved text — the
                // document model's content is the last synced state.
                if (needsContent && document.Content != null)
                    return (new TextDocument(document.Content), false, null);
                return (null, false, null);
            }

            var version = textDoc.Version;
            bool unchanged;
            lock (_recoveryVersionLock)
            {
                unchanged = !forceCapture
                    && _committedVersions.TryGetValue((tabId, paneIndex), out var last)
                    && last.BelongsToSameDocumentAs(version)
                    && last.CompareAge(version) == 0;
            }
            return unchanged
                ? (null, true, version)
                : (textDoc.CreateSnapshot(), false, version);
        }

        /// <summary>Latest payload wins; a single writer drains the queue.</summary>
        private void EnqueueRecoveryWrite(RecoveryWritePayload payload)
        {
            bool start;
            lock (_recoveryEnqueueLock)
            {
                _pendingRecovery = payload;
                start = !_recoveryWriteInFlight;
                _recoveryWriteInFlight = true;
            }
            if (!start)
                return;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                while (true)
                {
                    RecoveryWritePayload? work;
                    lock (_recoveryEnqueueLock)
                    {
                        if (_pendingRecovery == null)
                        {
                            _recoveryWriteInFlight = false;
                            return;
                        }
                        work = _pendingRecovery;
                        _pendingRecovery = null;
                    }
                    try { WriteRecoveryPayload(work); }
                    catch { /* snapshots are best-effort */ }
                }
            });
        }

        /// <summary>Off-UI-thread write. Ordered by sequence number so an older
        /// in-flight payload can never overwrite a newer capture. Committed
        /// document versions are recorded only after the write lands.</summary>
        private void WriteRecoveryPayload(RecoveryWritePayload payload)
        {
            lock (_recoveryIoLock)
            {
                if (payload.Seq <= _lastWrittenSeq)
                    return;
                _lastWrittenSeq = payload.Seq;
                _recoveryService.SaveSession(payload.Tabs, payload.Session.ActiveTabIndex);
                AppSettings.SaveSession(payload.Session);
                lock (_recoveryVersionLock)
                {
                    foreach (var (tabId, pane, version) in payload.Versions)
                        _committedVersions[(tabId, pane)] = version;
                }
                var m = _recoveryService.LastMetrics;
                System.Diagnostics.Debug.WriteLine(
                    $"[recovery] gen={m.Generation} total={m.TotalMs}ms " +
                    $"disk={m.DiskMs}ms ser={m.SerializeMs}ms alloc={m.AllocatedBytes}B " +
                    $"gen2={m.Gen2Collections} files={m.SnapshotFilesWritten}s/{m.ContentFilesWritten}c");
            }
        }

        /// <summary>Recovery trigger (PRD RECOV-01): capture on the UI thread,
        /// hand off to the background writer. Runs ~2s after the last edit or
        /// at the 30s ceiling while edits keep arriving.</summary>
        private void SaveRecoverySnapshots()
        {
            if (!App.Settings.RestoreOpenTabs)
                return;
            try { EnqueueRecoveryWrite(CaptureRecoveryPayload()); }
            catch { /* snapshots are best-effort */ }
        }

        /// <summary>Any edit or caret move re-arms the idle timer so the
        /// snapshot runs while the typist pauses. The max-interval timer runs
        /// unconditionally — its deduped captures are free and it also covers
        /// metadata-only changes like opening a tab.</summary>
        private void OnRecoveryActivity()
        {
            if (!App.Settings.RestoreOpenTabs)
                return;
            _recoveryIdleTimer?.Stop();
            _recoveryIdleTimer?.Start();
        }

        private bool RestoreSession()
        {
            var session = AppSettings.LoadSession();
            var (snapshots, snapActiveIndex) = _recoveryService.LoadSession();

            // New recovery path: snapshots cover untitled + dirty buffers, and
            // the session file carries view/split state keyed by RecoveryId.
            if (snapshots.Count > 0)
            {
                var stateById = session?.Tabs
                    .Where(t => t.RecoveryId != null)
                    .ToDictionary(t => t.RecoveryId!, t => t)
                    ?? new Dictionary<string, TabSessionState>();

                var anyRestored = false;
                foreach (var snap in snapshots)
                {
                    try
                    {
                        stateById.TryGetValue(snap.Id, out var st);
                        if (RestoreSnapshot(snap, st))
                            anyRestored = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Recovery] Failed to restore snapshot: {ex.Message}");
                    }
                }

                if (anyRestored)
                {
                    if (snapActiveIndex >= 0 && snapActiveIndex < TabControl.Items.Count)
                        TabControl.SelectedIndex = snapActiveIndex;
                    return true;
                }
            }

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

                    var tab = _tabs.FirstOrDefault(candidate =>
                        string.Equals(candidate.Document.FilePath, tabState.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (tab != null && tab.EditorControl != null)
                    {
                        // Restore view mode
                        tab.ViewMode = tabState.ViewMode;
                        tab.EditorControl.ViewMode = tabState.ViewMode;
                        
                        if (tabState.IsSplitViewEnabled)
                        {
                            EnableSplitViewForTab(tab);
                            var split = tab.SplitViewContainer;
                            if (split != null)
                            {
                                if (!string.IsNullOrEmpty(tabState.SecondaryFilePath) &&
                                    File.Exists(tabState.SecondaryFilePath))
                                {
                                    split.OpenDocumentInPane(split.BottomEditorPane, tabState.SecondaryFilePath);
                                }
                                split.SetPaneReadOnly(0, tabState.TopPaneReadOnly);
                                split.SetPaneReadOnly(1, tabState.BottomPaneReadOnly);
                                split.ActivatePane(tabState.ActivePaneIndex);
                            }
                        }

                        // Restore independent cursor and scroll positions after layout.
                        Dispatcher.InvokeAsync(() =>
                        {
                            var primaryEditor = tab.SplitViewContainer?.TopEditorPane.EditorControl ?? tab.EditorControl;
                            if (primaryEditor != null && primaryEditor.Editor.Text.Length >= tabState.CursorPosition)
                            {
                                primaryEditor.Editor.CaretOffset = tabState.CursorPosition;
                                primaryEditor.Editor.ScrollToVerticalOffset(tabState.ScrollOffset);
                            }

                            var secondaryEditor = tab.SplitViewContainer?.BottomEditorPane.EditorControl;
                            if (secondaryEditor != null && secondaryEditor.Editor.Text.Length >= tabState.SecondaryCursorPosition)
                            {
                                secondaryEditor.Editor.CaretOffset = tabState.SecondaryCursorPosition;
                                secondaryEditor.Editor.ScrollToVerticalOffset(tabState.SecondaryScrollOffset);
                            }
                        }, System.Windows.Threading.DispatcherPriority.Loaded);
                    }

                    anyTabOpened = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Session] Failed to restore tab: {ex.Message}");
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

            var hasTop = closedTab.FilePath != null || closedTab.Content != null;
            var hasBottom = closedTab.IsSplit &&
                (closedTab.BottomFilePath != null || closedTab.BottomContent != null);

            DocumentTab? tab = null;
            if (hasTop)
            {
                if (closedTab.FilePath != null)
                {
                    if (!File.Exists(closedTab.FilePath))
                    {
                        MessageBox.Show($"File no longer exists:\n{closedTab.FilePath}",
                            "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    // DEC-07: still-open file focuses its existing tab.
                    tab = OpenFileInNewTab(closedTab.FilePath);
                }
                else
                {
                    CreateNewTab();
                    tab = _tabs.LastOrDefault();
                    if (tab?.EditorControl != null && closedTab.Content != null)
                    {
                        tab.Document.Content = closedTab.Content;
                        tab.EditorControl.Document = tab.Document;
                    }
                }
                if (tab != null)
                {
                    tab.ViewMode = closedTab.ViewMode;
                    if (tab.EditorControl != null)
                        tab.EditorControl.ViewMode = closedTab.ViewMode;
                }
            }
            else if (hasBottom)
            {
                // Top pane was explicitly discarded — the surviving bottom
                // pane becomes the reopened tab's content.
                hasBottom = false;
                if (closedTab.BottomFilePath != null && File.Exists(closedTab.BottomFilePath))
                    tab = OpenFileInNewTab(closedTab.BottomFilePath);
                else if (closedTab.BottomContent != null)
                {
                    CreateNewTab();
                    tab = _tabs.LastOrDefault();
                    if (tab?.EditorControl != null)
                    {
                        tab.Document.Content = closedTab.BottomContent;
                        tab.EditorControl.Document = tab.Document;
                    }
                }
                if (tab != null)
                {
                    tab.ViewMode = closedTab.BottomViewMode;
                    if (tab.EditorControl != null)
                        tab.EditorControl.ViewMode = closedTab.BottomViewMode;
                }
            }

            if (tab == null || !hasBottom)
                return;

            // Rebuild the bottom pane of a split tab.
            EnableSplitViewForTab(tab);
            var split = tab.SplitViewContainer;
            if (split == null)
                return;
            if (!string.IsNullOrEmpty(closedTab.BottomFilePath) &&
                File.Exists(closedTab.BottomFilePath))
            {
                split.OpenDocumentInPane(split.BottomEditorPane, closedTab.BottomFilePath);
            }
            else if (closedTab.BottomContent != null)
            {
                split.SetPaneDocument(split.BottomEditorPane, new Document
                {
                    Content = closedTab.BottomContent,
                    IsDirty = true,
                });
            }
        }

        /// <summary>Buffer content worth restoring for a closed pane: the
        /// untitled buffer, unless the user explicitly discarded it.</summary>
        private static string? CaptureClosedContent(Document? doc, bool declined)
        {
            if (doc == null || declined)
                return null;
            return doc.FilePath == null ? doc.Content : null;
        }

        private void AddToClosedTabsHistory(DocumentTab tab, HashSet<int> dontSavePanes)
        {
            var top = tab.IsSplitViewEnabled ? tab.SplitViewContainer?.TopDocument : tab.Document;
            var bottom = tab.IsSplitViewEnabled ? tab.SplitViewContainer?.BottomDocument : null;

            var closedInfo = new ClosedTabInfo
            {
                FilePath = top?.FilePath,
                Content = CaptureClosedContent(top, dontSavePanes.Contains(0)),
                ViewMode = tab.ViewMode,
                IsSplit = bottom != null,
                ClosedAt = DateTime.Now
            };
            if (bottom != null)
            {
                closedInfo.BottomFilePath = bottom.FilePath;
                closedInfo.BottomContent = CaptureClosedContent(bottom, dontSavePanes.Contains(1));
                closedInfo.BottomViewMode = bottom != top
                    ? (tab.SplitViewContainer?.BottomEditorPane?.EditorControl?.ViewMode ?? tab.ViewMode)
                    : tab.ViewMode;
            }

            // Nothing restorable — every pane was explicitly discarded.
            if (closedInfo.FilePath == null && closedInfo.Content == null &&
                closedInfo.BottomFilePath == null && closedInfo.BottomContent == null)
                return;

            _closedTabs.Push(closedInfo);

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
                editor.SourceEditor.FontSize = App.Settings.SourceFontSize * _zoomLevel;
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

                // Rich tooltip: characters + estimated reading time (~200 wpm)
                var charCount = text.Replace("\r", string.Empty).Replace("\n", string.Empty).Length;
                var readMinutes = Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));
                WordCountText.ToolTip = wordCount == 0
                    ? "Empty document"
                    : $"{wordCount:N0} words · {charCount:N0} characters · ~{readMinutes} min read";

                // Update save state indicator
                SaveStateIndicator.Visibility = tab.HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                WordCountText.Text = "0 words";
                SaveStateIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void LineColButton_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (editor == null)
                return;

            var currentLine = editor.ViewMode == EditorViewMode.Source
                ? editor.SourceEditor.TextArea.Caret.Line
                : GetFormattedCaretLine(editor);
            var dialog = new Dialogs.GoToLineDialog(currentLine, editor.GetLineCount())
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
                editor.GoToLine(dialog.LineNumber);
        }

        private static int GetFormattedCaretLine(EditorControl editor)
        {
            var caretParagraph = editor.FormattedEditor.CaretPosition.Paragraph;
            if (caretParagraph == null)
                return 1;

            var line = 1;
            foreach (var paragraph in editor.FormattedEditor.Document.Blocks.OfType<Paragraph>())
            {
                if (ReferenceEquals(paragraph, caretParagraph))
                    return line;
                line++;
            }

            return 1;
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
            var newTheme = effectiveTheme == Models.ThemeMode.Dark ? Models.ThemeMode.Light : Models.ThemeMode.Dark;
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

        // ═══════════ Workpad 2.x: status messaging ═══════════

        /// <summary>Transient, non-modal status message (auto-clears).</summary>
        private void ShowStatusMessage(string message)
        {
            StatusMessageText.Text = message;
            StatusMessageText.Visibility = Visibility.Visible;
            if (_statusMessageTimer == null)
            {
                // Handler attached once — re-adding it per message would make
                // every later tick run the whole accumulated chain.
                _statusMessageTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _statusMessageTimer.Tick += (_, _) =>
                {
                    StatusMessageText.Visibility = Visibility.Collapsed;
                    _statusMessageTimer.Stop();
                };
            }
            _statusMessageTimer.Stop();
            _statusMessageTimer.Start();
        }

        // ═══════════ Workpad 2.x: secondary pane + external file conflicts ═══════════

        private void WireSecondaryPane(DocumentTab tab, SecondaryPaneHost host)
        {
            host.CompareRequested += (s, e) => CompareTabWithDisk(tab);
            host.ReloadRequested += (s, e) => ReloadTabFromDisk(tab);
            host.KeepRequested += (s, e) => { host.HideConflict(); _fileConflicts.Remove(tab); };
            host.SaveCopyRequested += (s, e) => SaveTabCopyAs(tab);
        }

        private void OnExternalFileChanged(string path)
        {
            var (tab, doc) = FindTabForPath(path);
            if (tab == null || doc == null)
                return;

            if (!doc.IsDirty)
            {
                // Clean buffer + external change → quiet reload of the pane that
                // owns this document (top or bottom), never the other pane.
                try
                {
                    doc.Load();
                    EditorForDocument(tab, doc)?.ReloadDocument();
                    tab.PaneHost?.HideConflict();
                    ShowStatusMessage($"{doc.FileName} was reloaded — it changed on disk.");
                }
                catch (Exception ex)
                {
                    ShowStatusMessage($"Could not reload {doc.FileName}: {ex.Message}");
                }
            }
            else
            {
                // Dirty buffer + external change → conflict banner, never auto-overwrite.
                _fileConflicts[tab] = (path, doc);
                tab.PaneHost?.ShowConflict($"{doc.FileName} changed on disk while you have unsaved edits.");
            }
        }

        private void OnExternalFileDeleted(string path)
        {
            var (tab, doc) = FindTabForPath(path);
            if (tab == null || doc == null)
                return;
            // Deleted on disk → preserve the buffer, surface the fact non-modally.
            _fileConflicts[tab] = (path, doc);
            tab.PaneHost?.ShowConflict($"{doc.FileName} was deleted on disk — your content is preserved in this tab.");
        }

        /// <summary>The editor bound to a given document — top or bottom split
        /// pane, or the single editor. Conflicts act on the pane that owns the
        /// changed file, not always the top pane.</summary>
        private static EditorControl? EditorForDocument(DocumentTab tab, Document? doc)
        {
            var split = tab.SplitViewContainer;
            if (split == null)
                return tab.EditorControl;
            if (ReferenceEquals(split.TopDocument, doc))
                return split.TopEditorPane?.EditorControl;
            if (ReferenceEquals(split.BottomDocument, doc))
                return split.BottomEditorPane?.EditorControl;
            return split.TopEditorPane?.EditorControl ?? tab.EditorControl;
        }

        private (DocumentTab?, Document?) FindTabForPath(string path)
        {
            foreach (var tab in _tabs)
            {
                if (string.Equals(tab.Document.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    return (tab, tab.Document);
                var split = tab.SplitViewContainer;
                if (split != null)
                {
                    if (string.Equals(split.TopDocument?.FilePath, path, StringComparison.OrdinalIgnoreCase))
                        return (tab, split.TopDocument);
                    if (string.Equals(split.BottomDocument?.FilePath, path, StringComparison.OrdinalIgnoreCase))
                        return (tab, split.BottomDocument);
                }
            }
            return (null, null);
        }

        private void ReloadTabFromDisk(DocumentTab tab)
        {
            _fileConflicts.TryGetValue(tab, out var conflict);
            var doc = conflict.Document ?? tab.Document;
            var target = conflict.Path ?? doc.FilePath;
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                ShowStatusMessage("The file no longer exists on disk.");
                return;
            }
            try
            {
                doc.FilePath = target;
                doc.Load();
                EditorForDocument(tab, doc)?.ReloadDocument();
                tab.PaneHost?.HideConflict();
                _fileConflicts.Remove(tab);
                ShowStatusMessage($"{doc.FileName} reloaded from disk.");
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Reload failed: {ex.Message}");
            }
        }

        private void SaveTabCopyAs(DocumentTab tab)
        {
            _fileConflicts.TryGetValue(tab, out var conflict);
            var doc = conflict.Document ?? tab.Document;
            var dialog = new SaveFileDialog
            {
                Filter = "All Files (*.*)|*.*",
                FileName = doc.FileName == "Untitled" ? "copy.txt" : "copy-" + doc.FileName
            };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                var text = EditorForDocument(tab, doc)?.GetText() ?? doc.Content;
                FileHelper.AtomicWriteText(dialog.FileName, text, System.Text.Encoding.UTF8);
                tab.PaneHost?.HideConflict();
                _fileConflicts.Remove(tab);
                ShowStatusMessage($"Saved a copy to {Path.GetFileName(dialog.FileName)}.");
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Could not save a copy: {ex.Message}");
            }
        }

        /// <summary>Conflict banner "Compare": disk content vs current buffer.</summary>
        private void CompareTabWithDisk(DocumentTab tab)
        {
            if (!_fileConflicts.TryGetValue(tab, out var conflict) || !File.Exists(conflict.Path))
            {
                ShowStatusMessage("The file on disk is no longer available to compare.");
                return;
            }
            var doc = conflict.Document;
            var diskText = File.ReadAllText(conflict.Path);
            var editorText = EditorForDocument(tab, doc)?.GetText() ?? doc.Content;
            ShowCompare(tab, Path.GetFileName(conflict.Path) + " (disk)", diskText,
                doc.FileName + " (this tab)", editorText);
        }

        // ═══════════ Workpad 2.x: compare ═══════════

        // One in-flight compare per host tab — a newer request cancels the
        // older so stale results can never replace fresher ones (WP-04-AC02).
        private readonly Dictionary<DocumentTab, CancellationTokenSource> _compareJobs = new();

        private async void ShowCompare(DocumentTab hostTab, string leftLabel, string left, string rightLabel, string right)
        {
            if (_compareJobs.Remove(hostTab, out var prior))
                prior.Cancel();
            var cts = new CancellationTokenSource();
            _compareJobs[hostTab] = cts;

            var view = new CompareView();
            view.ShowBusy(() => cts.Cancel());
            hostTab.PaneHost?.OpenPane("Comparing…", view);

            try
            {
                var result = await CompareService.CompareAsync(left, right, _normalizeXmlCompare, cts.Token);
                // Stale guards: cancelled, pane closed, or host tab gone — drop.
                if (cts.IsCancellationRequested || !_tabs.Contains(hostTab) ||
                    hostTab.PaneHost?.IsPaneOpen != true)
                    return;
                view.ShowResult(result, leftLabel, rightLabel);
                hostTab.PaneHost?.OpenPane($"Compare — {result.AddedCount} added, {result.RemovedCount} removed", view);
            }
            catch (OperationCanceledException) { /* superseded or closed — discard */ }
            catch (CompareTooLargeException)
            {
                if (hostTab.PaneHost?.IsPaneOpen == true)
                    hostTab.PaneHost?.ClosePane();
                ShowStatusMessage("These documents are too large for the current compare engine.");
            }
            finally
            {
                if (_compareJobs.TryGetValue(hostTab, out var current) && current == cts)
                    _compareJobs.Remove(hostTab);
                cts.Dispose();
            }
        }

        private void CompareWithSaved_Click(object sender, RoutedEventArgs e)
        {
            var tab = GetCurrentTab();
            if (tab == null)
                return;
            var path = tab.Document.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowStatusMessage("This tab has no saved file to compare with.");
                return;
            }
            var diskText = File.ReadAllText(path);
            var editorText = tab.GetActiveEditorControl()?.GetText() ?? tab.Document.Content;
            ShowCompare(tab, Path.GetFileName(path) + " (saved)", diskText,
                tab.Document.FileName + " (current)", editorText);
        }

        private void CompareWithTab_Click(object sender, RoutedEventArgs e)
        {
            var current = GetCurrentTab();
            var others = _tabs.Where(t => t != current).ToList();
            if (current == null || others.Count == 0)
            {
                ShowStatusMessage("Open another tab to compare with.");
                return;
            }
            var picker = new Dialogs.PickTabWindow(others) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedTab == null)
                return;
            var other = picker.SelectedTab;
            ShowCompare(current,
                current.Document.FileName, current.GetActiveEditorControl()?.GetText() ?? current.Document.Content,
                other.Document.FileName, other.GetActiveEditorControl()?.GetText() ?? other.Document.Content);
        }

        // ═══════════ Workpad 2.x: technical tools ═══════════

        /// <summary>Selection-first transform pipeline; results/extractions open in a new tab.</summary>
        private void RunTechnicalCommand(string actionId)
        {
            var tab = GetCurrentTab();
            var editor = tab?.GetActiveEditorControl();
            if (tab == null || editor == null)
                return;

            // Formatted view: selections can't be expressed as source offsets, so
            // run the transform on the selected text and replace it in place.
            if (editor.ViewMode == EditorViewMode.Formatted &&
                editor.TryGetFormattedSelection(out var formattedSelection))
            {
                RunFormattedSelectionCommand(actionId, editor, formattedSelection);
                return;
            }

            var (start, length) = editor.GetSelectionRange();
            var text = editor.GetText();

            switch (actionId)
            {
                case "validatexml":
                {
                    var target = length > 0 ? text.Substring(start, length) : text;
                    var issue = XmlToolsService.Validate(target, out var error);
                    if (issue == null)
                        ShowStatusMessage("XML is well-formed.");
                    else
                    {
                        ShowStatusMessage(error ?? "XML is not well-formed.");
                        if (issue.Line > 0)
                            editor.GoToLine(issue.Line);
                    }
                    return;
                }
                case "domains":
                case "hosts":
                {
                    var fn = actionId == "domains"
                        ? (Func<string, TransformResult>)UrlToolsService.GetDomains
                        : UrlToolsService.GetHosts;
                    var outcome = TextTransformService.Apply(text, start, length, fn);
                    if (!outcome.Success || outcome.NoChange)
                        ShowStatusMessage(outcome.Message ?? "No domains or hosts found.");
                    else
                        OpenResultTab(actionId == "domains" ? "Domains" : "Hosts", outcome.Replacement);
                    return;
                }
                default:
                {
                    Func<string, TransformResult> fn = actionId switch
                    {
                        "sanitize" => UrlToolsService.Sanitize,
                        "unsanitize" => UrlToolsService.Unsanitize,
                        "beautifyxml" => XmlToolsService.Beautify,
                        "minifyxml" => XmlToolsService.Minify,
                        _ => _ => TransformResult.Fail("Unknown command.")
                    };
                    var outcome = TextTransformService.Apply(text, start, length, fn);
                    if (!outcome.Success)
                        ShowStatusMessage(outcome.Message ?? "The command could not run on this text.");
                    else if (outcome.NoChange)
                        ShowStatusMessage(outcome.Message ?? "Nothing to change.");
                    else
                    {
                        editor.ReplaceTextRange(outcome.Offset, outcome.Length, outcome.Replacement);
                        ShowStatusMessage("Done — one undo restores the previous text.");
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Runs a technical command against the Formatted view's selected text and
        /// replaces that selection in place (whole-document fallback stays in the
        /// normal pipeline when nothing is selected).
        /// </summary>
        private void RunFormattedSelectionCommand(string actionId, EditorControl editor, string selectionText)
        {
            switch (actionId)
            {
                case "validatexml":
                {
                    var issue = XmlToolsService.Validate(selectionText, out var error);
                    ShowStatusMessage(issue == null
                        ? "XML is well-formed."
                        : error ?? "XML is not well-formed.");
                    return;
                }
                case "domains":
                case "hosts":
                {
                    var fn = actionId == "domains"
                        ? (Func<string, TransformResult>)UrlToolsService.GetDomains
                        : UrlToolsService.GetHosts;
                    var outcome = TextTransformService.Apply(selectionText, 0, selectionText.Length, fn);
                    if (!outcome.Success || outcome.NoChange)
                        ShowStatusMessage(outcome.Message ?? "No domains or hosts found.");
                    else
                        OpenResultTab(actionId == "domains" ? "Domains" : "Hosts", outcome.Replacement);
                    return;
                }
                default:
                {
                    Func<string, TransformResult> fn = actionId switch
                    {
                        "sanitize" => UrlToolsService.Sanitize,
                        "unsanitize" => UrlToolsService.Unsanitize,
                        "beautifyxml" => XmlToolsService.Beautify,
                        "minifyxml" => XmlToolsService.Minify,
                        _ => _ => TransformResult.Fail("Unknown command.")
                    };
                    var outcome = TextTransformService.Apply(selectionText, 0, selectionText.Length, fn);
                    if (!outcome.Success)
                        ShowStatusMessage(outcome.Message ?? "The command could not run on this text.");
                    else if (outcome.NoChange)
                        ShowStatusMessage(outcome.Message ?? "Nothing to change.");
                    else
                    {
                        editor.ReplaceFormattedSelection(outcome.Replacement);
                        ShowStatusMessage("Done — one undo restores the previous text.");
                    }
                    return;
                }
            }
        }

        /// <summary>Opens extraction results in a new untitled tab.</summary>
        private void OpenResultTab(string title, string content)
        {
            // Results are a plain list — inherit the originating tab's view mode
            // rather than forcing Source.
            var viewMode = TabControl.SelectedIndex >= 0 && TabControl.SelectedIndex < _tabs.Count
                ? _tabs[TabControl.SelectedIndex].ViewMode
                : EditorViewMode.Source;
            var document = new Document { Content = content };
            var tab = new DocumentTab(document, viewMode)
            {
                CustomTitle = title,
            };
            var editor = new EditorControl
            {
                Document = document,
                ViewMode = viewMode
            };
            tab.EditorControl = editor;
            HookEditorEvents(editor);
            WireTechnicalCommands(editor);
            var tabItem = CreateTabItem(tab, editor);
            _tabs.Add(tab);
            TabControl.Items.Add(tabItem);
            TabControl.SelectedItem = tabItem;
        }

        private void WireTechnicalCommands(EditorControl editor) =>
            editor.TechnicalCommandRequested += RunTechnicalCommand;

        private void NormalizeXmlCompare_Click(object sender, RoutedEventArgs e)
        {
            _normalizeXmlCompare = sender is MenuItem item && item.IsChecked;
        }

        private void SanitizeUrls_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("sanitize");
        private void UnsanitizeUrls_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("unsanitize");
        private void GetDomains_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("domains");
        private void GetHosts_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("hosts");
        private void BeautifyXml_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("beautifyxml");
        private void MinifyXml_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("minifyxml");
        private void ValidateXml_Click(object s, RoutedEventArgs e) => RunTechnicalCommand("validatexml");

        // ═══════════ Workpad 2.x: command palette ═══════════

        private void CommandPalette_Click(object sender, RoutedEventArgs e)
        {
            var palette = new Dialogs.CommandPaletteWindow(_commandRegistry) { Owner = this };
            palette.Show();
        }

        private void RegisterCommands()
        {
            void Reg(string id, string title, string category, string shortcut, Action action)
                => _commandRegistry.Register(new CommandEntry { Id = id, Title = title, Category = category, Shortcut = shortcut, Execute = action });

            Reg("file.new", "New Tab", "File", "Ctrl+T", CreateNewTab);
            Reg("file.open", "Open…", "File", "Ctrl+O", () => Open_Click(this, new RoutedEventArgs()));
            Reg("file.save", "Save", "File", "Ctrl+S", () => Save_Click(this, new RoutedEventArgs()));
            Reg("file.saveas", "Save As…", "File", "Ctrl+Shift+S", () => SaveAs_Click(this, new RoutedEventArgs()));
            Reg("file.closetab", "Close Tab", "File", "Ctrl+W", () => CloseTab_Click(this, new RoutedEventArgs()));
            Reg("file.reopen", "Reopen Closed Tab", "File", "Ctrl+Shift+T", () => ReopenClosedTab_Click(this, new RoutedEventArgs()));
            Reg("file.closeall", "Close All Tabs", "File", "", CloseAllTabs);
            Reg("file.closesaved", "Close Saved Tabs", "File", "", CloseSavedTabs);
            Reg("file.startfresh", "Start Fresh", "File", "", () => StartFresh_Click(this, new RoutedEventArgs()));
            Reg("edit.find", "Find…", "Edit", "Ctrl+F", () => Find_Click(this, new RoutedEventArgs()));
            Reg("edit.replace", "Replace…", "Edit", "Ctrl+H", () => Replace_Click(this, new RoutedEventArgs()));
            Reg("edit.gotoline", "Go to Line…", "Edit", "Ctrl+G", () => LineColButton_Click(this, new RoutedEventArgs()));
            Reg("format.bold", "Bold", "Format", "Ctrl+B", () => Bold_Click(this, new RoutedEventArgs()));
            Reg("format.italic", "Italic", "Format", "Ctrl+I", () => Italic_Click(this, new RoutedEventArgs()));
            Reg("format.link", "Insert Link", "Format", "Ctrl+K", () => InsertLink_Click(this, new RoutedEventArgs()));
            Reg("view.source", "Markdown Source", "View", "", () => ViewSource_Click(this, new RoutedEventArgs()));
            Reg("view.formatted", "Formatted View", "View", "", () => ViewFormatted_Click(this, new RoutedEventArgs()));
            Reg("view.toggle", "Toggle View Mode", "View", "Ctrl+Shift+M", () => ViewModeToggle_Click(this, new RoutedEventArgs()));
            Reg("view.split", "Split View", "View", "", () => SplitView_Click(this, new RoutedEventArgs()));
            Reg("view.bionic", "Bionic Reading", "View", "", () => BionicReading_Click(this, new RoutedEventArgs()));
            Reg("view.toolbar", "Toggle Toolbar", "View", "", () => App.Settings.ShowToolbar = !App.Settings.ShowToolbar);
            Reg("view.linenumbers", "Toggle Line Numbers", "View", "", () => App.Settings.ShowLineNumbers = !App.Settings.ShowLineNumbers);
            Reg("tools.sanitize", "Sanitize URLs", "Tools", "", () => RunTechnicalCommand("sanitize"));
            Reg("tools.unsanitize", "Unsanitize URLs", "Tools", "", () => RunTechnicalCommand("unsanitize"));
            Reg("tools.domains", "Get Domains", "Tools", "", () => RunTechnicalCommand("domains"));
            Reg("tools.hosts", "Get Hosts", "Tools", "", () => RunTechnicalCommand("hosts"));
            Reg("tools.beautify", "Beautify XML", "Tools", "", () => RunTechnicalCommand("beautifyxml"));
            Reg("tools.minify", "Minify XML", "Tools", "", () => RunTechnicalCommand("minifyxml"));
            Reg("tools.validate", "Validate XML", "Tools", "", () => RunTechnicalCommand("validatexml"));
            Reg("tools.compare.saved", "Compare with Saved", "Tools", "", () => CompareWithSaved_Click(this, new RoutedEventArgs()));
            Reg("tools.compare.tab", "Compare with Another Tab…", "Tools", "", () => CompareWithTab_Click(this, new RoutedEventArgs()));
            Reg("tab.lock", "Toggle Tab Lock", "Tabs", "", () => { var t = GetCurrentTab(); if (t != null) t.IsLocked = !t.IsLocked; });
            Reg("settings.preferences", "Preferences…", "Settings", "", () => Preferences_Click(this, new RoutedEventArgs()));
            Reg("help.updates", "Check for Updates…", "Help", "", () => CheckForUpdates_Click(this, new RoutedEventArgs()));
            Reg("help.about", "About Work Notes", "Help", "", () => About_Click(this, new RoutedEventArgs()));
        }

        // ═══════════ Workpad 2.x: help ═══════════

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            ShowStatusMessage("Checking for updates…");
            var info = await new UpdateCheckService().CheckAsync();
            if (info.Error != null)
            {
                ShowStatusMessage(info.Error);
                return;
            }
            if (info.UpdateAvailable)
            {
                var answer = MessageBox.Show(
                    $"A new version is available: {info.LatestTag}.\n\nOpen the releases page to download it?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = info.LatestUrl ?? UpdateCheckService.ReleasesPage,
                            UseShellExecute = true
                        });
                    }
                    catch { /* browser launch is best-effort */ }
                }
            }
            else
            {
                ShowStatusMessage("You're on the latest version.");
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "2.x";
            MessageBox.Show(
                $"Work Notes {version}\n\nA calm, technical-friendly scratchpad for Windows.",
                "About Work Notes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ═══════════ Workpad 2.x: session snapshot restore ═══════════

        /// <summary>Restores one tab from a recovery snapshot (untitled/dirty
        /// buffers get their content back; clean file tabs reopen the file).</summary>
        private bool RestoreSnapshot(TabSnapshot snap, TabSessionState? st)
        {
            var viewMode = st?.ViewMode
                ?? (Enum.TryParse<EditorViewMode>(snap.ViewMode, out var vm) ? vm : App.Settings.DefaultEditorView);

            if (snap.Content == null && !string.IsNullOrEmpty(snap.FilePath) && File.Exists(snap.FilePath))
            {
                // Clean file-backed tab: reopen from disk.
                OpenFileInNewTab(snap.FilePath);
                var tab = _tabs.LastOrDefault(t =>
                    string.Equals(t.Document.FilePath, snap.FilePath, StringComparison.OrdinalIgnoreCase));
                if (tab == null)
                    return false;
                tab.RecoveryId = snap.Id;
                tab.IsLocked = snap.IsLocked || (st?.IsLocked ?? false);
                ApplyRestoredState(tab, st, snap);
                return true;
            }

            if (snap.Content == null && string.IsNullOrEmpty(snap.FilePath))
                return false; // nothing to restore

            // Untitled or dirty buffer: rebuild content, keep the file link when
            // the file still exists (never overwrite it silently).
            var doc = new Document
            {
                FilePath = (!string.IsNullOrEmpty(snap.FilePath) && File.Exists(snap.FilePath)) ? snap.FilePath : null,
                Content = snap.Content ?? string.Empty,
                IsDirty = snap.IsDirty
            };
            var restored = new DocumentTab(doc, viewMode)
            {
                RecoveryId = snap.Id,
                IsLocked = snap.IsLocked || (st?.IsLocked ?? false),
                CustomTitle = doc.FilePath == null ? (snap.FileName ?? "Recovered note") : null,
            };
            var editor = new EditorControl { Document = doc, ViewMode = viewMode };
            restored.EditorControl = editor;
            HookEditorEvents(editor);
            WireTechnicalCommands(editor);
            var item = CreateTabItem(restored, editor);
            _tabs.Add(restored);
            TabControl.Items.Add(item);
            if (!string.IsNullOrEmpty(doc.FilePath))
                _fileMonitor?.Watch(doc.FilePath);
            ApplyRestoredState(restored, st, snap);
            return true;
        }

        private void ApplyRestoredState(DocumentTab tab, TabSessionState? st, TabSnapshot snap)
        {
            var cursor = st?.CursorPosition ?? snap.CursorPosition;
            var scroll = st?.ScrollOffset ?? snap.ScrollOffset;
            var isSplit = (st?.IsSplitViewEnabled ?? false) || snap.IsSplitViewEnabled;
            if (isSplit)
            {
                EnableSplitViewForTab(tab);
                var split = tab.SplitViewContainer;
                if (split != null)
                {
                    var secondaryPath = snap.SecondaryFilePath ?? st?.SecondaryFilePath;
                    if (snap.SecondaryContent != null)
                    {
                        // Untitled or dirty bottom buffer — restore its content;
                        // keep the file link only when the file still exists.
                        var secondaryDoc = new Document
                        {
                            FilePath = (!string.IsNullOrEmpty(secondaryPath) && File.Exists(secondaryPath)) ? secondaryPath : null,
                            Content = snap.SecondaryContent,
                            IsDirty = snap.SecondaryIsDirty || string.IsNullOrEmpty(secondaryPath)
                        };
                        split.SetPaneDocument(split.BottomEditorPane, secondaryDoc);
                        if (secondaryDoc.FilePath != null)
                            _fileMonitor?.Watch(secondaryDoc.FilePath);
                    }
                    else if (!string.IsNullOrEmpty(secondaryPath) && File.Exists(secondaryPath))
                    {
                        split.OpenDocumentInPane(split.BottomEditorPane, secondaryPath);
                    }
                    split.SetPaneReadOnly(0, snap.TopPaneReadOnly || (st?.TopPaneReadOnly ?? false));
                    split.SetPaneReadOnly(1, snap.BottomPaneReadOnly || (st?.BottomPaneReadOnly ?? false));
                    split.ActivatePane(st?.ActivePaneIndex ?? snap.ActivePaneIndex);
                }
            }
            Dispatcher.InvokeAsync(() =>
            {
                var editor = tab.SplitViewContainer?.TopEditorPane?.EditorControl ?? tab.EditorControl;
                if (editor != null && editor.Editor.Text.Length >= cursor)
                {
                    editor.Editor.CaretOffset = cursor;
                    editor.Editor.ScrollToVerticalOffset(scroll);
                }
                var secondary = tab.SplitViewContainer?.BottomEditorPane?.EditorControl;
                var secCursor = st?.SecondaryCursorPosition ?? snap.SecondaryCursorPosition;
                var secScroll = st?.SecondaryScrollOffset ?? snap.SecondaryScrollOffset;
                if (secondary != null && secondary.Editor.Text.Length >= secCursor)
                {
                    secondary.Editor.CaretOffset = secCursor;
                    secondary.Editor.ScrollToVerticalOffset(secScroll);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
