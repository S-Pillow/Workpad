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
    /// Call <see cref="Cleanup"/> when the hosting tab is closed to stop timers
    /// and unsubscribe events, preventing CPU and memory leaks.
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
        private string? _lastFormattedMarkdown; // Track original markdown before bionic is applied
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
            SourceEditor.PreviewMouseRightButtonDown += SourceEditor_PreviewMouseRightButtonDown;
            FormattedEditor.PreviewMouseRightButtonDown += FormattedEditor_PreviewMouseRightButtonDown;
            
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

            // Update formatted editor read-only state based on bionic
            UpdateFormattedEditorState();

            // Custom copy handling
            SourceEditor.PreviewKeyDown += Editor_PreviewKeyDown;
            FormattedEditor.PreviewKeyDown += Editor_PreviewKeyDown;

            // Apply theme colors on load
            ApplySelectionColors();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplySelectionColors();
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
                // CRITICAL: Always use SourceEditor.Text as canonical source, not FormattedEditor.Document
                // FormattedEditor.Document may contain bionic-modified content with bolded prefixes
                var markdownText = SourceEditor.Text;
                _lastFormattedMarkdown = markdownText; // Store original before bionic
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

            // Rebuild SpellCheckMarkerService for the new document.
            // The old service holds a reference to the previous TextDocument,
            // so GetWordAtOffset / AddMarker would read stale data.
            RebuildSpellCheckMarkerService();

            // Load into formatted editor
            if (_markdownParser != null)
            {
                _lastFormattedMarkdown = _document.Content; // Store original before bionic
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

            // Update empty state placeholder
            UpdateEmptyStatePlaceholder();

            // Run spell check on newly loaded content so suggestions are
            // available immediately (not only after the user types).
            if (App.Settings.EnableSpellCheck)
            {
                _spellCheckTimer?.Stop();
                _spellCheckTimer?.Start();
            }
        }

        /// <summary>
        /// Rebuilds the SpellCheckMarkerService and renderer after the SourceEditor.Document
        /// has been replaced (e.g. on file open). This ensures the marker service tracks
        /// offsets against the current document, not a stale one.
        /// </summary>
        private void RebuildSpellCheckMarkerService()
        {
            if (_spellCheckService == null)
                return;

            try
            {
                // Remove old renderer if present
                if (_spellCheckRenderer != null)
                {
                    SourceEditor.TextArea.TextView.BackgroundRenderers.Remove(_spellCheckRenderer);
                }

                // Create new service and renderer pointing at the current document
                _spellCheckMarkerService = new SpellCheckMarkerService(SourceEditor.Document);
                _spellCheckRenderer = new SpellCheckBackgroundRenderer(_spellCheckMarkerService);
                SourceEditor.TextArea.TextView.BackgroundRenderers.Add(_spellCheckRenderer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RebuildSpellCheckMarkerService error: {ex.Message}");
            }
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
                    _lastFormattedMarkdown = markdownText; // Store original before bionic
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

                // Update read-only state
                UpdateFormattedEditorState();

                SourceEditor.Visibility = Visibility.Collapsed;
                FormattedEditor.Visibility = Visibility.Visible;
                FormattedEditor.Focus();
            }
            else
            {
                // Formatted is now editable in Source mode
                FormattedEditor.IsReadOnly = false;
                
                // Source is always canonical, no sync needed when switching away from formatted
                // (SourceEditor.Text is already up to date)

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
                // CRITICAL: When bionic is enabled, FormattedEditor contains bionic-modified document
                // We must NEVER serialize from it - always use SourceEditor as canonical source
                if (_viewMode == EditorViewMode.Formatted && _markdownSerializer != null)
                {
                    // Sync formatted → source first (but only if bionic is OFF)
                    if (!App.Settings.EnableBionicReading)
                    {
                        var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
                        _isSyncing = true;
                        SourceEditor.Text = markdownText;
                        _isSyncing = false;
                    }
                    
                    // Always save from SourceEditor (canonical)
                    _document.Save(SourceEditor.Text);
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
            // Always return from SourceEditor (canonical source of truth)
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

            // Update empty state placeholder visibility
            UpdateEmptyStatePlaceholder();

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

        /// <summary>
        /// Shows or hides the empty state placeholder based on editor content.
        /// </summary>
        private void UpdateEmptyStatePlaceholder()
        {
            bool isEmpty = false;
            
            if (_viewMode == EditorViewMode.Source)
            {
                isEmpty = string.IsNullOrWhiteSpace(SourceEditor.Text);
            }
            else
            {
                var textRange = new TextRange(FormattedEditor.Document.ContentStart, FormattedEditor.Document.ContentEnd);
                isEmpty = string.IsNullOrWhiteSpace(textRange.Text);
            }
            
            EmptyStatePlaceholder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
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
            // Update formatted editor state
            UpdateFormattedEditorState();
            
            // For Source view, just redraw
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.TextArea.TextView.Redraw();
            }
            // For Formatted view, re-parse and apply bionic
            else if (_viewMode == EditorViewMode.Formatted && _markdownParser != null)
            {
                // Use SourceEditor.Text as canonical source
                var markdownText = SourceEditor.Text;
                _lastFormattedMarkdown = markdownText; // Store original before bionic
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

        private void UpdateFormattedEditorState()
        {
            // When bionic is enabled in formatted view, make it read-only to prevent corruption
            if (_viewMode == EditorViewMode.Formatted)
            {
                FormattedEditor.IsReadOnly = App.Settings.EnableBionicReading;
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
                // Save caret and selection offsets so we can restore after document replacement
                var caretOffset = FormattedEditor.Document.ContentStart.GetOffsetToPosition(FormattedEditor.CaretPosition);
                var selStartOffset = FormattedEditor.Document.ContentStart.GetOffsetToPosition(FormattedEditor.Selection.Start);
                var selEndOffset = FormattedEditor.Document.ContentStart.GetOffsetToPosition(FormattedEditor.Selection.End);
                bool hadSelection = !FormattedEditor.Selection.IsEmpty;

                // Serialize current document to markdown
                var currentMarkdown = _markdownSerializer?.SerializeToMarkdown(FormattedEditor.Document) ?? string.Empty;
                _lastFormattedMarkdown = currentMarkdown; // Store original before bionic

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

                // Restore caret and selection (approximate, offset-based)
                try
                {
                    if (hadSelection)
                    {
                        var newStart = FormattedEditor.Document.ContentStart.GetPositionAtOffset(selStartOffset);
                        var newEnd = FormattedEditor.Document.ContentStart.GetPositionAtOffset(selEndOffset);
                        if (newStart != null && newEnd != null)
                        {
                            FormattedEditor.Selection.Select(newStart, newEnd);
                        }
                    }
                    else
                    {
                        var newPosition = FormattedEditor.Document.ContentStart.GetPositionAtOffset(caretOffset);
                        if (newPosition != null)
                        {
                            FormattedEditor.CaretPosition = newPosition;
                        }
                    }
                }
                catch
                {
                    // If restoration fails, just leave caret where it is
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

            // If bionic is enabled, formatted view is read-only, so this shouldn't fire for user edits
            // Only fire for programmatic changes (which are _isSyncing = true)
            
            if (!_document.IsDirty)
            {
                _document.IsDirty = true;
            }

            // Throttle link detection in formatted view
            // BUT: Skip link detection if Bionic Reading is enabled to avoid document churning
            if (_viewMode == EditorViewMode.Formatted && !App.Settings.EnableBionicReading)
            {
                // Sync formatted -> source (only when bionic is OFF)
                if (_markdownSerializer != null)
                {
                    var markdownText = _markdownSerializer.SerializeToMarkdown(FormattedEditor.Document);
                    _isSyncing = true;
                    SourceEditor.Text = markdownText;
                    _isSyncing = false;
                }
                
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
        /// Only intercepts when there is an active selection so that the default
        /// behavior (e.g. AvalonEdit "copy current line") is preserved when nothing is selected.
        /// </summary>
        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                bool hasSelection;
                if (_viewMode == EditorViewMode.Source)
                {
                    hasSelection = !SourceEditor.TextArea.Selection.IsEmpty;
                }
                else
                {
                    hasSelection = !FormattedEditor.Selection.IsEmpty;
                }

                if (hasSelection)
                {
                    HandleCopy();
                    e.Handled = true;
                }
                // When no selection, fall through to default editor behavior
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

            // Save selection boundaries so we can restore after property change
            var selStart = selection.Start;
            var selEnd = selection.End;

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

            // Restore the selection (ApplyPropertyValue can collapse or shift it)
            FormattedEditor.Selection.Select(selStart, selEnd);
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
                    Foreground = TryFindResource("App.Accent") as SolidColorBrush ?? Brushes.Blue,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = Cursors.Hand
                };
                // Use TryCreate to avoid crash on malformed URLs
                if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var insertLinkUri))
                {
                    hyperlink.NavigateUri = insertLinkUri;
                }
                hyperlink.Click += (s, e) =>
                {
                    e.Handled = true;
                    HandleLinkClick(url);
                };

                var selection = FormattedEditor.Selection;
                if (!selection.IsEmpty)
                {
                    // Capture the paragraph and insertion point BEFORE deleting text.
                    // TextPointers become unreliable after document modifications.
                    var insertPos = selection.Start;
                    var paragraph = insertPos.Paragraph;
                    
                    // Delete exactly the selected text (not the whole Run).
                    // The old code used DeleteTextInRun which deleted from
                    // the start position to the end of the Run, corrupting text
                    // when the selection was only part of a Run.
                    var selRange = new TextRange(selection.Start, selection.End);
                    selRange.Text = "";

                    // Insert hyperlink at the exact insertion point (not at end of paragraph)
                    // Use the captured paragraph reference for the insert operation
                    if (paragraph != null)
                    {
                        // Get the Run at insertPos to insert before/after it
                        var insertRun = insertPos.Parent as Run;
                        if (insertRun != null)
                        {
                            paragraph.Inlines.InsertBefore(insertRun, hyperlink);
                        }
                        else
                        {
                            // Fallback: add to end if we can't find the exact position
                            paragraph.Inlines.Add(hyperlink);
                        }
                        FormattedEditor.CaretPosition = hyperlink.ContentEnd;
                    }
                }
                else
                {
                    // Insert at caret position
                    var caretPos = FormattedEditor.CaretPosition;
                    
                    // Try to insert at the exact caret position
                    var run = caretPos.Parent as Run;
                    if (run != null && run.Parent is Paragraph para)
                    {
                        // We're inside a run - split it if needed
                        var runText = run.Text;
                        var textBeforeCaret = caretPos.GetTextInRun(LogicalDirection.Backward);
                        var textAfterCaret = caretPos.GetTextInRun(LogicalDirection.Forward);
                        
                        if (!string.IsNullOrEmpty(textBeforeCaret) && !string.IsNullOrEmpty(textAfterCaret))
                        {
                            // Split the run
                            run.Text = textBeforeCaret;
                            var afterRun = new Run(textAfterCaret);
                            
                            var inlineCollection = para.Inlines;
                            inlineCollection.InsertAfter(run, hyperlink);
                            inlineCollection.InsertAfter(hyperlink, afterRun);
                        }
                        else if (!string.IsNullOrEmpty(textBeforeCaret))
                        {
                            // At end of run
                            para.Inlines.InsertAfter(run, hyperlink);
                        }
                        else
                        {
                            // At start of run
                            para.Inlines.InsertBefore(run, hyperlink);
                        }
                        
                        FormattedEditor.CaretPosition = hyperlink.ContentEnd;
                    }
                    else if (caretPos.Paragraph != null)
                    {
                        // At paragraph level - try to insert at current position
                        caretPos.Paragraph.Inlines.Add(hyperlink);
                        FormattedEditor.CaretPosition = hyperlink.ContentEnd;
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

        private void SourceEditor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewMode != EditorViewMode.Source)
                return;

            var position = SourceEditor.GetPositionFromPoint(e.GetPosition(SourceEditor));
            if (position != null)
            {
                var offset = SourceEditor.Document.GetOffset(position.Value.Location);
                var selection = SourceEditor.TextArea.Selection;

                // Preserve selection when right-clicking inside it
                bool insideSelection = !selection.IsEmpty &&
                    selection.Segments.Any(s => offset >= s.StartOffset && offset < s.EndOffset);

                if (!insideSelection)
                {
                    SourceEditor.TextArea.Caret.Offset = offset;
                }
            }
        }

        private void FormattedEditor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewMode != EditorViewMode.Formatted)
                return;

            var clickedPosition = FormattedEditor.GetPositionFromPoint(e.GetPosition(FormattedEditor), true);
            if (clickedPosition != null)
            {
                var selection = FormattedEditor.Selection;

                // Preserve selection when right-clicking inside it
                bool insideSelection = !selection.IsEmpty &&
                    clickedPosition.CompareTo(selection.Start) >= 0 &&
                    clickedPosition.CompareTo(selection.End) < 0;

                if (!insideSelection)
                {
                    FormattedEditor.CaretPosition = clickedPosition;
                }
            }
        }

        /// <summary>
        /// Context menu for source view links.
        /// </summary>
        private void SourceEditor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_viewMode != EditorViewMode.Source)
                return;

            // Cancel the default menu and open our dynamic one programmatically.
            // This avoids the WPF issue where replacing ContextMenu mid-event
            // leaves a stale menu on TextArea and breaks command routing.
            e.Handled = true;

            var offset = SourceEditor.CaretOffset;
            var contextMenu = CreateStandardContextMenu();
            var dynamicItems = new List<object>();

            // Spelling suggestions (source view).
            if (_spellCheckService != null && _spellCheckMarkerService != null && App.Settings.EnableSpellCheck)
            {
                var misspelledWord = _spellCheckMarkerService.GetWordAtOffset(offset);
                if (!string.IsNullOrEmpty(misspelledWord))
                {
                    var suggestions = _spellCheckService.GetSuggestions(misspelledWord, 5);
                    var marker = _spellCheckMarkerService.GetMarkerAtOffset(offset);

                    if (suggestions.Any())
                    {
                        foreach (var suggestion in suggestions)
                        {
                            var capturedSuggestion = suggestion; // capture for closure
                            var suggestionItem = new MenuItem
                            {
                                Header = suggestion,
                                FontWeight = FontWeights.Bold
                            };
                            suggestionItem.Click += (s, args) =>
                            {
                                if (marker != null)
                                {
                                    SourceEditor.Document.Replace(marker.StartOffset, marker.Length, capturedSuggestion);
                                    RunSpellCheck();
                                }
                            };
                            dynamicItems.Add(suggestionItem);
                        }
                    }
                    else
                    {
                        dynamicItems.Add(new MenuItem
                        {
                            Header = "(no suggestions)",
                            IsEnabled = false
                        });
                    }

                    var addToDictItem = new MenuItem { Header = "Add to Dictionary" };
                    addToDictItem.Click += (s, args) =>
                    {
                        _spellCheckService.AddToUserDictionary(misspelledWord);
                        RunSpellCheck();
                    };
                    dynamicItems.Add(addToDictItem);
                }
            }

            // Link actions (source view).
            if (_linkDetector != null)
            {
                var link = _linkDetector.DetectedLinks.FirstOrDefault(l =>
                    offset >= l.StartOffset && offset <= l.EndOffset);

                if (link != null)
                {
                    var openItem = new MenuItem { Header = "Open Link" };
                    openItem.Click += (s, args) => OpenLink(link.Url);
                    dynamicItems.Add(openItem);

                    var copyItem = new MenuItem { Header = "Copy Link Address" };
                    copyItem.Click += (s, args) => Clipboard.SetText(link.Url);
                    dynamicItems.Add(copyItem);
                }
            }

            PrependDynamicMenuItems(contextMenu, dynamicItems);
            contextMenu.PlacementTarget = SourceEditor;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// Context menu for formatted view links.
        /// </summary>
        private void FormattedEditor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_viewMode != EditorViewMode.Formatted)
                return;

            // Cancel the default menu and open our dynamic one programmatically.
            e.Handled = true;

            var contextMenu = CreateStandardContextMenu();
            var dynamicItems = new List<object>();

            // Spelling suggestions (formatted view).
            var misspelledWord = GetFormattedMisspelledWordAtCaret();
            if (misspelledWord != null && _spellCheckService != null && App.Settings.EnableSpellCheck)
            {
                var suggestions = _spellCheckService.GetSuggestions(misspelledWord.Word, 5);
                if (suggestions.Any())
                {
                    foreach (var suggestion in suggestions)
                    {
                        var capturedWord = misspelledWord; // capture for closure
                        var capturedSuggestion = suggestion; // capture for closure
                        var suggestionItem = new MenuItem
                        {
                            Header = suggestion,
                            FontWeight = FontWeights.Bold
                        };
                        suggestionItem.Click += (s, args) =>
                        {
                            ReplaceFormattedWord(capturedWord, capturedSuggestion);
                            RunSpellCheck();
                        };
                        dynamicItems.Add(suggestionItem);
                    }
                }
                else
                {
                    dynamicItems.Add(new MenuItem
                    {
                        Header = "(no suggestions)",
                        IsEnabled = false
                    });
                }

                var addToDictItem = new MenuItem { Header = "Add to Dictionary" };
                var wordToAdd = misspelledWord.Word; // capture for closure
                addToDictItem.Click += (s, args) =>
                {
                    _spellCheckService.AddToUserDictionary(wordToAdd);
                    RunSpellCheck();
                };
                dynamicItems.Add(addToDictItem);
            }

            // Link actions (formatted view).
            var caretPos = FormattedEditor.CaretPosition;
            var hyperlink = GetHyperlinkAtPosition(caretPos);
            if (hyperlink != null && hyperlink.NavigateUri != null)
            {
                var openItem = new MenuItem { Header = "Open Link" };
                openItem.Click += (s, args) => OpenLink(hyperlink.NavigateUri.ToString());
                dynamicItems.Add(openItem);

                var copyItem = new MenuItem { Header = "Copy Link Address" };
                copyItem.Click += (s, args) => Clipboard.SetText(hyperlink.NavigateUri.ToString());
                dynamicItems.Add(copyItem);
            }

            PrependDynamicMenuItems(contextMenu, dynamicItems);
            contextMenu.PlacementTarget = FormattedEditor;
            contextMenu.IsOpen = true;
        }

        private ContextMenu CreateStandardContextMenu()
        {
            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(CreateContextMenuItem("Undo", ContextUndo_Click, "Ctrl+Z"));
            contextMenu.Items.Add(CreateContextMenuItem("Redo", ContextRedo_Click, "Ctrl+Y"));
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateContextMenuItem("Cut", ContextCut_Click, "Ctrl+X"));
            contextMenu.Items.Add(CreateContextMenuItem("Copy", ContextCopy_Click, "Ctrl+C"));
            contextMenu.Items.Add(CreateContextMenuItem("Paste", ContextPaste_Click, "Ctrl+V"));
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateContextMenuItem("Select All", ContextSelectAll_Click, "Ctrl+A"));
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateContextMenuItem("Bold", ContextBold_Click, "Ctrl+B"));
            contextMenu.Items.Add(CreateContextMenuItem("Italic", ContextItalic_Click, "Ctrl+I"));
            contextMenu.Items.Add(CreateContextMenuItem("Insert Link", ContextInsertLink_Click, "Ctrl+K"));
            return contextMenu;
        }

        private MenuItem CreateContextMenuItem(string header, RoutedEventHandler clickHandler, string? gestureText = null)
        {
            var item = new MenuItem
            {
                Header = header
            };

            if (!string.IsNullOrWhiteSpace(gestureText))
            {
                item.InputGestureText = gestureText;
            }

            item.Click += clickHandler;
            return item;
        }

        private void PrependDynamicMenuItems(ContextMenu contextMenu, List<object> dynamicItems)
        {
            if (dynamicItems.Count == 0)
                return;

            var insertIndex = 0;
            foreach (var menuItem in dynamicItems)
            {
                contextMenu.Items.Insert(insertIndex++, menuItem);
            }

            contextMenu.Items.Insert(insertIndex, new Separator());
        }

        /// <summary>
        /// Holds a misspelled word's text and its TextPointer boundaries in the FlowDocument.
        /// Using TextPointers avoids the structural-vs-character offset mismatch that breaks
        /// GetOffsetToPosition / GetPositionAtOffset for complex documents.
        /// </summary>
        private class FormattedMisspelledWord
        {
            public string Word { get; set; } = "";
            public TextPointer Start { get; set; } = null!;
            public TextPointer End { get; set; } = null!;
        }

        /// <summary>
        /// Finds the misspelled word at the caret using TextPointer-based word boundary detection.
        /// This replaces the old offset-based approach which used GetOffsetToPosition (structural
        /// symbol count) instead of text character count, causing mismatches in complex documents.
        /// </summary>
        private FormattedMisspelledWord? GetFormattedMisspelledWordAtCaret()
        {
            if (_spellCheckService == null || !App.Settings.EnableSpellCheck)
                return null;

            try
            {
                var caretPos = FormattedEditor.CaretPosition;
                if (caretPos == null)
                    return null;

                // Walk backward from caret to find the start of the word
                var wordStart = caretPos;
                while (wordStart != null && wordStart.CompareTo(FormattedEditor.Document.ContentStart) > 0)
                {
                    var prev = wordStart.GetNextInsertionPosition(LogicalDirection.Backward);
                    if (prev == null)
                        break;

                    var charRange = new TextRange(prev, wordStart);
                    var ch = charRange.Text;
                    if (string.IsNullOrEmpty(ch) || ch.Length != 1 || !char.IsLetter(ch[0]))
                        break;

                    wordStart = prev;
                }

                // Walk forward from caret to find the end of the word
                var wordEnd = caretPos;
                while (wordEnd != null && wordEnd.CompareTo(FormattedEditor.Document.ContentEnd) < 0)
                {
                    var next = wordEnd.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null)
                        break;

                    var charRange = new TextRange(wordEnd, next);
                    var ch = charRange.Text;
                    if (string.IsNullOrEmpty(ch) || ch.Length != 1 || !char.IsLetter(ch[0]))
                        break;

                    wordEnd = next;
                }

                if (wordStart == null || wordEnd == null || wordStart.CompareTo(wordEnd) >= 0)
                    return null;

                var word = new TextRange(wordStart, wordEnd).Text.Trim();
                if (string.IsNullOrEmpty(word) || word.Length < 2)
                    return null;

                // Check if the word is misspelled
                if (_spellCheckService.IsCorrect(word))
                    return null;

                return new FormattedMisspelledWord
                {
                    Word = word,
                    Start = wordStart,
                    End = wordEnd
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Replaces a misspelled word in the formatted view using its stored TextPointers.
        /// </summary>
        private void ReplaceFormattedWord(FormattedMisspelledWord wordInfo, string replacement)
        {
            try
            {
                if (wordInfo.Start == null || wordInfo.End == null)
                    return;

                new TextRange(wordInfo.Start, wordInfo.End).Text = replacement;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReplaceFormattedWord error: {ex.Message}");
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

        #region Split View Support

        /// <summary>
        /// Sets the editor to use a shared TextDocument for split view Source mode.
        /// INDUSTRY BEST PRACTICE: Shared buffer gives perfect sync with shared undo stack.
        /// </summary>
        public void SetSharedSourceDocument(TextDocument sharedDocument, Document document, EditorViewMode viewMode)
        {
            _document = document;
            _viewMode = viewMode;
            _isLoading = true;

            try
            {
                // Replace SourceEditor's document with the shared one
                SourceEditor.Document = sharedDocument;

                // Rebuild spell check marker service for the new document
                RebuildSpellCheckMarkerService();

                // Switch to source view
                SourceEditor.Visibility = Visibility.Visible;
                FormattedEditor.Visibility = Visibility.Collapsed;

                // Apply link detection
                ApplyLinkDetection();

                // Apply font settings
                ApplyFontSettings();
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Refreshes the editor from the document content (for formatted mode mirror pane).
        /// </summary>
        public void RefreshFromDocument()
        {
            if (_document == null || _markdownParser == null || _viewMode != EditorViewMode.Formatted)
                return;

            _isLoading = true;
            try
            {
                var flowDoc = _markdownParser.ParseToFlowDocument(_document.Content);
                
                // Apply bionic reading if enabled
                if (App.Settings.EnableBionicReading)
                {
                    BionicReadingProcessor.ApplyBionicReading(flowDoc, App.Settings.BionicStrength);
                }
                
                FormattedEditor.Document = flowDoc;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Gets the FormattedEditor RichTextBox for split view configuration.
        /// </summary>
        public RichTextBox GetFormattedEditorControl() => FormattedEditor;

        #endregion

        #region Context Menu Handlers

        private void ContextUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.Undo();
            }
            else
            {
                FormattedEditor.Undo();
            }
        }

        private void ContextRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.Redo();
            }
            else
            {
                FormattedEditor.Redo();
            }
        }

        private void ContextCut_Click(object sender, RoutedEventArgs e)
        {
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.Cut();
            }
            else
            {
                FormattedEditor.Cut();
            }
        }

        private void ContextCopy_Click(object sender, RoutedEventArgs e)
        {
            // Use the same custom copy logic as Ctrl+C so clipboard
            // content is consistent regardless of invocation method.
            HandleCopy();
        }

        private void ContextPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.Paste();
            }
            else
            {
                FormattedEditor.Paste();
            }
        }

        private void ContextSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_viewMode == EditorViewMode.Source)
            {
                SourceEditor.SelectAll();
            }
            else
            {
                FormattedEditor.SelectAll();
            }
        }

        private void ContextBold_Click(object sender, RoutedEventArgs e)
        {
            ApplyBold();
        }

        private void ContextItalic_Click(object sender, RoutedEventArgs e)
        {
            ApplyItalic();
        }

        private void ContextInsertLink_Click(object sender, RoutedEventArgs e)
        {
            // Show Insert Link dialog
            var selectedText = GetSelectedText();
            var dialog = new Dialogs.InsertLinkDialog(selectedText)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                InsertLink(dialog.LinkUrl, dialog.LinkLabel);
            }
        }

        #endregion

        #region Lifecycle / Cleanup

        /// <summary>
        /// Stops all timers and unsubscribes event handlers so this control can be
        /// garbage-collected after its hosting tab is closed. Without this, three
        /// DispatcherTimers and multiple event subscriptions keep the control alive.
        /// </summary>
        public void Cleanup()
        {
            System.Diagnostics.Debug.WriteLine("[EditorControl] Cleanup — stopping timers, unsubscribing events");

            // Stop all throttle timers
            _linkDetectionTimer?.Stop();
            _formattedLinkDetectionTimer?.Stop();
            _spellCheckTimer?.Stop();

            // Unsubscribe editor events to break reference chains
            SourceEditor.TextChanged -= SourceEditor_TextChanged;
            FormattedEditor.TextChanged -= FormattedEditor_TextChanged;
            SourceEditor.PreviewMouseLeftButtonDown -= SourceEditor_PreviewMouseLeftButtonDown;
            SourceEditor.PreviewMouseRightButtonDown -= SourceEditor_PreviewMouseRightButtonDown;
            FormattedEditor.PreviewMouseRightButtonDown -= FormattedEditor_PreviewMouseRightButtonDown;
            SourceEditor.ContextMenuOpening -= SourceEditor_ContextMenuOpening;
            FormattedEditor.ContextMenuOpening -= FormattedEditor_ContextMenuOpening;
            SourceEditor.PreviewKeyDown -= Editor_PreviewKeyDown;
            FormattedEditor.PreviewKeyDown -= Editor_PreviewKeyDown;
            this.Loaded -= OnLoaded;
        }

        #endregion
    }
}
