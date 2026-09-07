using System.Diagnostics;
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

            // Pin SyncRoot so Msg blocks at IsModified=true (Channel.cs:186)
            // while already inside lock (_histLock).
            ch.SyncRoot.EnterWriteLock();
            var msgTask = Task.Run(() => ch.Msg("hello"));
            try
            {
                // Wait until the Msg thread is inside the history lock.
                var sw = Stopwatch.StartNew();
                bool observedHeld = false;
                var spinner = new SpinWait();
                while (sw.Elapsed < TimeSpan.FromSeconds(5))
                {
                    if (msgTask.IsCompleted) break;
                    if (!Monitor.TryEnter(histLock)) { observedHeld = true; break; }
                    Monitor.Exit(histLock);
                    spinner.SpinOnce();
                }
                Assert.True(observedHeld, "setup: Msg thread never reached the history lock");

                // History lock must be acquirable while Msg waits on SyncRoot.
                bool free = Monitor.TryEnter(histLock, TimeSpan.FromMilliseconds(500));
                if (free) Monitor.Exit(histLock);
                Assert.True(free, "Channel.Msg holds _histLock while blocked on SyncRoot (ABBA with Delete)");
            }
            finally
            {
                ch.SyncRoot.ExitWriteLock();
                Assert.True(msgTask.Wait(TimeSpan.FromSeconds(5)), "Msg did not finish after SyncRoot release");
            }
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
