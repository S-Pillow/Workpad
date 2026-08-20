using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorkNotes.Models;

namespace WorkNotes.Controls
{
    /// <summary>
    /// Hosts two fully independent document sessions. Each pane owns its own
    /// document, editor buffer, selection, undo history, and scroll position.
    /// </summary>
    public partial class SplitViewContainer : UserControl, IDisposable
    {
        private Document? _topDocument;
        private Document? _bottomDocument;
        private EditorViewMode _viewMode = EditorViewMode.Formatted;
        private EditorPane? _activePane;
        private PropertyChangedEventHandler? _topDocumentChanged;
        private PropertyChangedEventHandler? _bottomDocumentChanged;

        public event EventHandler<EditorPane>? ActivePaneChanged;
        public event EventHandler<SplitPaneClosedEventArgs>? PaneClosed;
        public event EventHandler<DocumentOpenedEventArgs>? DocumentOpened;
        public event EventHandler? StateChanged;

        public SplitViewContainer()
        {
            InitializeComponent();
            TopPane.GotPaneFocus += (_, _) => SetActivePane(TopPane);
            BottomPane.GotPaneFocus += (_, _) => SetActivePane(BottomPane);
        }

        public EditorPane? ActivePane => _activePane;
        public EditorPane TopEditorPane => TopPane;
        public EditorPane BottomEditorPane => BottomPane;
        public Document? TopDocument => _topDocument;
        public Document? BottomDocument => _bottomDocument;
        public Document? ActiveDocument => ReferenceEquals(_activePane, BottomPane) ? _bottomDocument : _topDocument;
        public int ActivePaneIndex => ReferenceEquals(_activePane, BottomPane) ? 1 : 0;
        public bool IsTopPaneReadOnly => TopReadOnly.IsChecked == true;
        public bool IsBottomPaneReadOnly => BottomReadOnly.IsChecked == true;
        public IEnumerable<Document> Documents
        {
            get
            {
                if (_topDocument != null) yield return _topDocument;
                if (_bottomDocument != null) yield return _bottomDocument;
            }
        }

        public void Initialize(Document primaryDocument, EditorViewMode viewMode)
        {
            _viewMode = viewMode;

            var topEditor = new EditorControl { ViewMode = viewMode };
            var bottomEditor = new EditorControl { ViewMode = viewMode };
            TopPane.EditorControl = topEditor;
            BottomPane.EditorControl = bottomEditor;

            SetDocument(TopPane, primaryDocument);
            SetDocument(BottomPane, new Document());

            TopReadOnly.IsChecked = true;
            BottomReadOnly.IsChecked = false;
            topEditor.SetReadOnly(true);
            bottomEditor.SetReadOnly(false);

            SetActivePane(TopPane);
        }

        public void SwitchViewMode(EditorViewMode newMode)
        {
            if (_viewMode == newMode)
                return;

            SyncToDocuments();
            _viewMode = newMode;
            if (TopPane.EditorControl != null) TopPane.EditorControl.ViewMode = newMode;
            if (BottomPane.EditorControl != null) BottomPane.EditorControl.ViewMode = newMode;
        }

        public void SyncToDocuments()
        {
            TopPane.EditorControl?.SyncToDocument();
            BottomPane.EditorControl?.SyncToDocument();
        }

        public bool SaveActiveDocument(Window owner, bool saveAs = false)
        {
            var pane = _activePane ?? TopPane;
            return SavePane(pane, owner, saveAs);
        }

        public bool PromptSaveAllDocuments(Window owner)
        {
            SyncToDocuments();
            return PromptSaveIfDirty(TopPane, owner) && PromptSaveIfDirty(BottomPane, owner);
        }

        /// <summary>
        /// Selects the document that will remain when split view is toggled off,
        /// prompting for changes in the pane that would be discarded.
        /// </summary>
        public bool TryPrepareCollapse(Window owner, out Document remainingDocument)
        {
            SyncToDocuments();
            var remainingPane = _activePane ?? TopPane;
            var closingPane = ReferenceEquals(remainingPane, TopPane) ? BottomPane : TopPane;

            if (!PromptSaveIfDirty(closingPane, owner))
            {
                remainingDocument = ActiveDocument ?? _topDocument ?? new Document();
                return false;
            }

            remainingDocument = GetDocument(remainingPane) ?? new Document();
            return true;
        }

        public bool OpenDocumentInPane(EditorPane pane, string filePath, Window? owner = null)
        {
            if (!File.Exists(filePath))
                return false;

            if (owner != null && !PromptSaveIfDirty(pane, owner))
                return false;

            try
            {
                var document = new Document { FilePath = filePath };
                document.Load();
                SetDocument(pane, document);
                SetActivePane(pane);
                pane.FocusEditor();
                DocumentOpened?.Invoke(this, new DocumentOpenedEventArgs(document));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Error opening file: {ex.Message}", "Open file",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void SetPaneReadOnly(int paneIndex, bool isReadOnly)
        {
            if (paneIndex == 1) BottomReadOnly.IsChecked = isReadOnly;
            else TopReadOnly.IsChecked = isReadOnly;
        }

        public void ActivatePane(int paneIndex)
        {
            var pane = paneIndex == 1 ? BottomPane : TopPane;
            SetActivePane(pane);
            pane.FocusEditor();
        }

        private void OpenPane(EditorPane pane)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Notes (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Markdown (*.md;*.markdown)|*.md;*.markdown|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt"
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                OpenDocumentInPane(pane, dialog.FileName, Window.GetWindow(this));
        }

        private void SetDocument(EditorPane pane, Document document)
        {
            if (ReferenceEquals(pane, TopPane))
            {
                DetachDocumentHandler(_topDocument, _topDocumentChanged);
                _topDocument = document;
                _topDocumentChanged = (_, _) =>
                {
                    UpdatePaneHeader(TopPane);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                };
                _topDocument.PropertyChanged += _topDocumentChanged;
            }
            else
            {
                DetachDocumentHandler(_bottomDocument, _bottomDocumentChanged);
                _bottomDocument = document;
                _bottomDocumentChanged = (_, _) =>
                {
                    UpdatePaneHeader(BottomPane);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                };
                _bottomDocument.PropertyChanged += _bottomDocumentChanged;
            }

            if (pane.EditorControl != null)
            {
                pane.EditorControl.Document = document;
                pane.EditorControl.ViewMode = _viewMode;
                pane.EditorControl.SetReadOnly(IsPaneReadOnly(pane));
            }
            UpdatePaneHeader(pane);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdatePaneHeader(EditorPane pane)
        {
            var document = GetDocument(pane);
            var label = document?.FileName ?? "Untitled";
            if (document?.IsDirty == true) label += " •";

            if (ReferenceEquals(pane, TopPane)) TopFileName.Text = label;
            else BottomFileName.Text = label;
        }

        private Document? GetDocument(EditorPane pane) =>
            ReferenceEquals(pane, TopPane) ? _topDocument : _bottomDocument;

        private bool IsPaneReadOnly(EditorPane pane) =>
            ReferenceEquals(pane, TopPane) ? TopReadOnly.IsChecked == true : BottomReadOnly.IsChecked == true;

        private bool PromptSaveIfDirty(EditorPane pane, Window owner)
        {
            var document = GetDocument(pane);
            pane.EditorControl?.SyncToDocument();
            if (document?.IsDirty != true)
                return true;

            var result = MessageBox.Show(owner,
                $"Do you want to save changes to {document.FileName}?",
                "Work Notes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                return SavePane(pane, owner, false);
            return result == MessageBoxResult.No;
        }

        private bool SavePane(EditorPane pane, Window owner, bool saveAs)
        {
            var document = GetDocument(pane);
            var editor = pane.EditorControl;
            if (document == null || editor == null)
                return false;

            editor.SyncToDocument();
            var previousPath = document.FilePath;
            if (saveAs || string.IsNullOrEmpty(document.FilePath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Notes (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Markdown (*.md;*.markdown)|*.md;*.markdown|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = document.FileName
                };
                if (dialog.ShowDialog(owner) != true)
                    return false;
                document.FilePath = dialog.FileName;
            }

            try
            {
                document.Save(document.Content);
                UpdatePaneHeader(pane);
                return true;
            }
            catch (Exception ex)
            {
                document.FilePath = previousPath;
                MessageBox.Show(owner, $"Error saving file: {ex.Message}", "Save file",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ClosePane(EditorPane pane)
        {
            var owner = Window.GetWindow(this);
            if (owner == null || !PromptSaveIfDirty(pane, owner))
                return;

            var remainingPane = ReferenceEquals(pane, TopPane) ? BottomPane : TopPane;
            var remainingDocument = GetDocument(remainingPane) ?? new Document();
            PaneClosed?.Invoke(this, new SplitPaneClosedEventArgs(remainingDocument));
        }

        private void SetActivePane(EditorPane pane)
        {
            if (ReferenceEquals(_activePane, pane))
                return;
            _activePane = pane;
            ActivePaneChanged?.Invoke(this, pane);
        }

        private static void DetachDocumentHandler(Document? document, PropertyChangedEventHandler? handler)
        {
            if (document != null && handler != null)
                document.PropertyChanged -= handler;
        }

        private void OpenTop_Click(object sender, RoutedEventArgs e) => OpenPane(TopPane);
        private void OpenBottom_Click(object sender, RoutedEventArgs e) => OpenPane(BottomPane);
        private void CloseTop_Click(object sender, RoutedEventArgs e) => ClosePane(TopPane);
        private void CloseBottom_Click(object sender, RoutedEventArgs e) => ClosePane(BottomPane);
        private void TopReadOnly_Changed(object sender, RoutedEventArgs e)
        {
            TopPane?.EditorControl?.SetReadOnly(TopReadOnly?.IsChecked == true);
            if (TopPane != null) SetActivePane(TopPane);
        }

        private void BottomReadOnly_Changed(object sender, RoutedEventArgs e)
        {
            BottomPane?.EditorControl?.SetReadOnly(BottomReadOnly?.IsChecked == true);
            if (BottomPane != null) SetActivePane(BottomPane);
        }

        public void Dispose()
        {
            DetachDocumentHandler(_topDocument, _topDocumentChanged);
            DetachDocumentHandler(_bottomDocument, _bottomDocumentChanged);
            TopPane.EditorControl?.Cleanup();
            BottomPane.EditorControl?.Cleanup();
        }
    }

    public sealed class SplitPaneClosedEventArgs : EventArgs
    {
        public SplitPaneClosedEventArgs(Document remainingDocument) => RemainingDocument = remainingDocument;
        public Document RemainingDocument { get; }
    }

    public sealed class DocumentOpenedEventArgs : EventArgs
    {
        public DocumentOpenedEventArgs(Document document) => Document = document;
        public Document Document { get; }
    }
}
