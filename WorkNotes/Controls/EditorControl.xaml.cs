using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using WorkNotes.Models;
using WorkNotes.Services;

namespace WorkNotes.Controls
{
    /// <summary>
    /// Dual-representation editor: Source (Markdown) and Formatted (Rich Text) views.
    /// </summary>
    public partial class EditorControl : UserControl
    {
        private Document? _document;
        private bool _isLoading;
        private bool _isSyncing;
        private EditorViewMode _viewMode = EditorViewMode.Formatted;
        private MarkdownParser? _markdownParser;
        private MarkdownSerializer? _markdownSerializer;
        private LinkDetector? _linkDetector;
        private LinkNavigationService? _linkNavigationService;
        private SpellCheckService? _spellCheckService;
        private SpellCheckMarkerService? _spellCheckMarkerService;
        private SpellCheckBackgroundRenderer? _spellCheckRenderer;
        private BionicReadingTransformer? _bionicTransformer;
        private System.Windows.Threading.DispatcherTimer? _linkDetectionTimer;
        private System.Windows.Threading.DispatcherTimer? _formattedLinkDetectionTimer;
        private System.Windows.Threading.DispatcherTimer? _spellCheckTimer;

        public EditorControl()
        {
            InitializeComponent();

            // Initialize link navigation service
            _linkNavigationService = new LinkNavigationService(
                () => App.Settings.ConfirmBeforeOpeningLinks,
                (value) =>
                {
                    App.Settings.ConfirmBeforeOpeningLinks = value;
                    App.Settings.Save();
                });

            // Initialize spell check service
            try
            {
                System.Diagnostics.Debug.WriteLine("Initializing SpellCheckService...");
                _spellCheckService = App.SpellCheckService ?? throw new Exception("SpellCheckService not initialized");
                _spellCheckMarkerService = new SpellCheckMarkerService(SourceEditor.Document);
                _spellCheckRenderer = new SpellCheckBackgroundRenderer(_spellCheckMarkerService);
                SourceEditor.TextArea.TextView.BackgroundRenderers.Add(_spellCheckRenderer);
                System.Diagnostics.Debug.WriteLine("SpellCheckService initialized successfully");
            }
            catch (Exception ex)
            {
                // Spell check failed to initialize - non-critical, continue without it
                System.Diagnostics.Debug.WriteLine($"Spell check initialization failed: {ex}");
                MessageBox.Show($"Spell check failed to initialize: {ex.Message}\n\nThe app will continue without spell checking.", 
                    "Spell Check Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Initialize services
            var linkBrush = TryFindResource("App.Accent") as SolidColorBrush ?? Brushes.Blue;
            _markdownParser = new MarkdownParser(
                linkBrush, 
                HandleLinkClick,
                () => App.Settings.EnableAutoLinkDetection,
                () => App.Settings.EnableBionicReading,
                () => App.Settings.BionicStrength);
            _markdownSerializer = new MarkdownSerializer();
            _linkDetector = new LinkDetector(linkBrush);

            // Initialize Bionic Reading transformer
            _bionicTransformer = new BionicReadingTransformer(
                () => App.Settings.EnableBionicReading,
                () => App.Settings.BionicStrength);
            SourceEditor.TextArea.TextView.LineTransformers.Add(_bionicTransformer);

            // Hook up source editor events
            SourceEditor.TextChanged += SourceEditor_TextChanged;
            SourceEditor.Options.EnableRectangularSelection = true;
            SourceEditor.Options.EnableTextDragDrop = true;
            SourceEditor.Options.ShowTabs = true;
            SourceEditor.Options.ShowSpaces = false;

            // Link detection timer for source view (throttled)
            _linkDetectionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _linkDetectionTimer.Tick += (s, e) =>
            {
                _linkDetectionTimer.Stop();
                ApplyLinkDetection();
            };

            // Link detection timer for formatted view (throttled)
            _formattedLinkDetectionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _formattedLinkDetectionTimer.Tick += (s, e) =>
            {
                _formattedLinkDetectionTimer.Stop();
                DetectAndLinkifyBareUrls();
            };

            // Spell check timer (throttled)
            _spellCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _spellCheckTimer.Tick += (s, e) =>
            {
                _spellCheckTimer.Stop();
                RunSpellCheck();
            };

            // Mouse events for Ctrl+Click
            SourceEditor.PreviewMouseLeftButtonDown += SourceEditor_PreviewMouseLeftButtonDown;
            
            // Context menu
            SourceEditor.ContextMenuOpening += SourceEditor_ContextMenuOpening;
            FormattedEditor.ContextMenuOpening += FormattedEditor_ContextMenuOpening;

            // Hook up formatted editor events
            FormattedEditor.TextChanged += FormattedEditor_TextChanged;
            FormattedEditor.Document.PagePadding = new Thickness(0);
            
            // Set spell check based on settings
            UpdateSpellCheckSettings();

            // Apply font settings
            ApplyFontSettings();

            // Custom copy handling
            SourceEditor.PreviewKeyDown += Editor_PreviewKeyDown;
            FormattedEditor.PreviewKeyDown += Editor_PreviewKeyDown;

            // Apply theme colors
            ApplySelectionColors();
            this.Loaded += (s, e) => ApplySelectionColors();
        }

        private void ApplySelectionColors()
        {
            if (TryFindResource("App.EditorSelection") is SolidColorBrush selectionBrush)
            {
                SourceEditor.TextArea.SelectionBrush = selectionBrush;
            }

            if (TryFindResource("App.EditorSelectionBorder") is SolidColorBrush selectionBorderBrush)
            {
                SourceEditor.TextArea.SelectionBorder = new Pen(selectionBorderBrush, 1);
            }
        }

        /// <summary>
        /// Gets the underlying source editor (for compatibility).
        /// </summary>
        public TextEditor Editor => SourceEditor;

        /// <summary>
        /// Gets or sets the current document.
        /// </summary>
        public Document? Document
        {
            get => _document;
            set
            {
                if (_document != value)
                {
                    _document = value;
                    LoadDocumentIntoEditor();
                }
            }
        }

        /// <summary>
        /// Gets or sets the current view mode.
        /// </summary>
        public EditorViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                    SwitchViewMode();
                }
            }
        }

        /// <summary>
        /// Refreshes the formatted view (used when settings change).
        /// </summary>
        public void RefreshFormattedView()
        {
            if (_viewMode == EditorViewMode.Formatted && _markdownParser != null)
            {
                var markdownText = _markdownSerializer?.SerializeToMarkdown(FormattedEditor.Document) ?? string.Empty;
                var flowDoc = _markdownParser.ParseToFlowDocument(markdownText);
                
                // Apply bionic reading if enabled
                if (App.Settings.EnableBionicReading)
                {
                    BionicReadingProcessor.ApplyBionicReading(flowDoc, App.Settings.BionicStrength);
                }
                
                FormattedEditor.Document = flowDoc;
            }
        }

        private void LoadDocumentIntoEditor()
        {
            if (_document == null)
            {
                _isLoading = true;
                SourceEditor.Document = new TextDocument();
                FormattedEditor.Document.Blocks.Clear();
                _isLoading = false;
                return;
            }

            _isLoading = true;

            // Load into source editor
            SourceEditor.Document = new TextDocument(_document.Content);

            // Load into formatted editor
            if (_markdownParser != null)
            {
                var flowDoc = _markdownParser.ParseToFlowDocument(_document.Content);
                
                // Apply bionic reading if enabled
                if (App.Settings.EnableBionicReading)
                {
                    BionicReadingProcessor.ApplyBionicReading(flowDoc, App.Settings.BionicStrength);
                }
                
                FormattedEditor.Document = flowDoc;
            }

            _isLoading = false;
            _document.IsDirty = false;

            // Show correct editor
            SwitchViewMode();
        }

        private void SwitchViewMode()
        {
            if (_isSyncing) return;

            _isSyncing = true;

            if (_viewMode == EditorViewMode.Formatted)
            {
                // Sync source -> formatted
                if (_markdownParser != null && !_isLoading)
                {
                    var markdownText = SourceEditor.Text;
                    var flowDoc = _markdownParser.ParseToFlowDocument(markdownText);
                    
                    // Apply bionic reading if enabled
                    if (App.Settings.EnableBionicReading)
                    {
                        BionicReadingProcessor.ApplyBionicReading(flowDoc, App.Settings.BionicStrength);
                    }
                    
                    FormattedEditor.Document = flowDoc;
                }

                // Remove link detector from source
                var existing = SourceEditor.TextArea.TextView.LineTransformers.OfType<LinkDetector>().FirstOrDefault();
                if (existing != null)
                {
                    SourceEditor.TextArea.TextView.LineTransformers.Remove(existing);
                }

                SourceEditor.Visibility = Visibility.Collapsed;
                FormattedEditor.Visibility = Visibility.Visible;
                FormattedEditor.Focus();
            }
            else
            {
                // Sync formatted -> source
                if (_markdownSerializer != null && !_isLoading)
                {
                    var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
                    SourceEditor.Text = markdownText;
                }

                // Apply link detection to source
                ApplyLinkDetection();

                FormattedEditor.Visibility = Visibility.Collapsed;
                SourceEditor.Visibility = Visibility.Visible;
                SourceEditor.Focus();
            }

            _isSyncing = false;
        }

        /// <summary>
        /// Saves the editor content to the document.
        /// </summary>
        public void SaveToDocument()
        {
            if (_document != null)
            {
                if (_viewMode == EditorViewMode.Formatted && _markdownSerializer != null)
                {
                    var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
                    _document.Save(markdownText);
                }
                else
                {
                    _document.Save(SourceEditor.Text);
                }
            }
        }

        /// <summary>
        /// Gets the current editor content as markdown.
        /// </summary>
        public string GetText()
        {
            if (_viewMode == EditorViewMode.Formatted && _markdownSerializer != null)
            {
                return _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
            }
            return SourceEditor.Text;
        }

        /// <summary>
        /// Gets the currently selected text.
        /// </summary>
        public string GetSelectedText()
        {
            if (_viewMode == EditorViewMode.Formatted)
            {
                return FormattedEditor.Selection.Text;
            }
            return SourceEditor.SelectedText;
        }

        private void SourceEditor_TextChanged(object? sender, EventArgs e)
        {
            if (_isLoading || _isSyncing || _document == null)
                return;

            if (!_document.IsDirty)
            {
                _document.IsDirty = true;
            }

            // Throttle link detection
            if (_viewMode == EditorViewMode.Source)
            {
                _linkDetectionTimer?.Stop();
                _linkDetectionTimer?.Start();

                // Throttle spell check
                if (App.Settings.EnableSpellCheck)
                {
                    _spellCheckTimer?.Stop();
                    _spellCheckTimer?.Start();
                }
            }
        }

        private void ApplyLinkDetection()
        {
            if (_linkDetector == null || _viewMode != EditorViewMode.Source)
                return;

            _linkDetector.ClearLinks();

            // Remove existing detector
            var existing = SourceEditor.TextArea.TextView.LineTransformers.OfType<LinkDetector>().FirstOrDefault();
            if (existing != null)
            {
                SourceEditor.TextArea.TextView.LineTransformers.Remove(existing);
            }

            // Add detector
            SourceEditor.TextArea.TextView.LineTransformers.Add(_linkDetector);
            SourceEditor.TextArea.TextView.Redraw();
        }

        private void RunSpellCheck()
        {
            if (_spellCheckService == null)
                return;

            if (!App.Settings.EnableSpellCheck)
            {
                // Clear spell check marks in both views
                if (_spellCheckMarkerService != null)
                {
                    _spellCheckMarkerService.Clear();
                }
                ClearFormattedSpellCheck();
                
                if (_viewMode == EditorViewMode.Source)
                {
                    SourceEditor.TextArea.TextView.Redraw();
                }
                return;
            }

            if (_viewMode == EditorViewMode.Source)
            {
                RunSourceSpellCheck();
            }
            else if (_viewMode == EditorViewMode.Formatted)
            {
                RunFormattedSpellCheck();
            }
        }

        private void RunSourceSpellCheck()
        {
            if (_spellCheckMarkerService == null)
                return;

            try
            {
                _spellCheckMarkerService.Clear();

                var text = SourceEditor.Text;
                var tokens = _spellCheckService!.TokenizeText(text);

                foreach (var token in tokens)
                {
                    if (!token.IsCorrect)
                    {
                        _spellCheckMarkerService.AddMarker(token.StartOffset, token.EndOffset - token.StartOffset);
                    }
                }

                SourceEditor.TextArea.TextView.Redraw();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Source spell check error: {ex.Message}");
            }
        }

        private void RunFormattedSpellCheck()
        {
            // Formatted view now uses WPF's built-in spell checker (enabled via XAML)
            // No custom implementation needed - WPF handles it automatically
            // Just make sure it's enabled/disabled based on settings
            FormattedEditor.SpellCheck.IsEnabled = App.Settings.EnableSpellCheck;
        }

        private void ApplySpellCheckToFlowDocument(FlowDocument document, HashSet<string> misspelledWords)
        {
            foreach (var block in document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    ApplySpellCheckToParagraph(paragraph, misspelledWords);
                }
            }
        }

        private void ApplySpellCheckToParagraph(Paragraph paragraph, HashSet<string> misspelledWords)
        {
            var inlinesToProcess = paragraph.Inlines.ToList();
            var newInlines = new List<Inline>();

            foreach (var inline in inlinesToProcess)
            {
                if (inline is Run run)
                {
                    // Skip runs inside hyperlinks
                    if (run.Parent is Hyperlink)
                    {
                        newInlines.Add(run);
                        continue;
                    }

                    // Split this Run into word-level Runs for spell checking
                    var text = run.Text;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        newInlines.Add(run);
                        continue;
                    }

                    // Find all tokens (words, spaces, punctuation)
                    var tokenMatches = System.Text.RegularExpressions.Regex.Matches(text, @"(\b[\w']+\b|\s+|[^\w\s']+)");
                    
                    if (tokenMatches.Count <= 1)
                    {
                        // Single token, check if it's a misspelled word
                        var trimmed = text.Trim('\'', ' ', '\t', '\r', '\n');
                        if (!string.IsNullOrWhiteSpace(trimmed) && 
                            System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\w']+$") &&
                            misspelledWords.Contains(trimmed))
                        {
                            // Apply red wavy underline
                            var decoration = new TextDecoration
                            {
                                Location = TextDecorationLocation.Underline,
                                Pen = new Pen(Brushes.Red, 1)
                                {
                                    DashStyle = new DashStyle(new double[] { 2, 2 }, 0)
                                }
                            };
                            
                            if (run.TextDecorations == null)
                            {
                                run.TextDecorations = new TextDecorationCollection();
                            }
                            
                            // Only add if not already present
                            if (!run.TextDecorations.Any(td => td.Location == TextDecorationLocation.Underline && 
                                                               td.Pen?.Brush == Brushes.Red))
                            {
                                run.TextDecorations.Add(decoration);
                            }
                        }
                        newInlines.Add(run);
                        continue;
                    }

                    // Multiple tokens - split into separate runs
                    foreach (System.Text.RegularExpressions.Match match in tokenMatches)
                    {
                        var token = match.Value;
                        var trimmedToken = token.Trim('\'');
                        
                        var newRun = new Run(token)
                        {
                            FontWeight = run.FontWeight,
                            FontStyle = run.FontStyle,
                            FontSize = run.FontSize,
                            FontFamily = run.FontFamily,
                            Foreground = run.Foreground,
                            Background = run.Background
                        };

                        // Check if this token is a misspelled word
                        if (!string.IsNullOrWhiteSpace(trimmedToken) && 
                            System.Text.RegularExpressions.Regex.IsMatch(token, @"\b[\w']+\b") &&
                            misspelledWords.Contains(trimmedToken))
                        {
                            // Apply red wavy underline to this word only
                            var decoration = new TextDecoration
                            {
                                Location = TextDecorationLocation.Underline,
                                Pen = new Pen(Brushes.Red, 1)
                                {
                                    DashStyle = new DashStyle(new double[] { 2, 2 }, 0)
                                }
                            };
                            newRun.TextDecorations = new TextDecorationCollection { decoration };
                        }

                        newInlines.Add(newRun);
                    }
                }
                else if (inline is Span span)
                {
                    // Recursively process spans
                    ProcessSpanForSpellCheck(span, misspelledWords);
                    newInlines.Add(span);
                }
                else
                {
                    // Keep other inline types as-is
                    newInlines.Add(inline);
                }
            }

            // Replace all inlines at once (avoid collection modification during enumeration)
            paragraph.Inlines.Clear();
            foreach (var inline in newInlines)
            {
                paragraph.Inlines.Add(inline);
            }
        }

        private void ProcessSpanForSpellCheck(Span span, HashSet<string> misspelledWords)
        {
            foreach (var inline in span.Inlines.ToList())
            {
                if (inline is Run run)
                {
                    var words = System.Text.RegularExpressions.Regex.Matches(run.Text, @"\b[\w']+\b");
                    bool hasMisspelling = false;

                    foreach (System.Text.RegularExpressions.Match match in words)
                    {
                        var word = match.Value.Trim('\'');
                        if (misspelledWords.Contains(word))
                        {
                            hasMisspelling = true;
                            break;
                        }
                    }

                    if (hasMisspelling && !(inline.Parent is Hyperlink))
                    {
                        var decoration = new TextDecoration
                        {
                            Location = TextDecorationLocation.Underline,
                            Pen = new Pen(Brushes.Red, 1)
                            {
                                DashStyle = new DashStyle(new double[] { 2, 2 }, 0)
                            }
                        };
                        
                        if (run.TextDecorations == null)
                        {
                            run.TextDecorations = new TextDecorationCollection();
                        }
                        
                        if (!run.TextDecorations.Any(td => td.Location == TextDecorationLocation.Underline && 
                                                           td.Pen?.Brush == Brushes.Red))
                        {
                            run.TextDecorations.Add(decoration);
                        }
                    }
                }
                else if (inline is Span nestedSpan)
                {
                    ProcessSpanForSpellCheck(nestedSpan, misspelledWords);
                }
            }
        }

        private void ClearFormattedSpellCheck()
        {
            try
            {
                foreach (var block in FormattedEditor.Document.Blocks)
                {
                    if (block is Paragraph paragraph)
                    {
                        ClearSpellCheckFromParagraph(paragraph);
                    }
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        private void ClearSpellCheckFromParagraph(Paragraph paragraph)
        {
            foreach (var inline in paragraph.Inlines.ToList())
            {
                if (inline is Run run && run.TextDecorations != null)
                {
                    var toRemove = run.TextDecorations.Where(td => 
                        td.Location == TextDecorationLocation.Underline && 
                        td.Pen?.Brush == Brushes.Red).ToList();
                    
                    foreach (var decoration in toRemove)
                    {
                        run.TextDecorations.Remove(decoration);
                    }
                }
                else if (inline is Span span)
                {
                    ClearSpellCheckFromSpan(span);
                }
            }
        }

        private void ClearSpellCheckFromSpan(Span span)
        {
            foreach (var inline in span.Inlines.ToList())
            {
                if (inline is Run run && run.TextDecorations != null)
                {
                    var toRemove = run.TextDecorations.Where(td => 
                        td.Location == TextDecorationLocation.Underline && 
                        td.Pen?.Brush == Brushes.Red).ToList();
                    
                    foreach (var decoration in toRemove)
                    {
                        run.TextDecorations.Remove(decoration);
                    }
                }
                else if (inline is Span nestedSpan)
                {
                    ClearSpellCheckFromSpan(nestedSpan);
                }
            }
        }

        public void RefreshSpellCheck()
        {
            UpdateSpellCheckSettings();
            RunSpellCheck();
        }

        public void RefreshBionicReading()
        {
            // For Source view, just redraw
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.TextArea.TextView.Redraw();
            }
            // For Formatted view, re-parse and apply bionic
            else if (_viewMode == EditorViewMode.Formatted && _markdownParser != null)
            {
                var markdownText = _markdownSerializer?.SerializeToMarkdown(FormattedEditor.Document) ?? string.Empty;
                var flowDoc = _markdownParser.ParseToFlowDocument(markdownText);
                
                // Apply bionic reading if enabled
                if (App.Settings.EnableBionicReading)
                {
                    BionicReadingProcessor.ApplyBionicReading(flowDoc, App.Settings.BionicStrength);
                }
                
                _isSyncing = true;
                FormattedEditor.Document = flowDoc;
                _isSyncing = false;
            }
        }

        public void ApplyFontSettings()
        {
            var fontFamily = new System.Windows.Media.FontFamily(App.Settings.FontFamily);
            var fontSize = App.Settings.FontSize;

            // Apply to Source editor (AvalonEdit)
            SourceEditor.FontFamily = fontFamily;
            SourceEditor.FontSize = fontSize;
            SourceEditor.WordWrap = App.Settings.WordWrap;

            // Apply to Formatted editor (RichTextBox)
            FormattedEditor.FontFamily = fontFamily;
            FormattedEditor.FontSize = fontSize;
            
            // Word wrap is always on for RichTextBox, but we can control horizontal scrollbar
            if (App.Settings.WordWrap)
            {
                FormattedEditor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            else
            {
                FormattedEditor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }
        
        private void UpdateSpellCheckSettings()
        {
            // Enable/disable WPF's built-in spell checker for Formatted view
            FormattedEditor.SpellCheck.IsEnabled = App.Settings.EnableSpellCheck;
            
            // Source view uses our custom spell checker (always follows the setting)
            if (App.Settings.EnableSpellCheck && _viewMode == EditorViewMode.Source)
            {
                RunSpellCheck();
            }
            else if (!App.Settings.EnableSpellCheck)
            {
                // Clear spell check markers in source view
                _spellCheckMarkerService?.Clear();
                SourceEditor.TextArea.TextView.Redraw();
            }
        }

        private void DetectAndLinkifyBareUrls()
        {
            // Skip if bionic reading is enabled - the constant re-parsing breaks bionic effect
            // Links will still be detected on initial load and view switches
            if (_viewMode != EditorViewMode.Formatted || _isSyncing || _markdownParser == null || App.Settings.EnableBionicReading)
                return;

            _isSyncing = true;

            try
            {
                // Get current caret position to restore it
                var caretPosition = FormattedEditor.CaretPosition;
                var caretOffset = FormattedEditor.Document.ContentStart.GetOffsetToPosition(caretPosition);

                // Serialize current document to markdown
                var currentMarkdown = _markdownSerializer?.SerializeToMarkdown(FormattedEditor.Document) ?? string.Empty;

                // Re-parse to detect new URLs
                var linkBrush = TryFindResource("App.Accent") as SolidColorBrush ?? Brushes.Blue;
                var newFlowDoc = _markdownParser.ParseToFlowDocument(currentMarkdown);
                
                // Apply bionic reading if enabled
                if (App.Settings.EnableBionicReading)
                {
                    BionicReadingProcessor.ApplyBionicReading(newFlowDoc, App.Settings.BionicStrength);
                }

                // Replace document
                FormattedEditor.Document = newFlowDoc;

                // Restore caret position (approximate)
                try
                {
                    var newPosition = FormattedEditor.Document.ContentStart.GetPositionAtOffset(caretOffset);
                    if (newPosition != null)
                    {
                        FormattedEditor.CaretPosition = newPosition;
                    }
                }
                catch
                {
                    // If restoration fails, just leave caret at end
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void FormattedEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _isSyncing || _document == null)
                return;

            if (!_document.IsDirty)
            {
                _document.IsDirty = true;
            }

            // Throttle link detection in formatted view
            // BUT: Skip link detection if Bionic Reading is enabled to avoid document churning
            if (_viewMode == EditorViewMode.Formatted && !App.Settings.EnableBionicReading)
            {
                _formattedLinkDetectionTimer?.Stop();
                _formattedLinkDetectionTimer?.Start();

                // Throttle spell check in formatted view
                if (App.Settings.EnableSpellCheck)
                {
                    _spellCheckTimer?.Stop();
                    _spellCheckTimer?.Start();
                }
            }
        }

        /// <summary>
        /// Custom keyboard handling for copy.
        /// </summary>
        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                HandleCopy();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Custom copy handling with multiple clipboard formats.
        /// </summary>
        private void HandleCopy()
        {
            var dataObject = new DataObject();

            if (_viewMode == EditorViewMode.Formatted)
            {
                var selection = FormattedEditor.Selection;
                if (!selection.IsEmpty)
                {
                    // Plain text (pure content, no markers)
                    var plainText = selection.Text;
                    dataObject.SetText(plainText, TextDataFormat.Text);
                    dataObject.SetText(plainText, TextDataFormat.UnicodeText);

                    // RTF format (preserve formatting)
                    try
                    {
                        var range = new TextRange(selection.Start, selection.End);
                        using (var stream = new System.IO.MemoryStream())
                        {
                            range.Save(stream, DataFormats.Rtf);
                            stream.Position = 0;
                            using (var reader = new System.IO.StreamReader(stream))
                            {
                                var rtf = reader.ReadToEnd();
                                dataObject.SetData(DataFormats.Rtf, rtf);
                            }
                        }
                    }
                    catch { }

                    Clipboard.SetDataObject(dataObject, true);
                }
            }
            else
            {
                // Source view - copy raw markdown
                var selectedText = SourceEditor.SelectedText;
                if (!string.IsNullOrEmpty(selectedText))
                {
                    dataObject.SetText(selectedText, TextDataFormat.Text);
                    dataObject.SetText(selectedText, TextDataFormat.UnicodeText);
                    Clipboard.SetDataObject(dataObject, true);
                }
            }
        }

        private void HandleLinkClick(string url)
        {
            _linkNavigationService?.TryNavigate(url, Window.GetWindow(this));
        }

        #region Formatting Methods

        /// <summary>
        /// Applies bold formatting.
        /// </summary>
        public void ApplyBold()
        {
            if (_viewMode == EditorViewMode.Formatted)
            {
                ToggleFormatting(FontWeights.Bold, null);
            }
            else
            {
                WrapSelection("**", "**");
            }
        }

        /// <summary>
        /// Applies italic formatting.
        /// </summary>
        public void ApplyItalic()
        {
            if (_viewMode == EditorViewMode.Formatted)
            {
                ToggleFormatting(null, FontStyles.Italic);
            }
            else
            {
                WrapSelection("*", "*");
            }
        }

        /// <summary>
        /// Toggles bold/italic formatting in formatted view.
        /// </summary>
        private void ToggleFormatting(FontWeight? weight, FontStyle? style)
        {
            var selection = FormattedEditor.Selection;
            if (selection.IsEmpty)
                return;

            if (weight.HasValue)
            {
                var currentWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
                var newWeight = currentWeight.Equals(FontWeights.Bold) ? FontWeights.Normal : FontWeights.Bold;
                selection.ApplyPropertyValue(TextElement.FontWeightProperty, newWeight);
            }

            if (style.HasValue)
            {
                var currentStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
                var newStyle = currentStyle.Equals(FontStyles.Italic) ? FontStyles.Normal : FontStyles.Italic;
                selection.ApplyPropertyValue(TextElement.FontStyleProperty, newStyle);
            }
        }

        /// <summary>
        /// Inserts a Markdown link.
        /// </summary>
        public void InsertLink(string url, string label)
        {
            if (_viewMode == EditorViewMode.Formatted)
            {
                // Stop any pending auto-detection
                _formattedLinkDetectionTimer?.Stop();

                // In formatted view, insert as hyperlink
                var hyperlink = new Hyperlink(new Run(label))
                {
                    NavigateUri = new Uri(url, UriKind.RelativeOrAbsolute),
                    Foreground = TryFindResource("App.Accent") as SolidColorBrush ?? Brushes.Blue,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = Cursors.Hand
                };
                hyperlink.Click += (s, e) =>
                {
                    e.Handled = true;
                    HandleLinkClick(url);
                };

                var selection = FormattedEditor.Selection;
                if (!selection.IsEmpty)
                {
                    // Replace selection
                    selection.Start.InsertTextInRun(string.Empty);
                    var insertPosition = selection.Start;
                    selection.Text = string.Empty;
                    insertPosition.Paragraph?.Inlines.Add(hyperlink);
                }
                else
                {
                    // Insert at caret
                    var caretPos = FormattedEditor.CaretPosition;
                    if (caretPos.Paragraph != null)
                    {
                        caretPos.Paragraph.Inlines.Add(hyperlink);
                        FormattedEditor.CaretPosition = hyperlink.ContentEnd.GetNextInsertionPosition(LogicalDirection.Forward) ?? hyperlink.ContentEnd;
                    }
                }
            }
            else
            {
                // Source view - insert markdown syntax
                var linkText = $"[{label}]({url})";
                var selection = SourceEditor.TextArea.Selection;

                if (!selection.IsEmpty)
                {
                    var segment = selection.Segments.First();
                    SourceEditor.Document.Replace(segment.StartOffset, segment.EndOffset - segment.StartOffset, linkText);
                }
                else
                {
                    var offset = SourceEditor.CaretOffset;
                    SourceEditor.Document.Insert(offset, linkText);
                    SourceEditor.CaretOffset = offset + linkText.Length;
                }
            }
        }

        /// <summary>
        /// Wraps selection with markdown markers (source view only).
        /// </summary>
        private void WrapSelection(string prefix, string suffix)
        {
            var textArea = SourceEditor.TextArea;
            var document = SourceEditor.Document;
            var selection = textArea.Selection;

            if (!selection.IsEmpty)
            {
                var selectedText = selection.GetText();
                var segment = selection.Segments.First();
                var offset = segment.StartOffset;
                var length = segment.EndOffset - segment.StartOffset;
                var wrappedText = prefix + selectedText + suffix;

                document.Replace(offset, length, wrappedText);
                textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(
                    textArea,
                    offset + prefix.Length,
                    offset + prefix.Length + selectedText.Length);
                textArea.Caret.Offset = offset + prefix.Length + selectedText.Length;
            }
            else
            {
                var offset = textArea.Caret.Offset;
                var wordStart = offset;
                var wordEnd = offset;

                while (wordStart > 0 && IsWordChar(document.GetCharAt(wordStart - 1)))
                {
                    wordStart--;
                }

                while (wordEnd < document.TextLength && IsWordChar(document.GetCharAt(wordEnd)))
                {
                    wordEnd++;
                }

                if (wordEnd > wordStart)
                {
                    var word = document.GetText(wordStart, wordEnd - wordStart);
                    var wrappedText = prefix + word + suffix;
                    document.Replace(wordStart, wordEnd - wordStart, wrappedText);
                    textArea.Caret.Offset = wordStart + wrappedText.Length;
                }
                else
                {
                    var placeholder = prefix + suffix;
                    document.Insert(offset, placeholder);
                    textArea.Caret.Offset = offset + prefix.Length;
                }
            }

            SourceEditor.Focus();
        }

        private bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        #endregion

        #region Link Interaction

        /// <summary>
        /// Handles Ctrl+Click to open links in source view.
        /// </summary>
        private void SourceEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                return;

            if (_linkDetector == null || _viewMode != EditorViewMode.Source)
                return;

            var position = SourceEditor.GetPositionFromPoint(e.GetPosition(SourceEditor));
            if (position == null)
                return;

            var offset = SourceEditor.Document.GetOffset(position.Value.Location);

            // Check if click is on a link
            var link = _linkDetector.DetectedLinks.FirstOrDefault(l => 
                offset >= l.StartOffset && offset <= l.EndOffset);

            if (link != null)
            {
                OpenLink(link.Url);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Context menu for source view links.
        /// </summary>
        private void SourceEditor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_viewMode != EditorViewMode.Source)
                return;

            var offset = SourceEditor.CaretOffset;
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Check for spelling suggestions first
            if (_spellCheckService != null && _spellCheckMarkerService != null && App.Settings.EnableSpellCheck)
            {
                var misspelledWord = _spellCheckMarkerService.GetWordAtOffset(offset);
                if (!string.IsNullOrEmpty(misspelledWord))
                {
                    var suggestions = _spellCheckService.GetSuggestions(misspelledWord, 5);

                    if (suggestions.Any())
                    {
                        foreach (var suggestion in suggestions)
                        {
                            var item = new System.Windows.Controls.MenuItem
                            {
                                Header = suggestion,
                                FontWeight = FontWeights.Bold
                            };
                            var marker = _spellCheckMarkerService.GetMarkerAtOffset(offset);
                            item.Click += (s, args) =>
                            {
                                if (marker != null)
                                {
                                    SourceEditor.Document.Replace(marker.StartOffset, marker.Length, suggestion);
                                    RunSpellCheck();
                                }
                            };
                            contextMenu.Items.Add(item);
                        }
                    }
                    else
                    {
                        var noSuggestions = new System.Windows.Controls.MenuItem
                        {
                            Header = "(no suggestions)",
                            IsEnabled = false
                        };
                        contextMenu.Items.Add(noSuggestions);
                    }

                    contextMenu.Items.Add(new System.Windows.Controls.Separator());

                    // Add to dictionary
                    var addToDictItem = new System.Windows.Controls.MenuItem { Header = "Add to Dictionary" };
                    addToDictItem.Click += (s, args) =>
                    {
                        _spellCheckService.AddToUserDictionary(misspelledWord);
                        RunSpellCheck();
                    };
                    contextMenu.Items.Add(addToDictItem);

                    contextMenu.Items.Add(new System.Windows.Controls.Separator());
                }
            }

            // Check for link
            if (_linkDetector != null)
            {
                var link = _linkDetector.DetectedLinks.FirstOrDefault(l =>
                    offset >= l.StartOffset && offset <= l.EndOffset);

                if (link != null)
                {
                    var openItem = new System.Windows.Controls.MenuItem { Header = "Open Link" };
                    openItem.Click += (s, args) => OpenLink(link.Url);
                    contextMenu.Items.Add(openItem);

                    var copyItem = new System.Windows.Controls.MenuItem { Header = "Copy Link Address" };
                    copyItem.Click += (s, args) => Clipboard.SetText(link.Url);
                    contextMenu.Items.Add(copyItem);
                }
            }

            if (contextMenu.Items.Count > 0)
            {
                SourceEditor.ContextMenu = contextMenu;
            }
            else
            {
                SourceEditor.ContextMenu = null;
            }
        }

        /// <summary>
        /// Context menu for formatted view links.
        /// </summary>
        private void FormattedEditor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_viewMode != EditorViewMode.Formatted)
                return;

            // Check if caret is on a hyperlink
            var caretPos = FormattedEditor.CaretPosition;
            var hyperlink = GetHyperlinkAtPosition(caretPos);

            if (hyperlink != null && hyperlink.NavigateUri != null)
            {
                var contextMenu = new System.Windows.Controls.ContextMenu();

                var openItem = new System.Windows.Controls.MenuItem { Header = "Open Link" };
                openItem.Click += (s, args) => OpenLink(hyperlink.NavigateUri.ToString());
                contextMenu.Items.Add(openItem);

                var copyItem = new System.Windows.Controls.MenuItem { Header = "Copy Link Address" };
                copyItem.Click += (s, args) => Clipboard.SetText(hyperlink.NavigateUri.ToString());
                contextMenu.Items.Add(copyItem);

                FormattedEditor.ContextMenu = contextMenu;
            }
            else
            {
                FormattedEditor.ContextMenu = null;
            }
        }

        private System.Windows.Documents.Hyperlink? GetHyperlinkAtPosition(TextPointer position)
        {
            var parent = position.Parent;
            while (parent != null)
            {
                if (parent is System.Windows.Documents.Hyperlink hyperlink)
                    return hyperlink;
                
                if (parent is Inline inline)
                    parent = inline.Parent;
                else
                    break;
            }
            return null;
        }

        private void OpenLink(string url)
        {
            _linkNavigationService?.TryNavigate(url, Window.GetWindow(this));
        }

        #endregion
    }
}
