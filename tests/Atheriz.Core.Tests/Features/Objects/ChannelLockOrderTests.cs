#pragma warning disable xUnit1031 // Thread-affine RWL held across the wait: awaiting would hop threads and break lock affinity (Msg_DoesNotHoldHistoryLockWhileWaitingOnSyncRoot).
using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Channel Msg must not hold the history lock while taking the object lock:
// Delete detaches under peer SyncRoot then takes the history lock, so the
// reverse order inside Msg is an ABBA deadlock (Channel.cs:182-188 vs :155-158).
[Collection("Ported")]
public class ChannelLockOrderTests
{
    [Fact]
    public void Msg_DoesNotHoldHistoryLockWhileWaitingOnSyncRoot()
    {
        // Correct: history append + dirty mark must not nest _histLock ->
        // SyncRoot, so a thread blocked in Msg never pins the history lock.
        ObjectRegistry.ClearAll();
        var ch = new Channel();
        try
        {
            ch.Id = 5001;
            ObjectRegistry.AddObject(ch);
            var histLock = (object)typeof(Channel)
                .GetField("_histLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ch)!;

            // Pin SyncRoot READ: shared with Msg's Name read, so Msg advances
            // to the dirty mark (Channel.cs IsModified runs outside the history
            // lock) and blocks there on the write upgrade. The entry must still
            // become visible: that proves Msg appended and RELEASED the history
            // lock before blocking. Under the old nesting (dirty mark inside
            // the lock), History deadlocks here.
            // NOTE: a write pin cannot be used — Msg reads Name first and
            // would block before ever reaching the history lock in both impls.
            ch.SyncRoot.EnterReadLock();
            // Real Thread for Msg: "blocked while SyncRoot is pinned" must
            // mean cross-thread blocking. A pooled wait may inline the worker
            // onto this thread (same verdict for the spinner either way, but
            // the blocking verdict would no longer prove anything).
            Exception? msgError = null;
            using var msgDone = new ManualResetEventSlim(false);
            var msgThread = new Thread(() =>
            {
                try { ch.Msg("hello"); }
                catch (Exception ex) { msgError = ex; }
                finally { msgDone.Set(); }
            })
            { IsBackground = true };
            msgThread.Start();
            try
            {
                var historyTask = Task.Run(() =>
                {
                    var spinner = new SpinWait();
                    while (true)
                    {
                        if (ch.History.Count > 0) return;
                        spinner.SpinOnce();
                    }
                });
                var historyWinner = Task.WhenAny(historyTask, Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
                Assert.True(historyWinner == historyTask,
                    "Msg entry never became visible: history lock is pinned while Msg waits");
                Assert.False(msgDone.IsSet,
                    "Msg finished while SyncRoot pinned (expected it blocked at the dirty mark)");

                // History lock must be acquirable while Msg waits on SyncRoot.
                bool free = Monitor.TryEnter(histLock, TimeSpan.FromMilliseconds(500));
                if (free) Monitor.Exit(histLock);
                Assert.True(free, "Channel.Msg holds _histLock while blocked on SyncRoot (ABBA with Delete)");
            }
            finally
            {
                ch.SyncRoot.ExitReadLock();
                Assert.True(msgDone.Wait(TimeSpan.FromSeconds(5)), "Msg did not finish after SyncRoot release");
                Assert.True(msgThread.Join(TimeSpan.FromSeconds(5)));
                Assert.Null(msgError);
            }
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
