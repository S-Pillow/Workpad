namespace WorkNotes.Models
{
    /// <summary>
    /// Application-level view modes controlling window chrome and layout visibility.
    /// This is separate from EditorViewMode (Formatted/Source), which controls
    /// which editor backend is active within a tab.
    /// </summary>
    public enum AppViewMode
    {
        /// <summary>Normal windowed mode with all chrome visible.</summary>
        Normal,

        /// <summary>Full screen: maximized, no chrome except tab strip and exit button.</summary>
        FullScreen,

        /// <summary>Post-It: same window bounds, only editor visible (no tab strip, no chrome).</summary>
        PostIt,

        /// <summary>Distraction-free: full screen, centered reading-width content, no chrome.</summary>
        DistractionFree
    }
}
