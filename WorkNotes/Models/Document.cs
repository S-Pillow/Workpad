using System;
using System.ComponentModel;
using System.Diagnostics;
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
        public string DisplayName => FileName + (IsDirty ? " •" : string.Empty);

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
        /// Saves content to the file path using an atomic write (temp file + rename).
        /// On power loss or crash the file is either the old version or the new version,
        /// never a partial/corrupt write.
        /// </summary>
        public void Save(string content)
        {
            if (FilePath == null)
                throw new InvalidOperationException("Cannot save document without a file path.");

            FileHelper.AtomicWriteText(FilePath, content, Encoding.UTF8);
            LastSavedFingerprint = FileHelper.Fingerprint(content, Encoding.UTF8);
            Content = content;
            IsDirty = false;
        }

        /// <summary>SHA-256 of the exact bytes the last <see cref="Save"/> wrote
        /// (encoding preamble + encoded content). Lets the external-file monitor
        /// suppress watcher echoes of our own save without rereading the target —
        /// which an external writer could already have replaced.</summary>
        public byte[]? LastSavedFingerprint { get; private set; }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Shared helper for crash-safe file writes.
    /// Pattern: write to temp file in same directory, then atomic rename.
    /// On NTFS same-volume MoveFileEx with MOVEFILE_REPLACE_EXISTING is atomic.
    /// </summary>
    internal static class FileHelper
    {
        /// <summary>SHA-256 of the byte layout <see cref="AtomicWriteText"/>
        /// produces for this content/encoding pair: StreamWriter emits the
        /// encoding preamble once, then the encoded content.</summary>
        public static byte[] Fingerprint(string content, Encoding encoding)
        {
            using var ih = System.Security.Cryptography.IncrementalHash
                .CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            ih.AppendData(encoding.GetPreamble());
            ih.AppendData(encoding.GetBytes(content));
            return ih.GetHashAndReset();
        }

        public static void AtomicWriteText(string targetPath, string content, Encoding encoding)
        {
            var dir = Path.GetDirectoryName(targetPath)
                      ?? throw new InvalidOperationException($"Cannot determine directory for: {targetPath}");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = Path.Combine(dir, ".~" + Path.GetRandomFileName());
            try
            {
                // Write to temp file, then flush to disk via FileStream
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, encoding))
                {
                    sw.Write(content);
                    sw.Flush();
                    fs.Flush(flushToDisk: true); // Ensure data is on stable storage
                }

                // Atomic replace — same-volume rename on NTFS
                File.Move(tempPath, targetPath, overwrite: true);
            }
            catch
            {
                // Clean up temp file on any failure; original target remains untouched
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"[FileHelper] Failed to clean up temp file {tempPath}: {cleanupEx.Message}");
                }
                throw; // Re-throw so callers can surface the real error
            }
        }
    }
}
