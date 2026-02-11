using System;
using System.Collections.Generic;

namespace WorkNotes.Models
{
    /// <summary>
    /// Represents the state of a single tab for session restoration.
    /// </summary>
    public class TabSessionState
    {
        /// <summary>
        /// Gets or sets the file path (null for Untitled).
        /// </summary>
        public string? FilePath { get; set; }

        /// <summary>
        /// Gets or sets the editor view mode.
        /// </summary>
        public EditorViewMode ViewMode { get; set; }

        /// <summary>
        /// Gets or sets the cursor position in the text.
        /// </summary>
        public int CursorPosition { get; set; }

        /// <summary>
        /// Gets or sets the scroll offset.
        /// </summary>
        public double ScrollOffset { get; set; }
    }

    /// <summary>
    /// Represents the complete session state for restoration.
    /// </summary>
    public class TabSession
    {
        /// <summary>
        /// Gets or sets the list of open tabs.
        /// </summary>
        public List<TabSessionState> Tabs { get; set; } = new List<TabSessionState>();

        /// <summary>
        /// Gets or sets the index of the active tab.
        /// </summary>
        public int ActiveTabIndex { get; set; }
    }

    /// <summary>
    /// Represents a recently closed tab for reopening.
    /// </summary>
    public class ClosedTabInfo
    {
        /// <summary>
        /// Gets or sets the file path (null for Untitled).
        /// </summary>
        public string? FilePath { get; set; }

        /// <summary>
        /// Gets or sets the content (for unsaved Untitled tabs).
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Gets or sets the editor view mode.
        /// </summary>
        public EditorViewMode ViewMode { get; set; }

        /// <summary>
        /// Gets or sets when the tab was closed.
        /// </summary>
        public DateTime ClosedAt { get; set; }
    }
}
