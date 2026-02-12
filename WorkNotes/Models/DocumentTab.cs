using System;
using System.ComponentModel;
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
        private PropertyChangedEventHandler? _documentChangeHandler;

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
        /// Gets or sets the split view container (split view mode).
        /// </summary>
        public SplitViewContainer? SplitViewContainer
        {
            get => _splitViewContainer;
            set
            {
                if (_splitViewContainer != value)
                {
                    _splitViewContainer = value;
                    OnPropertyChanged();
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
        /// Gets the tab header text with dirty indicator.
        /// </summary>
        public string HeaderText
        {
            get
            {
                var name = _document.FileName;
                var dirtyIndicator = _document.IsDirty ? " •" : "";
                return name + dirtyIndicator;
            }
        }

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
