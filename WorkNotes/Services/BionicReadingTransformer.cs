using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    /// <summary>
    /// Applies Bionic Reading visual effect to AvalonEdit text.
    /// Bolds the first N letters of each word based on word length and strength setting.
    /// </summary>
    public class BionicReadingTransformer : DocumentColorizingTransformer
    {
        private readonly Func<bool> _isEnabled;
        private readonly Func<BionicStrength> _getStrength;

        // Patterns to skip
        private static readonly Regex UrlPattern = new Regex(
            @"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EmailPattern = new Regex(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled);

        // Word pattern for bionic effect — must be static to avoid allocating
        // a new compiled Regex per ColorizeLine call (which fires on every render pass).
        private static readonly Regex WordPattern = new Regex(
            @"\b[a-zA-Z]+\b", RegexOptions.Compiled);

        public BionicReadingTransformer(Func<bool> isEnabled, Func<BionicStrength> getStrength)
        {
            _isEnabled = isEnabled;
            _getStrength = getStrength;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_isEnabled())
                return;

            var lineText = CurrentContext.Document.GetText(line);
            if (string.IsNullOrWhiteSpace(lineText))
                return;

            // Find protected spans (URLs, emails)
            var protectedSpans = new List<(int start, int end)>();

            foreach (Match match in UrlPattern.Matches(lineText))
            {
                protectedSpans.Add((match.Index, match.Index + match.Length));
            }

            foreach (Match match in EmailPattern.Matches(lineText))
            {
                if (!IsInProtectedSpan(match.Index, match.Index + match.Length, protectedSpans))
                {
                    protectedSpans.Add((match.Index, match.Index + match.Length));
                }
            }

            // Find words and apply bionic effect (official standard: all words)
            foreach (Match match in WordPattern.Matches(lineText))
            {
                var wordStart = match.Index;
                var wordEnd = match.Index + match.Length;

                // Skip if in protected span
                if (IsInProtectedSpan(wordStart, wordEnd, protectedSpans))
                    continue;

                var word = match.Value;
                var boldLength = CalculateBoldLength(word.Length, _getStrength());

                if (boldLength > 0)
                {
                    // Bold the first N characters
                    ChangeLinePart(
                        line.Offset + wordStart,
                        line.Offset + wordStart + boldLength,
                        element =>
                        {
                            // Preserve existing style (e.g. italic) while applying bold
                            element.TextRunProperties.SetTypeface(new Typeface(
                                element.TextRunProperties.Typeface.FontFamily,
                                element.TextRunProperties.Typeface.Style,
                                FontWeights.Bold,
                                FontStretches.Normal));
                        });
                }
            }
        }

        private int CalculateBoldLength(int wordLength, BionicStrength strength)
        {
            // Official Bionic Reading: bold approximately first half of each word
            // Using Math.Ceiling(length / 2) as per the official algorithm
            
            // Strength adjusts the formula slightly
            switch (strength)
            {
                case BionicStrength.Light:
                    // Slightly less than half
                    return Math.Max(1, (int)Math.Floor(wordLength / 2.5));

                case BionicStrength.Medium:
                    // Official standard: first half
                    return (int)Math.Ceiling(wordLength / 2.0);

                case BionicStrength.Strong:
                    // Slightly more than half
                    return Math.Min(wordLength - 1, (int)Math.Ceiling(wordLength * 0.6));

                default:
                    return (int)Math.Ceiling(wordLength / 2.0);
            }
        }

        private bool IsInProtectedSpan(int start, int end, List<(int start, int end)> protectedSpans)
        {
            foreach (var span in protectedSpans)
            {
                if ((start >= span.start && start < span.end) ||
                    (end > span.start && end <= span.end) ||
                    (start <= span.start && end >= span.end))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
