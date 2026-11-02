using System.Diagnostics;
using System.Windows.Threading;
using System.Text;
using WorkNotes.Models;
using WorkNotes.Services;

namespace WorkNotes.Tests;

/// <summary>
/// External-file-monitor tests. FileSystemWatcher timing is nondeterministic
/// (it batches, delays, and drops events), so all semantics tests inject
/// watcher wake-ups through SimulateWatcherEvent on an inline-dispatch
/// monitor. Exactly one real-disk test confirms the wiring.
/// </summary>
public class ExternalFileMonitorTests : IDisposable
{
    // StreamWriter(Encoding.UTF8) emits a BOM — the exact byte layout
    // FileHelper.AtomicWriteText produces for our saves.
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly string _dir;

    public ExternalFileMonitorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "workpad-fsw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { }
    }

    /// <summary>Runs the settle check inline instead of via the dispatcher.</summary>
    private sealed class InlineMonitor : ExternalFileMonitor
    {
        public InlineMonitor() : base(Dispatcher.CurrentDispatcher) { }
        protected override void PostToUi(Action action) => action();
    }

    [Fact]
    public void FilePresent_AtSettle_RaisesChanged()
    {
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "a.txt");
        File.WriteAllText(path, "x");
        string? changed = null, deleted = null;
        m.FileChanged += p => changed = p;
        m.FileDeleted += p => deleted = p;

        m.SimulateWatcherEvent(path);

        Assert.Equal(path, changed);
        Assert.Null(deleted);
    }

    [Fact]
    public void FileAbsent_AtSettle_RaisesDeleted()
    {
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "gone.txt");
        string? changed = null, deleted = null;
        m.FileChanged += p => changed = p;
        m.FileDeleted += p => deleted = p;

        m.SimulateWatcherEvent(path);

        Assert.Null(changed);
        Assert.Equal(path, deleted);
    }

    [Fact]
    public void AtomicSave_LooksLikeChange_NotDeletion()
    {
        // VS Code / Notepad++ save via write-temp + rename/replace: the watcher
        // may surface it as delete+create or rename. The settle-time decision
        // resolves it to a change because the path exists afterwards.
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "b.txt");
        File.WriteAllText(path, "v1");
        var events = new List<string>();
        m.FileChanged += _ => events.Add("changed");
        m.FileDeleted += _ => events.Add("deleted");

        // Replace sequence: the path exists at settle → change, never deletion.
        File.WriteAllText(path, "v2");
        m.SimulateWatcherEvent(path); // the "deleted" half of a replace pair
        m.SimulateWatcherEvent(path); // the "created"/changed half

        Assert.DoesNotContain("deleted", events);
        Assert.Contains("changed", events);
    }

    [Fact]
    public void OwnSave_IsSuppressed()
    {
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "c.txt");
        File.WriteAllText(path, "x", Utf8Bom);
        var fired = false;
        m.FileChanged += _ => fired = true;
        m.FileDeleted += _ => fired = true;

        File.WriteAllText(path, "y", Utf8Bom);
        // fingerprint the bytes our save wrote, not a reread of the file
        m.BeginSelfWrite(path, FileHelper.Fingerprint("y", Encoding.UTF8));
        m.SimulateWatcherEvent(path);

        Assert.False(fired);
    }

    [Fact]
    public void OwnSave_EventBurst_AllSuppressed()
    {
        // One atomic save surfaces as multiple watcher events (Changed +
        // Renamed + Created). Every event in the window is suppressed — the
        // marker is not consumed by the first event.
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "d.txt");
        File.WriteAllText(path, "x", Utf8Bom);
        var events = new List<string>();
        m.FileChanged += _ => events.Add("changed");
        m.FileDeleted += _ => events.Add("deleted");

        File.WriteAllText(path, "y", Utf8Bom);
        m.BeginSelfWrite(path, FileHelper.Fingerprint("y", Encoding.UTF8));
        m.SimulateWatcherEvent(path);
        m.SimulateWatcherEvent(path);
        m.SimulateWatcherEvent(path);

        Assert.Empty(events);
    }

    [Fact]
    public void ExternalChange_AfterSelfWriteWindow_StillFires()
    {
        // The suppression window expires: a change arriving after it is real.
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "e.txt");
        File.WriteAllText(path, "x", Utf8Bom);
        var events = new List<string>();
        m.FileChanged += _ => events.Add("changed");

        m.BeginSelfWrite(path, FileHelper.Fingerprint("x", Encoding.UTF8));
        m.SimulateWatcherEvent(path);
        Thread.Sleep(1100); // past the 1s suppression window
        File.WriteAllText(path, "z", Utf8Bom);
        m.SimulateWatcherEvent(path);

        Assert.Contains("changed", events);
    }

    [Fact]
    public void ExternalEdit_InsideSelfWriteWindow_IsNotSuppressed()
    {
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "f.txt");
        File.WriteAllText(path, "ours", Utf8Bom);
        var fired = false;
        m.FileChanged += _ => fired = true;
        m.FileDeleted += _ => fired = true;

        m.BeginSelfWrite(path, FileHelper.Fingerprint("ours", Encoding.UTF8));
        File.WriteAllText(path, "theirs"); // coincident external edit
        m.SimulateWatcherEvent(path);

        Assert.True(fired);
    }

    public void Watch_Unwatch_TracksRegistration()
    {
        using var m = new InlineMonitor();
        var path = Path.Combine(_dir, "watched.txt");
        File.WriteAllText(path, "x");

        Assert.False(m.IsWatching(path));
        m.Watch(path);
        Assert.True(m.IsWatching(path));
        m.Unwatch(path);
        Assert.False(m.IsWatching(path));
    }

    /// <summary>The one real-disk test: a real FileSystemWatcher event on a
    /// watched path reaches FileChanged on the owning dispatcher.</summary>
    [Fact]
    public void RealWatcher_DeliversChange_OnRealDispatcher()
    {
        Exception? error = null;
        var gotChanged = 0;
        var signalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                var path = Path.Combine(_dir, "real.txt");
                File.WriteAllText(path, "x");
                var monitor = new ExternalFileMonitor(Dispatcher.CurrentDispatcher);
                monitor.FileChanged += _ => { Interlocked.Exchange(ref gotChanged, 1); signalled.TrySetResult(true); };
                monitor.Watch(path);

                File.WriteAllText(path, "y");

                // Pump the dispatcher until the (possibly batched) event settles.
                var frame = new DispatcherFrame();
                _ = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await signalled.Task.WaitAsync(cts.Token); }
                    catch { /* timeout falls through */ }
                    frame.Continue = false;
                });
                Dispatcher.PushFrame(frame);
                monitor.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
                signalled.TrySetResult(false);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));

        Assert.Null(error);
        Assert.True(Interlocked.CompareExchange(ref gotChanged, 0, 0) == 1,
            "FileSystemWatcher event did not reach FileChanged within 10s");
    }
}
