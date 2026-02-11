using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace WorkNotes.Services
{
    /// <summary>
    /// Text marker service for AvalonEdit spell checking.
    /// </summary>
    public class SpellCheckMarkerService
    {
        private readonly TextDocument _document;
        private readonly List<SpellCheckMarker> _markers = new List<SpellCheckMarker>();

        public SpellCheckMarkerService(TextDocument document)
        {
            _document = document;
        }

        public IEnumerable<SpellCheckMarker> Markers => _markers;

        /// <summary>
        /// Clears all spell check markers.
        /// </summary>
        public void Clear()
        {
            _markers.Clear();
        }

        /// <summary>
        /// Adds a spell check marker for a misspelled word.
        /// </summary>
        public void AddMarker(int startOffset, int length)
        {
            if (startOffset < 0 || startOffset + length > _document.TextLength)
                return;

            _markers.Add(new SpellCheckMarker
            {
                StartOffset = startOffset,
                Length = length
            });
        }

        /// <summary>
        /// Gets the marker at a specific offset.
        /// </summary>
        public SpellCheckMarker? GetMarkerAtOffset(int offset)
        {
            foreach (var marker in _markers)
            {
                if (offset >= marker.StartOffset && offset < marker.StartOffset + marker.Length)
                {
                    return marker;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the misspelled word at a specific offset.
        /// </summary>
        public string? GetWordAtOffset(int offset)
        {
            var marker = GetMarkerAtOffset(offset);
            if (marker == null)
                return null;

            try
            {
                return _document.GetText(marker.StartOffset, marker.Length);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Represents a spell check marker (red squiggle underline).
    /// </summary>
    public class SpellCheckMarker
    {
        public int StartOffset { get; set; }
        public int Length { get; set; }
    }

    /// <summary>
    /// Background renderer for spell check squiggles.
    /// </summary>
    public class SpellCheckBackgroundRenderer : IBackgroundRenderer
    {
        private readonly SpellCheckMarkerService _markerService;
        private readonly Pen _squigglePen;

        public SpellCheckBackgroundRenderer(SpellCheckMarkerService markerService)
        {
            _markerService = markerService;

            // Red squiggle pen
            var brush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            brush.Freeze();
            _squigglePen = new Pen(brush, 1);
            _squigglePen.Freeze();
        }

        public KnownLayer Layer => KnownLayer.Selection; // Draw below text

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_markerService == null || textView == null)
                return;

            var visualLines = textView.VisualLines;
            if (visualLines.Count == 0)
                return;

            var viewStart = visualLines[0].FirstDocumentLine.Offset;
            var viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;

            foreach (var marker in _markerService.Markers)
            {
                // Skip if marker is outside visible range
                if (marker.StartOffset + marker.Length < viewStart || marker.StartOffset > viewEnd)
                    continue;

                // Get geometry for the marked text
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment
                {
                    StartOffset = marker.StartOffset,
                    Length = marker.Length
                }))
                {
                    // Draw wavy underline
                    var baselineY = rect.Bottom - 1;
                    DrawWavyLine(drawingContext, rect.Left, baselineY, rect.Width);
                }
            }
        }

        private void DrawWavyLine(DrawingContext drawingContext, double x, double y, double width)
        {
            const double waveHeight = 2;
            const double waveLength = 4;

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(x, y), false, false);

                var currentX = x;
                var goingUp = true;

                while (currentX < x + width)
                {
                    var nextX = Math.Min(currentX + waveLength / 2, x + width);
                    var nextY = goingUp ? y - waveHeight : y + waveHeight;
                    context.LineTo(new Point(nextX, nextY), true, false);
                    currentX = nextX;
                    goingUp = !goingUp;
                }
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(null, _squigglePen, geometry);
        }
    }
}
