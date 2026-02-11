using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WorkNotes.Controls;

namespace WorkNotes.Models
{
    /// <summary>
    /// Represents a tab containing a document and its editor.
    /// </summary>
    public class DocumentTab : INotifyPropertyChanged
    {
        private Document _document;
        private EditorControl? _editorControl;
        private EditorViewMode _viewMode;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DocumentTab(Document document, EditorViewMode initialViewMode = EditorViewMode.Formatted)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _viewMode = initialViewMode;
            
            // Listen to document changes to update tab header
            _document.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Document.DisplayName) ||
                    e.PropertyName == nameof(Document.FileName) ||
                    e.PropertyName == nameof(Document.IsDirty))
                {
                    OnPropertyChanged(nameof(HeaderText));
                }
            };
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
                    _document = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeaderText));
                }
            }
        }

        /// <summary>
        /// Gets or sets the editor control for this tab.
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

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
