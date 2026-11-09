using System.Collections.Generic;
using WorkNotes.Models;

namespace WorkNotes.Services
{
    public enum CopyAction
    {
        /// <summary>The copy that just wrote the file — already current.</summary>
        None,
        /// <summary>Clean copy: reload from disk so it can't later save stale text.</summary>
        Reload,
        /// <summary>Unsaved edits: never overwrite — raise the conflict banner.</summary>
        Conflict
    }

    /// <summary>
    /// The same file can be open as several independent <see cref="Document"/>
    /// copies (both split panes, or another tab's pane). When the file on disk
    /// changes — externally, or because one copy saved — EVERY other copy must
    /// react: a clean copy reloads, a dirty one gets a conflict. Reacting only
    /// on the first match left the other copy stale, and saving it later
    /// silently overwrote the newer file.
    /// </summary>
    public static class SameFileCopies
    {
        /// <param name="openCopies">Every open document backed by the changed
        /// path. The same instance may be listed more than once (a tab's
        /// document is also its top pane's); it is handled once.</param>
        /// <param name="savedBy">The copy whose own save changed the file, or
        /// null for an external change.</param>
        public static List<(Document Copy, CopyAction Action)> Plan(IEnumerable<Document> openCopies, Document? savedBy)
        {
            var seen = new HashSet<Document>(ReferenceEqualityComparer.Instance);
            var plan = new List<(Document, CopyAction)>();
            foreach (var copy in openCopies)
            {
                if (copy == null || !seen.Add(copy))
                    continue;
                var action = ReferenceEquals(copy, savedBy) ? CopyAction.None
                    : copy.IsDirty ? CopyAction.Conflict
                    : CopyAction.Reload;
                plan.Add((copy, action));
            }
            return plan;
        }
    }
}
