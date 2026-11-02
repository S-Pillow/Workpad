using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WorkNotes.Controls;

namespace WorkNotes.Models
{
    /// <summary>
    /// Represents a tab containing a document and its editor.
    /// Supports both single editor mode and split view mode.
    /// </summary>
    public class DocumentTab : INotifyPropertyChanged
    {
        private Document _document;
        private EditorControl? _editorControl;
        private SplitViewContainer? _splitViewContainer;
        private bool _isSplitViewEnabled;
        private EditorViewMode _viewMode;
        private bool _isLocked;
        private string? _recoveryId;
        private string? _customTitle;
        private PropertyChangedEventHandler? _documentChangeHandler;
        private EventHandler? _splitViewChangeHandler;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DocumentTab(Document document, EditorViewMode initialViewMode = EditorViewMode.Formatted)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _viewMode = initialViewMode;
            
            // Create and subscribe the handler
            _documentChangeHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(Document.DisplayName) ||
                    e.PropertyName == nameof(Document.FileName) ||
                    e.PropertyName == nameof(Document.IsDirty))
                {
                    OnPropertyChanged(nameof(HeaderText));
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            };
            _document.PropertyChanged += _documentChangeHandler;
        }

        /// <summary>
        /// Gets the document associated with this tab.
        /// </summary>
        public Document Document
        {
            get => _document;
            set
            {
                if (_document != value)
                {
                    // Unsubscribe from old document
                    if (_document != null && _documentChangeHandler != null)
                    {
                        _document.PropertyChanged -= _documentChangeHandler;
                    }

                    _document = value;
                    
                    // Subscribe to new document
                    if (_document != null && _documentChangeHandler != null)
                    {
                        _document.PropertyChanged += _documentChangeHandler;
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderText));
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            }
        }

        /// <summary>
        /// Gets or sets the editor control for this tab (single editor mode).
        /// </summary>
        public EditorControl? EditorControl
        {
            get => _editorControl;
            set
            {
                if (_editorControl != value)
                {
                    _editorControl = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Per-tab host for the conflict banner and the reusable secondary pane
        /// (compare / inspector). Created with the tab's content wrapper.
        /// </summary>
        public SecondaryPaneHost? PaneHost { get; set; }

        /// <summary>
        /// Gets or sets the split view container (split view mode).
        /// </summary>
        public SplitViewContainer? SplitViewContainer
        {
            get => _splitViewContainer;
            set
            {
                if (_splitViewContainer != value)
                {
                    if (_splitViewContainer != null && _splitViewChangeHandler != null)
                        _splitViewContainer.StateChanged -= _splitViewChangeHandler;

                    _splitViewContainer = value;

                    if (_splitViewContainer != null)
                    {
                        _splitViewChangeHandler = (_, _) =>
                        {
                            OnPropertyChanged(nameof(HeaderText));
                            OnPropertyChanged(nameof(HasUnsavedChanges));
                        };
                        _splitViewContainer.StateChanged += _splitViewChangeHandler;
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderText));
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            }
        }

        /// <summary>
        /// Gets or sets whether split view is enabled for this tab.
        /// </summary>
        public bool IsSplitViewEnabled
        {
            get => _isSplitViewEnabled;
            set
            {
                if (_isSplitViewEnabled != value)
                {
                    _isSplitViewEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderText));
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            }
        }

        /// <summary>
        /// Gets or sets the view mode for this tab.
        /// </summary>
        public EditorViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the tab is locked against all close paths.
        /// </summary>
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked != value)
                {
                    _isLocked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderText));
                }
            }
        }

        /// <summary>
        /// Recovery snapshot id linking this tab to its persisted snapshot.
        /// </summary>
        public string? RecoveryId
        {
            get => _recoveryId;
            set { _recoveryId = value; }
        }

        /// <summary>
        /// Optional user-facing title for restored untitled buffers
        /// (e.g. "Recovered notes").
        /// </summary>
        public string? CustomTitle
        {
            get => _customTitle;
            set
            {
                if (_customTitle != value)
                {
                    _customTitle = value;
                    OnPropertyChanged(nameof(HeaderText));
                }
            }
        }

        /// <summary>
        /// Gets the tab header text with dirty indicator.
        /// </summary>
        public string HeaderText
        {
            get
            {
                // Return just the filename without the dirty indicator
                // The visual UnsavedDot will show the dirty state instead
                if (_isSplitViewEnabled && _splitViewContainer != null)
                    return $"{_splitViewContainer.TopDocument?.FileName ?? "Untitled"}  ·  {_splitViewContainer.BottomDocument?.FileName ?? "Untitled"}";

                if (!string.IsNullOrEmpty(_customTitle))
                    return _customTitle;

                return _document.FileName;
            }
        }

        public bool HasUnsavedChanges => _isSplitViewEnabled && _splitViewContainer != null
            ? _splitViewContainer.Documents.Any(document => document.IsDirty)
            : _document.IsDirty;

        /// <summary>
        /// Gets the active editor control (handles both single and split view modes).
        /// </summary>
        public EditorControl? GetActiveEditorControl()
        {
            if (_isSplitViewEnabled && _splitViewContainer != null)
            {
                return _splitViewContainer.ActivePane?.EditorControl;
            }
            return _editorControl;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
