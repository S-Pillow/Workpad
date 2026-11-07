using System.Collections.Generic;

namespace WorkNotes.Services
{
    /// <summary>
    /// WP-17 source preservation. Every rendered block carries the exact source
    /// it came from plus a fingerprint of how it looked when rendered. At
    /// serialization an unchanged fingerprint means "emit the source verbatim";
    /// only a changed block is regenerated, and only from its own syntax.
    /// <para>
    /// <see cref="Owner"/> pins the anchor to the element it was created for:
    /// when WPF splits a paragraph (Enter) it may copy local values such as Tag
    /// onto the new half, and that copy must not claim the original's source.
    /// </para>
    /// </summary>
    public abstract class SourceAnchor
    {
        protected SourceAnchor(string source, string terminator)
        {
            Source = source;
            Terminator = terminator;
        }

        /// <summary>Authored text, internal line terminators included, final one excluded.</summary>
        public string Source { get; }

        /// <summary>The terminator that followed the block in the source ("" at end of file).</summary>
        public string Terminator { get; }

        public string Fingerprint { get; set; } = "";

        public object? Owner { get; set; }

        public bool IsOwnedBy(object element) => ReferenceEquals(Owner, element);
    }

    /// <summary>FlowDocument.Tag: the newline new content is written with.</summary>
    public sealed class DocumentAnchor
    {
        public DocumentAnchor(string newline) => Newline = newline;
        public string Newline { get; }
    }

    public sealed class LineAnchor : SourceAnchor
    {
        public LineAnchor(string source, string terminator) : base(source, terminator) { }
    }

    public sealed class HeadingAnchor : SourceAnchor
    {
        public HeadingAnchor(string source, string terminator) : base(source, terminator) { }
        public int Level { get; init; }
        public string Prefix { get; init; } = "";
        public string Suffix { get; init; } = "";
        public bool IsSetext { get; init; }

        /// <summary>Setext only: first line's terminator + the underline line.</summary>
        public string SetextTail { get; init; } = "";
    }

    public sealed class CodeAnchor : SourceAnchor
    {
        public CodeAnchor(string source, string terminator) : base(source, terminator) { }
        public string OpenFence { get; init; } = "";
        public string OpenTerminator { get; init; } = "";
        public string CloseFence { get; init; } = "";

        /// <summary>Terminator of the line just before the closing fence.</summary>
        public string CloseLeadTerminator { get; init; } = "";

        /// <summary>Newline between body lines when the body is regenerated.</summary>
        public string BodyNewline { get; init; } = "";

        public bool HadBody { get; init; }
        public string? Info { get; init; }
    }

    public sealed class RuleAnchor : SourceAnchor
    {
        public RuleAnchor(string source, string terminator) : base(source, terminator) { }
    }

    /// <summary>On a blockquote Section; its lines carry <see cref="QuoteLineAnchor"/>.</summary>
    public sealed class QuoteAnchor
    {
        public object? Owner { get; set; }
    }

    public sealed class QuoteLineAnchor : SourceAnchor
    {
        public QuoteLineAnchor(string source, string terminator) : base(source, terminator) { }

        /// <summary>The "&gt; " markers before the content ("" for a lazy line).</summary>
        public string Prefix { get; init; } = "";
    }

    /// <summary>On a List element.</summary>
    public sealed class ListAnchor
    {
        public bool Ordered { get; init; }
        public object? Owner { get; set; }
    }

    /// <summary>On a list item's first paragraph: the item head lines.</summary>
    public sealed class ListItemAnchor : SourceAnchor
    {
        public ListItemAnchor(string source, string terminator) : base(source, terminator) { }
        public int Depth { get; init; }
        public string Indent { get; init; } = "";
        public string Marker { get; init; } = "";
        public string Prefix { get; init; } = "";
        public bool Ordered { get; init; }
        public long Number { get; init; }
        public char Delimiter { get; init; }

        /// <summary>Terminator after the item's last content line.</summary>
        public string ContentTerminator { get; init; } = "";

        /// <summary>Blank lines between this item's content and what follows.</summary>
        public IReadOnlyList<SourceLine> BlankTail { get; init; } = new List<SourceLine>();

        /// <summary>Whitespace after the marker ("" means a bare marker line).</summary>
        public string Spacing => Prefix.Substring(System.Math.Min(Prefix.Length, Indent.Length + Marker.Length));
    }

    /// <summary>On a Table: the alignment row, re-emitted after the header row.</summary>
    public sealed class TableAnchor
    {
        public TableAnchor(SourceLine delimiter) => Delimiter = delimiter;
        public SourceLine Delimiter { get; }
        public object? Owner { get; set; }
    }

    public sealed class TableRowAnchor : SourceAnchor
    {
        public TableRowAnchor(string source, string terminator) : base(source, terminator) { }
        public bool IsHeader { get; init; }
        public bool LeadingPipe { get; init; } = true;
        public bool TrailingPipe { get; init; } = true;
        public string Indent { get; init; } = "";
    }

    /// <summary>On an inline-code Run: the authored span (backticks included)
    /// and the text it displayed, so an unedited span round-trips exactly.</summary>
    public sealed class InlineCodeMarker
    {
        public InlineCodeMarker(string raw, string display)
        {
            Raw = raw;
            Display = display;
        }

        public string Raw { get; }
        public string Display { get; }
    }
}
