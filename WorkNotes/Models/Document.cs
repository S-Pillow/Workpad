using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace WorkNotes.Models
{
    /// <summary>
    /// Represents a single text document with file path and dirty state tracking.
    /// </summary>
    public class Document : INotifyPropertyChanged
    {
        private string? _filePath;
        private bool _isDirty;
        private string _content = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets or sets the file path. Null for new unsaved documents.
        /// </summary>
        public string? FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileName));
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// Gets the file name without path, or "Untitled" if no file.
        /// </summary>
        public string FileName => FilePath != null ? Path.GetFileName(FilePath) : "Untitled";

        /// <summary>
        /// Gets the display name with dirty indicator.
        /// </summary>
        public string DisplayName => FileName + (IsDirty ? " *" : string.Empty);

        /// <summary>
        /// Gets or sets whether the document has unsaved changes.
        /// </summary>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// Gets or sets the document content (cached).
        /// </summary>
        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Loads content from the file path.
        /// </summary>
        public void Load()
        {
            if (FilePath == null)
                throw new InvalidOperationException("Cannot load document without a file path.");

            Content = File.ReadAllText(FilePath, Encoding.UTF8);
            IsDirty = false;
        }

        /// <summary>
        /// Saves content to the file path.
        /// </summary>
        public void Save(string content)
        {
            if (FilePath == null)
                throw new InvalidOperationException("Cannot save document without a file path.");

            File.WriteAllText(FilePath, content, Encoding.UTF8);
            Content = content;
            IsDirty = false;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
