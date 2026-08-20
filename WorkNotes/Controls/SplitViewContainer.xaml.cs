using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using WorkNotes.Models;

namespace WorkNotes.Controls
{
    /// <summary>
    /// Split view container that manages two synchronized editor panes (top/bottom).
    /// Implements IDisposable to stop timers and unsubscribe events on teardown.
    /// </summary>
    public partial class SplitViewContainer : UserControl, IDisposable
    {
        private Document? _document;
        private EditorViewMode _viewMode = EditorViewMode.Formatted;
        private EditorPane? _activePane;
        private bool _isSyncing;
        private DispatcherTimer? _formattedSyncTimer;
        private TextDocument? _sharedTextDocument;
        private EventHandler? _sharedDocTextChangedHandler;
        private RichTextBox? _formattedSyncEditor;
        private TextChangedEventHandler? _formattedTextChangedHandler;

        public event EventHandler<EditorPane>? ActivePaneChanged;

        public SplitViewContainer()
        {
            InitializeComponent();

            // Hook up focus tracking
            TopPane.GotPaneFocus += (s, e) => SetActivePane(TopPane);
            BottomPane.GotPaneFocus += (s, e) => SetActivePane(BottomPane);

            // Initialize formatted mode sync timer (throttled to avoid churn)
            _formattedSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _formattedSyncTimer.Tick += FormattedSyncTimer_Tick;

            // Set top pane as initially active
            _activePane = TopPane;
        }

        /// <summary>
        /// Gets the currently active (focused) pane.
        /// </summary>
        public EditorPane? ActivePane => _activePane;

        /// <summary>
        /// Gets the top pane.
        /// </summary>
        public EditorPane TopEditorPane => TopPane;

        /// <summary>
        /// Gets the bottom pane.
        /// </summary>
        public EditorPane BottomEditorPane => BottomPane;

        /// <summary>
        /// Initializes the split view with a document and view mode.
        /// </summary>
        public void Initialize(Document document, EditorViewMode viewMode)
        {
            _document = document;
            _viewMode = viewMode;

            // Create two EditorControl instances
            var topEditor = new EditorControl();
            var bottomEditor = new EditorControl();

            TopPane.EditorControl = topEditor;
            BottomPane.EditorControl = bottomEditor;

            // CRITICAL: Implement shared buffer pattern for Source mode
            if (viewMode == EditorViewMode.Source)
            {
                InitializeSharedSourceMode(topEditor, bottomEditor, document);
            }
            else
            {
                InitializeFormattedMode(topEditor, bottomEditor, document);
            }

            // Set top pane as active
            SetActivePane(TopPane);
            TopPane.FocusEditor();
        }

        /// <summary>
        /// INDUSTRY BEST PRACTICE: Shared TextDocument for Source mode.
        /// Both panes reference the SAME document instance, giving perfect sync
        /// with shared undo/redo stack and instant updates.
        /// </summary>
        private void InitializeSharedSourceMode(EditorControl topEditor, EditorControl bottomEditor, Document document)
        {
            System.Diagnostics.Debug.WriteLine("[SplitView] Initializing SHARED SOURCE MODE");

            DetachFormattedSyncHandler();

            // Unsubscribe old handler to prevent leaking on reinitialize / view-mode switch
            if (_sharedTextDocument != null && _sharedDocTextChangedHandler != null)
            {
                _sharedTextDocument.TextChanged -= _sharedDocTextChangedHandler;
            }

            // Create a shared TextDocument from the canonical content
            _sharedTextDocument = new TextDocument(document.Content);

            // Set both panes to use the shared document
            topEditor.SetSharedSourceDocument(_sharedTextDocument, document, EditorViewMode.Source);
            bottomEditor.SetSharedSourceDocument(_sharedTextDocument, document, EditorViewMode.Source);

            // Hook up text change to mark dirty (stored so we can unsubscribe later)
            _sharedDocTextChangedHandler = (s, e) =>
            {
                if (!_isSyncing && document != null)
                {
                    document.IsDirty = true;
                }
            };
            _sharedTextDocument.TextChanged += _sharedDocTextChangedHandler;
        }

        /// <summary>
        /// INDUSTRY BEST PRACTICE: Projection-based Formatted mode.
        /// Top pane is editable, bottom pane is read-only mirror (prevents dual-edit conflicts).
        /// Both are views (projections) of the canonical Document.Content.
        /// </summary>
        private void InitializeFormattedMode(EditorControl topEditor, EditorControl bottomEditor, Document document)
        {
            System.Diagnostics.Debug.WriteLine("[SplitView] Initializing PROJECTION FORMATTED MODE");

            DetachFormattedSyncHandler();

            // Set view mode BEFORE document to ensure correct initialization
            topEditor.ViewMode = EditorViewMode.Formatted;
            bottomEditor.ViewMode = EditorViewMode.Formatted;

            // Top pane: Editable formatted view
            topEditor.Document = document;

            // Bottom pane: Read-only formatted mirror
            bottomEditor.Document = document;
            
            // The lower pane is a projection. Only the upper pane is editable.
            bottomEditor.GetFormattedEditorControl().IsReadOnly = true;

            // Hook up top editor changes to sync to bottom (throttled), retaining
            // the handler so repeated mode switches cannot accumulate callbacks.
            _formattedSyncEditor = topEditor.GetFormattedEditorControl();
            _formattedTextChangedHandler = (s, e) =>
            {
                if (!_isSyncing)
                {
                    _formattedSyncTimer?.Stop();
                    _formattedSyncTimer?.Start();
                }
            };
            _formattedSyncEditor.TextChanged += _formattedTextChangedHandler;
        }

        private void DetachFormattedSyncHandler()
        {
            if (_formattedSyncEditor != null && _formattedTextChangedHandler != null)
            {
                _formattedSyncEditor.TextChanged -= _formattedTextChangedHandler;
            }

            _formattedSyncEditor = null;
            _formattedTextChangedHandler = null;
        }

        /// <summary>
        /// Throttled sync for formatted mode: serialize top pane, update document, refresh bottom pane.
        /// </summary>
        private void FormattedSyncTimer_Tick(object? sender, EventArgs e)
        {
            _formattedSyncTimer?.Stop();

            if (_isSyncing || _document == null || TopPane.EditorControl == null || BottomPane.EditorControl == null)
                return;

            try
            {
                _isSyncing = true;

                // Serialize from top pane (editable) to get current content
                var currentContent = TopPane.EditorControl.GetText();

                // Update canonical document
                _document.Content = currentContent;

                // Refresh bottom pane from canonical source (read-only mirror)
                BottomPane.EditorControl.RefreshFromDocument();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SplitView] Formatted sync error: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// Sets the active pane and raises event.
        /// </summary>
        private void SetActivePane(EditorPane pane)
        {
            if (_activePane != pane)
            {
                _activePane = pane;
                System.Diagnostics.Debug.WriteLine($"[SplitView] Active pane changed to: {pane.PaneId}");
                ActivePaneChanged?.Invoke(this, pane);
            }
        }

        /// <summary>
        /// Synchronizes the current content to the in-memory document.
        /// In Source mode: shared buffer is already canonical.
        /// In Formatted mode: serialize from top (editable) pane.
        /// </summary>
        public void SyncToDocument()
        {
            if (_document == null)
                return;

            _formattedSyncTimer?.Stop();

            if (_viewMode == EditorViewMode.Source && _sharedTextDocument != null)
            {
                _document.Content = _sharedTextDocument.Text;
            }
            else if (_viewMode == EditorViewMode.Formatted && TopPane.EditorControl != null)
            {
                TopPane.EditorControl.SyncToDocument();
            }
        }

        /// <summary>
        /// Synchronizes the split editor and explicitly persists it to disk.
        /// </summary>
        public void SaveToDocument()
        {
            if (_document == null)
                return;

            SyncToDocument();
            _document.Save(_document.Content);
        }

        /// <summary>
        /// Switches view mode (Source <-> Formatted).
        /// </summary>
        public void SwitchViewMode(EditorViewMode newMode)
        {
            if (_viewMode == newMode || _document == null)
                return;

            // Preserve edits in memory while changing representation. Switching
            // views must never persist changes without an explicit Save command.
            SyncToDocument();

            _viewMode = newMode;

            // Reinitialize with new mode
            if (TopPane.EditorControl != null && BottomPane.EditorControl != null)
            {
                if (newMode == EditorViewMode.Source)
                {
                    InitializeSharedSourceMode(TopPane.EditorControl, BottomPane.EditorControl, _document);
                }
                else
                {
                    InitializeFormattedMode(TopPane.EditorControl, BottomPane.EditorControl, _document);
                }
            }
        }

        /// <summary>
        /// Stops timers, unsubscribes events, and cleans up child EditorControls.
        /// </summary>
        public void Dispose()
        {
            _formattedSyncTimer?.Stop();
            _formattedSyncTimer = null;
            DetachFormattedSyncHandler();

            if (_sharedTextDocument != null && _sharedDocTextChangedHandler != null)
            {
                _sharedTextDocument.TextChanged -= _sharedDocTextChangedHandler;
            }
            _sharedTextDocument = null;

            // Clean up child editor controls
            TopPane.EditorControl?.Cleanup();
            BottomPane.EditorControl?.Cleanup();

            System.Diagnostics.Debug.WriteLine("[SplitView] Disposed");
        }
    }
}
