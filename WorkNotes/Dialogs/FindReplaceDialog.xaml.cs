using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Controls;
using WorkNotes.Services;
using WorkNotes.Controls;
using WorkNotes.Models;
using ICSharpCode.AvalonEdit.Document;

namespace WorkNotes.Dialogs
{
    /// <summary>
    /// Find and Replace dialog with professional search options.
    /// </summary>
    public partial class FindReplaceDialog : Window
    {
        private readonly EditorControl _editorControl;
        private readonly FindReplaceService _findReplaceService;
        private int _lastSearchOffset = 0;
        private TextPointer? _lastFormattedSearchPosition = null;

        public FindReplaceDialog(EditorControl editorControl)
        {
            InitializeComponent();
            _editorControl = editorControl;
            _findReplaceService = new FindReplaceService();

            // Set initial focus to find textbox
            FindTextBox.Focus();
            
            // Handle Enter key in find textbox
            FindTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    FindNext_Click(s, e);
                    e.Handled = true;
                }
            };

            // Handle Enter key in replace textbox
            ReplaceTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Replace_Click(s, e);
                    e.Handled = true;
                }
            };
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            var searchTerm = FindTextBox.Text;
            if (string.IsNullOrEmpty(searchTerm))
                return;

            if (_editorControl.ViewMode == EditorViewMode.Source)
            {
                // Source view - use string-based search
                var text = _editorControl.Editor.Text;
                var currentOffset = _editorControl.Editor.CaretOffset;

                var result = _findReplaceService.FindNext(
                    text,
                    searchTerm,
                    currentOffset,
                    MatchCaseCheckBox.IsChecked == true,
                    WholeWordCheckBox.IsChecked == true,
                    WrapAroundCheckBox.IsChecked == true);

                if (result != null)
                {
                    _editorControl.Editor.Select(result.StartOffset, result.Length);
                    _editorControl.Editor.ScrollToLine(_editorControl.Editor.Document.GetLineByOffset(result.StartOffset).LineNumber);
                    _editorControl.Editor.Focus();
                    _lastSearchOffset = result.StartOffset + result.Length;
                }
                else
                {
                    MessageBox.Show($"Cannot find \"{searchTerm}\"", "Find", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // Formatted view - use RichTextBox TextRange search
                var rtb = _editorControl.FindName("FormattedEditor") as RichTextBox;
                if (rtb != null)
                {
                    var startPos = _lastFormattedSearchPosition ?? rtb.CaretPosition;
                    var isFirstSearch = _lastFormattedSearchPosition == null;
                    
                    var match = FindTextInRichTextBox(rtb, searchTerm, startPos, 
                        MatchCaseCheckBox.IsChecked == true, 
                        WholeWordCheckBox.IsChecked == true);

                    // Try wrap-around if not found
                    if (match == null && WrapAroundCheckBox.IsChecked == true)
                    {
                        // Only wrap if we've already found something, or if first search didn't start from beginning
                        if (!isFirstSearch || startPos.CompareTo(rtb.Document.ContentStart) > 0)
                        {
                            match = FindTextInRichTextBox(rtb, searchTerm, rtb.Document.ContentStart,
                                MatchCaseCheckBox.IsChecked == true,
                                WholeWordCheckBox.IsChecked == true);
                        }
                    }

                    if (match != null)
                    {
                        rtb.Selection.Select(match.Start, match.End);
                        rtb.Focus();
                        _lastFormattedSearchPosition = match.End;

                        // Scroll to selection
                        var rect = match.Start.GetCharacterRect(LogicalDirection.Forward);
                        rtb.ScrollToVerticalOffset(Math.Max(0, rect.Top - 50));
                    }
                    else
                    {
                        MessageBox.Show($"Cannot find \"{searchTerm}\"", "Find", MessageBoxButton.OK, MessageBoxImage.Information);
                        _lastFormattedSearchPosition = null;
                    }
                }
            }
        }

        private void FindPrevious_Click(object sender, RoutedEventArgs e)
        {
            var searchTerm = FindTextBox.Text;
            if (string.IsNullOrEmpty(searchTerm))
                return;

            if (_editorControl.ViewMode == EditorViewMode.Source)
            {
                // Source view - use string-based search
                var text = _editorControl.Editor.Text;
                var currentOffset = _editorControl.Editor.CaretOffset;

                var result = _findReplaceService.FindPrevious(
                    text,
                    searchTerm,
                    currentOffset,
                    MatchCaseCheckBox.IsChecked == true,
                    WholeWordCheckBox.IsChecked == true,
                    WrapAroundCheckBox.IsChecked == true);

                if (result != null)
                {
                    _editorControl.Editor.Select(result.StartOffset, result.Length);
                    _editorControl.Editor.ScrollToLine(_editorControl.Editor.Document.GetLineByOffset(result.StartOffset).LineNumber);
                    _editorControl.Editor.Focus();
                    _lastSearchOffset = result.StartOffset;
                }
                else
                {
                    MessageBox.Show($"Cannot find \"{searchTerm}\"", "Find", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // Formatted view - use RichTextBox TextRange search backward
                var rtb = _editorControl.FindName("FormattedEditor") as RichTextBox;
                if (rtb != null)
                {
                    var endPos = _lastFormattedSearchPosition ?? rtb.CaretPosition;
                    var match = FindTextInRichTextBoxBackward(rtb, searchTerm, endPos,
                        MatchCaseCheckBox.IsChecked == true,
                        WholeWordCheckBox.IsChecked == true);

                    // Try wrap-around if not found
                    if (match == null && WrapAroundCheckBox.IsChecked == true && _lastFormattedSearchPosition != null)
                    {
                        match = FindTextInRichTextBoxBackward(rtb, searchTerm, rtb.Document.ContentEnd,
                            MatchCaseCheckBox.IsChecked == true,
                            WholeWordCheckBox.IsChecked == true);
                    }

                    if (match != null)
                    {
                        rtb.Selection.Select(match.Start, match.End);
                        rtb.Focus();
                        _lastFormattedSearchPosition = match.Start;

                        // Scroll to selection
                        var rect = match.Start.GetCharacterRect(LogicalDirection.Forward);
                        rtb.ScrollToVerticalOffset(Math.Max(0, rect.Top - 50));
                    }
                    else
                    {
                        MessageBox.Show($"Cannot find \"{searchTerm}\"", "Find", MessageBoxButton.OK, MessageBoxImage.Information);
                        _lastFormattedSearchPosition = null;
                    }
                }
            }
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            var searchTerm = FindTextBox.Text;
            var replaceTerm = ReplaceTextBox.Text;

            if (string.IsNullOrEmpty(searchTerm))
                return;

            if (_editorControl.ViewMode == EditorViewMode.Source)
            {
                // Source view logic
                var currentSelection = _editorControl.Editor.SelectedText;
                var matchCase = MatchCaseCheckBox.IsChecked == true;
                var comparisonType = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                if (!string.IsNullOrEmpty(currentSelection) && currentSelection.Equals(searchTerm, comparisonType))
                {
                    // Replace current selection
                    var selection = _editorControl.Editor.TextArea.Selection;
                    if (!selection.IsEmpty)
                    {
                        var segment = selection.Segments.FirstOrDefault();
                        if (segment != null)
                        {
                            _editorControl.Editor.Document.Replace(segment.StartOffset, segment.Length, replaceTerm);
                        }
                    }
                }
                
                // Find next after replace
                FindNext_Click(sender, e);
            }
            else
            {
                // Formatted view - check if current selection matches
                var rtb = _editorControl.FindName("FormattedEditor") as RichTextBox;
                if (rtb != null && !rtb.Selection.IsEmpty)
                {
                    var currentSelection = rtb.Selection.Text;
                    var matchCase = MatchCaseCheckBox.IsChecked == true;
                    var comparisonType = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                    if (currentSelection.Equals(searchTerm, comparisonType))
                    {
                        // Replace the selection
                        rtb.Selection.Text = replaceTerm;
                        if (_editorControl.Document != null)
                        {
                            _editorControl.Document.IsDirty = true;
                        }
                    }
                }
                
                // Find next
                FindNext_Click(sender, e);
            }
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            var searchTerm = FindTextBox.Text;
            var replaceTerm = ReplaceTextBox.Text ?? string.Empty;

            if (string.IsNullOrEmpty(searchTerm))
                return;

            if (_editorControl.ViewMode == EditorViewMode.Source)
            {
                // Source view - use service to find all matches
                var text = _editorControl.Editor.Text;
                var matches = _findReplaceService.FindAll(
                    text,
                    searchTerm,
                    MatchCaseCheckBox.IsChecked == true,
                    WholeWordCheckBox.IsChecked == true);

                if (matches.Count == 0)
                {
                    MessageBox.Show($"Cannot find \"{searchTerm}\"", "Replace All", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Replace all in one undo step
                var document = _editorControl.Editor.Document;
                using (document.RunUpdate())
                {
                    // Replace in reverse order to maintain offsets
                    for (int i = matches.Count - 1; i >= 0; i--)
                    {
                        var match = matches[i];
                        document.Replace(match.StartOffset, match.Length, replaceTerm);
                    }
                }

                if (_editorControl.Document != null)
                {
                    _editorControl.Document.IsDirty = true;
                }

                MessageBox.Show($"Replaced {matches.Count} occurrence(s)", "Replace All", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Formatted view - find all matches in RichTextBox
                var rtb = _editorControl.FindName("FormattedEditor") as RichTextBox;
                if (rtb != null)
                {
                    var matchRanges = new System.Collections.Generic.List<TextRange>();
                    var currentPos = rtb.Document.ContentStart;

                    // Find all occurrences
                    while (currentPos != null && currentPos.CompareTo(rtb.Document.ContentEnd) < 0)
                    {
                        var match = FindTextInRichTextBox(rtb, searchTerm, currentPos,
                            MatchCaseCheckBox.IsChecked == true,
                            WholeWordCheckBox.IsChecked == true);

                        if (match == null)
                            break;

                        matchRanges.Add(match);
                        currentPos = match.End;
                    }

                    if (matchRanges.Count == 0)
                    {
                        MessageBox.Show($"Cannot find \"{searchTerm}\"", "Replace All", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Replace all in one undo operation
                    rtb.BeginChange();
                    try
                    {
                        // Replace in reverse order to maintain positions
                        for (int i = matchRanges.Count - 1; i >= 0; i--)
                        {
                            matchRanges[i].Text = replaceTerm;
                        }
                    }
                    finally
                    {
                        rtb.EndChange();
                    }

                    if (_editorControl.Document != null)
                    {
                        _editorControl.Document.IsDirty = true;
                    }

                    MessageBox.Show($"Replaced {matchRanges.Count} occurrence(s)", "Replace All", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Industry-standard TextRange search helpers for RichTextBox
        private string GetTextInRange(TextPointer start, TextPointer end)
        {
            var textRange = new TextRange(start, end);
            return textRange.Text;
        }

        private TextRange? FindTextInRichTextBox(RichTextBox rtb, string searchText, TextPointer startPosition, bool matchCase, bool wholeWord)
        {
            if (string.IsNullOrEmpty(searchText))
                return null;

            var comparisonType = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var navigator = startPosition.GetPositionAtOffset(0, LogicalDirection.Forward);

            while (navigator != null && navigator.CompareTo(rtb.Document.ContentEnd) < 0)
            {
                // Skip to next text position
                if (navigator.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                {
                    navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    continue;
                }

                // Try to get enough text to match
                var runLength = navigator.GetTextRunLength(LogicalDirection.Forward);
                var availableText = navigator.GetTextInRun(LogicalDirection.Forward);

                // Check if we can match starting here
                if (availableText.Length > 0)
                {
                    var matchIndex = availableText.IndexOf(searchText, comparisonType);
                    
                    while (matchIndex >= 0)
                    {
                        var matchStart = navigator.GetPositionAtOffset(matchIndex);
                        if (matchStart == null)
                            break;

                        // Get the actual matched text to verify
                        var potentialEnd = matchStart.GetPositionAtOffset(searchText.Length);
                        if (potentialEnd != null)
                        {
                            var matchedText = new TextRange(matchStart, potentialEnd).Text;
                            
                            // Verify the match (handle newlines and whitespace)
                            if (matchedText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ")
                                .Equals(searchText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "), comparisonType))
                            {
                                if (wholeWord)
                                {
                                    // Check word boundaries
                                    var charBefore = matchStart.GetTextInRun(LogicalDirection.Backward);
                                    var charAfter = potentialEnd.GetTextInRun(LogicalDirection.Forward);
                                    
                                    var isStartBoundary = string.IsNullOrEmpty(charBefore) || 
                                        (charBefore.Length > 0 && !char.IsLetterOrDigit(charBefore[charBefore.Length - 1]));
                                    var isEndBoundary = string.IsNullOrEmpty(charAfter) || 
                                        (charAfter.Length > 0 && !char.IsLetterOrDigit(charAfter[0]));

                                    if (isStartBoundary && isEndBoundary)
                                        return new TextRange(matchStart, potentialEnd);
                                }
                                else
                                {
                                    return new TextRange(matchStart, potentialEnd);
                                }
                            }
                        }

                        // Try next match in this run
                        matchIndex = availableText.IndexOf(searchText, matchIndex + 1, comparisonType);
                    }
                }

                // Move to next text position
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }

            return null;
        }

        private TextRange? FindTextInRichTextBoxBackward(RichTextBox rtb, string searchText, TextPointer endPosition, bool matchCase, bool wholeWord)
        {
            if (string.IsNullOrEmpty(searchText))
                return null;

            var comparisonType = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var navigator = endPosition.GetPositionAtOffset(0, LogicalDirection.Backward);
            TextRange? lastMatch = null;

            // Search forward from start to end, keeping last match
            var current = rtb.Document.ContentStart;
            while (current != null && current.CompareTo(endPosition) < 0)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                {
                    current = current.GetNextContextPosition(LogicalDirection.Forward);
                    continue;
                }

                var availableText = current.GetTextInRun(LogicalDirection.Forward);
                if (availableText.Length > 0)
                {
                    var matchIndex = availableText.IndexOf(searchText, comparisonType);
                    
                    while (matchIndex >= 0)
                    {
                        var matchStart = current.GetPositionAtOffset(matchIndex);
                        if (matchStart == null)
                            break;

                        // Make sure this match is before our end position
                        if (matchStart.CompareTo(endPosition) >= 0)
                            break;

                        var potentialEnd = matchStart.GetPositionAtOffset(searchText.Length);
                        if (potentialEnd != null && potentialEnd.CompareTo(endPosition) <= 0)
                        {
                            var matchedText = new TextRange(matchStart, potentialEnd).Text;
                            
                            if (matchedText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ")
                                .Equals(searchText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "), comparisonType))
                            {
                                if (wholeWord)
                                {
                                    var charBefore = matchStart.GetTextInRun(LogicalDirection.Backward);
                                    var charAfter = potentialEnd.GetTextInRun(LogicalDirection.Forward);
                                    
                                    var isStartBoundary = string.IsNullOrEmpty(charBefore) || 
                                        (charBefore.Length > 0 && !char.IsLetterOrDigit(charBefore[charBefore.Length - 1]));
                                    var isEndBoundary = string.IsNullOrEmpty(charAfter) || 
                                        (charAfter.Length > 0 && !char.IsLetterOrDigit(charAfter[0]));

                                    if (isStartBoundary && isEndBoundary)
                                        lastMatch = new TextRange(matchStart, potentialEnd);
                                }
                                else
                                {
                                    lastMatch = new TextRange(matchStart, potentialEnd);
                                }
                            }
                        }

                        matchIndex = availableText.IndexOf(searchText, matchIndex + 1, comparisonType);
                    }
                }

                current = current.GetNextContextPosition(LogicalDirection.Forward);
            }

            return lastMatch;
        }
    }
}
