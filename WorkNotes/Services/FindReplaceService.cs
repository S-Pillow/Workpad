using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WorkNotes.Services
{
    /// <summary>
    /// Find and replace service with support for match case, whole word, and wrap-around.
    /// </summary>
    public class FindReplaceService
    {
        public class SearchResult
        {
            public int StartOffset { get; set; }
            public int Length { get; set; }
            public string MatchedText { get; set; } = string.Empty;
        }

        /// <summary>
        /// Finds the next occurrence of the search term starting from the specified offset.
        /// </summary>
        public SearchResult? FindNext(string text, string searchTerm, int startOffset, bool matchCase, bool wholeWord, bool wrapAround)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchTerm))
                return null;

            var result = FindInRange(text, searchTerm, startOffset, text.Length, matchCase, wholeWord);
            
            // If not found and wrap-around is enabled, search from the beginning
            if (result == null && wrapAround && startOffset > 0)
            {
                result = FindInRange(text, searchTerm, 0, startOffset, matchCase, wholeWord);
            }

            return result;
        }

        /// <summary>
        /// Finds the previous occurrence of the search term starting from the specified offset.
        /// </summary>
        public SearchResult? FindPrevious(string text, string searchTerm, int startOffset, bool matchCase, bool wholeWord, bool wrapAround)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchTerm))
                return null;

            // Search backwards from current position
            var result = FindInRangeBackward(text, searchTerm, 0, startOffset, matchCase, wholeWord);
            
            // If not found and wrap-around is enabled, search from the end
            if (result == null && wrapAround && startOffset < text.Length)
            {
                result = FindInRangeBackward(text, searchTerm, startOffset, text.Length, matchCase, wholeWord);
            }

            return result;
        }

        /// <summary>
        /// Finds all occurrences of the search term in the text.
        /// </summary>
        public List<SearchResult> FindAll(string text, string searchTerm, bool matchCase, bool wholeWord)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchTerm))
                return results;

            int offset = 0;
            while (offset < text.Length)
            {
                var result = FindInRange(text, searchTerm, offset, text.Length, matchCase, wholeWord);
                if (result == null)
                    break;

                results.Add(result);
                offset = result.StartOffset + result.Length;
            }

            return results;
        }

        private SearchResult? FindInRange(string text, string searchTerm, int startOffset, int endOffset, bool matchCase, bool wholeWord)
        {
            if (startOffset < 0 || startOffset >= text.Length || endOffset <= startOffset)
                return null;

            var searchText = text.Substring(startOffset, Math.Min(endOffset - startOffset, text.Length - startOffset));
            
            if (wholeWord)
            {
                var pattern = $@"\b{Regex.Escape(searchTerm)}\b";
                var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var match = Regex.Match(searchText, pattern, options);
                
                if (match.Success)
                {
                    return new SearchResult
                    {
                        StartOffset = startOffset + match.Index,
                        Length = match.Length,
                        MatchedText = match.Value
                    };
                }
            }
            else
            {
                var comparisonType = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var index = searchText.IndexOf(searchTerm, comparisonType);
                
                if (index >= 0)
                {
                    return new SearchResult
                    {
                        StartOffset = startOffset + index,
                        Length = searchTerm.Length,
                        MatchedText = text.Substring(startOffset + index, searchTerm.Length)
                    };
                }
            }

            return null;
        }

        private SearchResult? FindInRangeBackward(string text, string searchTerm, int startOffset, int endOffset, bool matchCase, bool wholeWord)
        {
            if (startOffset < 0 || endOffset <= startOffset || endOffset > text.Length)
                return null;

            var searchText = text.Substring(startOffset, endOffset - startOffset);
            
            if (wholeWord)
            {
                var pattern = $@"\b{Regex.Escape(searchTerm)}\b";
                var options = matchCase ? RegexOptions.RightToLeft : RegexOptions.RightToLeft | RegexOptions.IgnoreCase;
                var match = Regex.Match(searchText, pattern, options);
                
                if (match.Success)
                {
                    return new SearchResult
                    {
                        StartOffset = startOffset + match.Index,
                        Length = match.Length,
                        MatchedText = match.Value
                    };
                }
            }
            else
            {
                var comparisonType = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var index = searchText.LastIndexOf(searchTerm, comparisonType);
                
                if (index >= 0)
                {
                    return new SearchResult
                    {
                        StartOffset = startOffset + index,
                        Length = searchTerm.Length,
                        MatchedText = text.Substring(startOffset + index, searchTerm.Length)
                    };
                }
            }

            return null;
        }
    }
}
