using WorkNotes.Models;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>
/// PR #9 item 3 regression: the same file open in both split panes. Every
/// other copy must react to a disk change — clean copies reload, dirty ones
/// conflict — including when the change is our own save from the other pane
/// (whose watcher echo is suppressed). Before the fix only the first match
/// reacted, so the other pane stayed stale and a later save from it silently
/// overwrote the newer file.
/// </summary>
public class SameFileCopiesTests
{
    private static Document Copy(bool dirty) => new() { FilePath = @"C:\notes\plan.md", IsDirty = dirty };

    [Fact]
    public void SaveFromTopPane_ReloadsCleanBottomCopy()
    {
        var top = Copy(dirty: false);   // just saved
        var bottom = Copy(dirty: false); // stale, clean
        var plan = SameFileCopies.Plan(new[] { top, top, bottom }, savedBy: top);
        Assert.Equal(new[] { CopyAction.None, CopyAction.Reload }, plan.Select(p => p.Action));
        Assert.Same(bottom, plan[1].Copy);
    }

    [Fact]
    public void SaveFromTopPane_ConflictsDirtyBottomCopy_NeverReloadsOverIt()
    {
        var top = Copy(dirty: false);
        var bottom = Copy(dirty: true);
        var plan = SameFileCopies.Plan(new[] { top, bottom }, savedBy: top);
        Assert.Equal(CopyAction.Conflict, plan.Single(p => ReferenceEquals(p.Copy, bottom)).Action);
    }

    [Fact]
    public void ExternalChange_CleanTopDirtyBottom_BottomStillGetsConflict()
    {
        // The pre-fix path: first match was the clean top copy, so the dirty
        // bottom copy got no banner at all.
        var top = Copy(dirty: false);
        var bottom = Copy(dirty: true);
        var plan = SameFileCopies.Plan(new[] { top, bottom }, savedBy: null);
        Assert.Equal(new[] { CopyAction.Reload, CopyAction.Conflict }, plan.Select(p => p.Action));
    }

    [Fact]
    public void SameInstanceListedTwice_IsHandledOnce()
    {
        var shared = Copy(dirty: true);
        var plan = SameFileCopies.Plan(new[] { shared, shared }, savedBy: null);
        Assert.Single(plan);
        Assert.Equal(CopyAction.Conflict, plan[0].Action);
    }
}
