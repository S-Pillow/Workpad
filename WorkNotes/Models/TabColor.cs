namespace WorkNotes.Models
{
    /// <summary>Per-tab identity colors (WP-06). None plus seven restrained
    /// swatches; the header shows a small marker dot, never a full tint, so the
    /// tab strip keeps its Notepad-calm look.</summary>
    public enum TabColor
    {
        None = 0,
        Red = 1,
        Orange = 2,
        Yellow = 3,
        Green = 4,
        Teal = 5,
        Blue = 6,
        Purple = 7,
    }
}
