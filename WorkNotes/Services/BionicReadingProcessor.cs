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
    /// Marks a Run whose FontWeight was set by Bionic Reading rather than by the author.
    /// Carries the weight the run would have had WITHOUT bionic, so the Markdown serializer
    /// can round-trip the document without turning bionic prefixes into literal ** markers.
    /// </summary>
    public sealed class BionicRunMarker
    {
        public BionicRunMarker(FontWeight originalWeight)
        {
            OriginalWeight = originalWeight;
        }

        /// <summary>The font weight the author actually applied (bionic bolding excluded).</summary>
        public FontWeight OriginalWeight { get; }
    }

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

            // THEME FIX (dark mode): only carry over Foreground/Background when the source run
            // actually has a LOCAL value. Reading run.Foreground on a document that is not yet
            // attached to the visual tree returns the framework default (Black); stamping that
            // onto the generated runs as a local value permanently overrides theme inheritance,
            // which made bionic text invisible on the dark editor background.
            var hasLocalForeground =
                run.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue;
            var hasLocalBackground =
                run.ReadLocalValue(TextElement.BackgroundProperty) != DependencyProperty.UnsetValue;

            // authorWeight = what the serializer should treat this run as. Bionic may render a
            // prefix bold, but only the author's own **bold** should survive a round-trip.
            Run MakeRun(string runText, FontWeight weight)
            {
                var newRun = new Run(runText)
                {
                    FontWeight = weight,
                    FontStyle = baseStyle,
                    Tag = new BionicRunMarker(isAlreadyBold ? FontWeights.Bold : baseWeight)
                };
                if (hasLocalForeground) newRun.Foreground = run.Foreground;
                if (hasLocalBackground) newRun.Background = run.Background;
                return newRun;
            }

            // Split into words and non-words (official standard: all alphabetic words)
            // Updated to handle underscores: treat them as separators like spaces
            var tokens = Regex.Matches(text, @"([a-zA-Z]+|\d+|[^\w]|\s+|_)");

            foreach (Match match in tokens)
            {
                var token = match.Value;

                // Check if it's an alphabetic word
                if (Regex.IsMatch(token, @"^[a-zA-Z]+$"))
                {
                    // Apply bionic effect
                    var boldLength = CalculateBoldLength(token.Length, strength);

                    // For single-character words, bold the entire character
                    if (token.Length == 1)
                    {
                        result.Add(MakeRun(token, FontWeights.Bold));
                    }
                    else if (boldLength > 0 && boldLength < token.Length)
                    {
                        // Bold part (always bold, even if base text is already bold)
                        result.Add(MakeRun(token.Substring(0, boldLength), FontWeights.Bold));

                        // Normal part (use original weight if not already bold, otherwise keep bold)
                        result.Add(MakeRun(token.Substring(boldLength),
                            isAlreadyBold ? FontWeights.Bold : FontWeights.Normal));
                    }
                    else
                    {
                        // Word is too short or other reason, keep as-is
                        result.Add(MakeRun(token, baseWeight));
                    }
                }
                else
                {
                    // Not a word (spaces, punctuation, short words) - keep as-is
                    result.Add(MakeRun(token, baseWeight));
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
