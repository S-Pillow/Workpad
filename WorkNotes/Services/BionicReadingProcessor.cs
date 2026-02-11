using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    /// <summary>
    /// Applies Bionic Reading effect to a FlowDocument by post-processing Run elements.
    /// </summary>
    public static class BionicReadingProcessor
    {
        private static readonly Regex UrlPattern = new Regex(
            @"https?://[^\s]+|www\.[^\s]+|[a-zA-Z0-9][a-zA-Z0-9-]*\.[a-zA-Z]{2,}(?:/[^\s]*)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EmailPattern = new Regex(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Applies Bionic Reading effect to all text in the FlowDocument.
        /// </summary>
        public static void ApplyBionicReading(FlowDocument document, BionicStrength strength)
        {
            if (document == null) return;

            foreach (var block in document.Blocks.ToList())
            {
                if (block is Paragraph paragraph)
                {
                    ApplyBionicToParagraph(paragraph, strength);
                }
            }
        }

        private static void ApplyBionicToParagraph(Paragraph paragraph, BionicStrength strength)
        {
            var inlinesToProcess = paragraph.Inlines.ToList();
            var newInlines = new List<Inline>();

            foreach (var inline in inlinesToProcess)
            {
                if (inline is Run run && !(inline.Parent is Hyperlink))
                {
                    // Process this run for bionic reading
                    var processedInlines = ProcessRunForBionic(run, strength);
                    newInlines.AddRange(processedInlines);
                }
                else if (inline is Hyperlink hyperlink)
                {
                    // Keep hyperlinks as-is (don't apply bionic to links)
                    newInlines.Add(hyperlink);
                }
                else if (inline is Span span && !(inline is Hyperlink))
                {
                    // Process spans recursively (for bold/italic text)
                    ProcessSpanForBionic(span, strength);
                    newInlines.Add(span);
                }
                else
                {
                    // Keep other inlines as-is
                    newInlines.Add(inline);
                }
            }

            // Replace all inlines
            paragraph.Inlines.Clear();
            foreach (var inline in newInlines)
            {
                paragraph.Inlines.Add(inline);
            }
        }

        private static void ProcessSpanForBionic(Span span, BionicStrength strength)
        {
            var inlinesToProcess = span.Inlines.ToList();
            var newInlines = new List<Inline>();

            foreach (var inline in inlinesToProcess)
            {
                if (inline is Run run)
                {
                    // For runs inside spans (bold/italic), we need to preserve the formatting
                    // and apply bionic on top of it
                    var processedInlines = ProcessRunForBionic(run, strength);
                    newInlines.AddRange(processedInlines);
                }
                else
                {
                    newInlines.Add(inline);
                }
            }

            span.Inlines.Clear();
            foreach (var inline in newInlines)
            {
                span.Inlines.Add(inline);
            }
        }

        private static List<Inline> ProcessRunForBionic(Run run, BionicStrength strength)
        {
            var result = new List<Inline>();
            var text = run.Text;

            // Skip if empty
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Add(run);
                return result;
            }

            // Skip if it's a URL or email
            if (UrlPattern.IsMatch(text) || EmailPattern.IsMatch(text))
            {
                result.Add(run);
                return result;
            }

            // Check if this run is already bold (from markdown **text**)
            var isAlreadyBold = run.FontWeight == FontWeights.Bold;
            var baseWeight = run.FontWeight;
            var baseStyle = run.FontStyle;

            // Split into words and non-words (official standard: all alphabetic words)
            var tokens = Regex.Matches(text, @"(\b[a-zA-Z]+\b|[^\w]+|\b\d+\w*\b)");

            foreach (Match match in tokens)
            {
                var token = match.Value;

                // Check if it's an alphabetic word
                if (Regex.IsMatch(token, @"^[a-zA-Z]+$"))
                {
                    // Apply bionic effect
                    var boldLength = CalculateBoldLength(token.Length, strength);

                    if (boldLength > 0 && boldLength < token.Length)
                    {
                        // Bold part (always bold, even if base text is already bold)
                        result.Add(new Run(token.Substring(0, boldLength))
                        {
                            FontWeight = FontWeights.Bold,
                            FontStyle = baseStyle,
                            Foreground = run.Foreground,
                            Background = run.Background
                        });

                        // Normal part (use original weight if not already bold, otherwise keep bold)
                        result.Add(new Run(token.Substring(boldLength))
                        {
                            FontWeight = isAlreadyBold ? FontWeights.Bold : FontWeights.Normal,
                            FontStyle = baseStyle,
                            Foreground = run.Foreground,
                            Background = run.Background
                        });
                    }
                    else
                    {
                        // Word is too short or other reason, keep as-is
                        result.Add(new Run(token)
                        {
                            FontWeight = baseWeight,
                            FontStyle = baseStyle,
                            Foreground = run.Foreground,
                            Background = run.Background
                        });
                    }
                }
                else
                {
                    // Not a word (spaces, punctuation, short words) - keep as-is
                    result.Add(new Run(token)
                    {
                        FontWeight = baseWeight,
                        FontStyle = baseStyle,
                        Foreground = run.Foreground,
                        Background = run.Background
                    });
                }
            }

            return result;
        }

        private static int CalculateBoldLength(int wordLength, BionicStrength strength)
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
    }
}
