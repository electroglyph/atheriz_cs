using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Audit 10 finding 25: ForceClose must not hold the door write lock while
// taking NodeHandler Lock3. The remap thread holds Lock3 and then wants the
// door lock (RemapDoors' Lock3 -> door order); if ForceClose's per-write
// mark took Lock3 under the door hold, the remap's TryEnter times out.
// Neuter (mark under the hold again) makes gotDoor false.
[Collection("Ported")]
public sealed class DoorHandlerLockOrderTests
{
    [Fact]
    public void ForceClose_DoesNotHoldDoorLockWhileMarking()
    {
        using var _ = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var from = new Coord("pin25", 0, 0, 0);
        var to = new Coord("pin25", 0, 1, 0);
        var door = Door.Create(from, "north", to, "south", closed: false, locked: false);
        nh.AddDoor(door);

        using var lockHeld = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        bool gotDoor = false;
        var remap = new Thread(() =>
        {
            nh.Lock3.EnterWriteLock();
            try
            {
                lockHeld.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                // One attempt with a timeout: a transient outer hold (µs)
                // is absorbed, but a mark parked on Lock3 under the door
                // hold never lets go.
                gotDoor = door.Lock.TryEnterWriteLock(2000);
                if (gotDoor) door.Lock.ExitWriteLock();
            }
            finally { nh.Lock3.ExitWriteLock(); }
        })
        { IsBackground = true };
        remap.Start();
        Assert.True(lockHeld.Wait(TimeSpan.FromSeconds(5)));

        var closer = new Thread(() => door.ForceClose()) { IsBackground = true };
        closer.Start();
        Thread.Sleep(1000); // let ForceClose reach the post-release mark, parked on Lock3
        release.Set();
        Assert.True(closer.Join(TimeSpan.FromSeconds(15)));
        Assert.True(remap.Join(TimeSpan.FromSeconds(10)));

        Assert.True(gotDoor);
        Assert.True(door.Closed);
    }
}
