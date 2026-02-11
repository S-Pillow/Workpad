using System.Text;
using System.Windows.Documents;

namespace WorkNotes.Services
{
    /// <summary>
    /// Serializes FlowDocument back to Markdown text.
    /// </summary>
    public class MarkdownSerializer
    {
        /// <summary>
        /// Converts a FlowDocument to Markdown text.
        /// </summary>
        public string SerializeToMarkdown(FlowDocument document)
        {
            var sb = new StringBuilder();
            bool firstBlock = true;

            foreach (var block in document.Blocks)
            {
                if (!firstBlock)
                    sb.AppendLine();

                if (block is Paragraph paragraph)
                {
                    SerializeParagraph(paragraph, sb);
                }

                firstBlock = false;
            }

            return sb.ToString();
        }

        private void SerializeParagraph(Paragraph paragraph, StringBuilder sb)
        {
            foreach (var inline in paragraph.Inlines)
            {
                SerializeInline(inline, sb);
            }
        }

        private void SerializeInline(Inline inline, StringBuilder sb)
        {
            if (inline is Run run)
            {
                var text = run.Text;
                var isBold = run.FontWeight == System.Windows.FontWeights.Bold;
                var isItalic = run.FontStyle == System.Windows.FontStyles.Italic;

                if (isBold && isItalic)
                {
                    sb.Append("***");
                    sb.Append(text);
                    sb.Append("***");
                }
                else if (isBold)
                {
                    sb.Append("**");
                    sb.Append(text);
                    sb.Append("**");
                }
                else if (isItalic)
                {
                    sb.Append("*");
                    sb.Append(text);
                    sb.Append("*");
                }
                else
                {
                    sb.Append(text);
                }
            }
            else if (inline is Hyperlink hyperlink)
            {
                // Extract text from hyperlink
                var linkText = new TextRange(hyperlink.ContentStart, hyperlink.ContentEnd).Text;
                var url = hyperlink.NavigateUri?.ToString() ?? string.Empty;

                // If we have a URL, serialize as markdown link
                if (!string.IsNullOrEmpty(url))
                {
                    // Check if the link text is the same as the URL (bare URL)
                    if (linkText == url || 
                        linkText == url.Replace("https://", "").Replace("http://", "").TrimEnd('/') ||
                        url.Replace("https://", "").Replace("http://", "").TrimEnd('/').StartsWith(linkText.TrimEnd('/')))
                    {
                        // Bare URL - output the link text (what's displayed), not the full URL
                        sb.Append(linkText);
                    }
                    else
                    {
                        // Labeled link - output as [label](url)
                        sb.Append($"[{linkText}]({url})");
                    }
                }
                else
                {
                    // No URL, just output the text
                    sb.Append(linkText);
                }
            }
            else if (inline is Span span)
            {
                foreach (var child in span.Inlines)
                {
                    SerializeInline(child, sb);
                }
            }
        }
    }
}
