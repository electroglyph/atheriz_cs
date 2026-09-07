using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// MoveTo must not run user leave/receive hooks while holding both location
// write locks: hook delegates can move things and take other locks, inverting
// the sort_locks order (GameObject.Move.cs:274-277 vs :308-353).
[Collection("Ported")]
public class MoveHookLockTests
{
    private sealed class LeaveGate
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Proceed = new(false);

        [Before]
        public bool Gate(GameObject? dest, string? exit)
        {
            Entered.Set();
            Proceed.Wait(TimeSpan.FromSeconds(5));
            return true;
        }
    }

    [Fact]
    public void MoveTo_ReleasesLocationLocksBeforeLeaveHooks()
    {
        // Correct: while a leave hook runs, neither location write lock is
        // held, so other threads can still enter them.
        ObjectRegistry.ClearAll();
        try
        {
            var oldNode = new Node(new Coord("limbo", 3, 3, 0));
            var newNode = new Node(new Coord("limbo", 4, 4, 0));
            var mover = GameObject.Create("mover");
            ObjectRegistry.AddObject(mover);
            Assert.True(mover.MoveTo(oldNode));

            var gate = new LeaveGate();
            var method = typeof(LeaveGate).GetMethod("Gate")!;
            var hook = (Delegate)method.CreateDelegate(typeof(Func<GameObject?, string?, bool>), gate);
            oldNode.InstallHook("at_pre_object_leave", hook);

            var moveTask = Task.Run(() => mover.MoveTo(newNode));
            try
            {
                Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(5)), "setup: leave hook never ran");

                // Destination lock must be free while the hook is parked in it.
                bool free = newNode.SyncRoot.TryEnterWriteLock(0);
                if (free) newNode.SyncRoot.ExitWriteLock();
                Assert.True(free, "MoveTo holds location locks while running leave hooks");
            }
            finally
            {
                gate.Proceed.Set();
                Assert.True(moveTask.Wait(TimeSpan.FromSeconds(5)), "MoveTo did not finish after hook release");
                Assert.True(moveTask.Result);
            }
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
