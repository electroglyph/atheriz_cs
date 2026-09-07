using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Create inits per-instance cmdsets and fires at_create (base_obj.py:181-186),
// and the PC view lock tests only the target's connection (base_obj.py:164).
[Collection("Ported")]
public class CreateLifecycleTests
{
    [Fact]
    public void Create_InitsCmdSets_AndMarksModified()
    {
        // Per-instance cmdsets mirror base_obj.py:181 internal_cmdset /
        // external_cmdset init (needed by channel-command install).
        ObjectRegistry.ClearAll();
        try
        {
            var made = GameObject.Create("fresh");
            Assert.NotNull(made.InternalCmdSet);
            Assert.NotNull(made.ExternalCmdSet);
            Assert.True(made.IsModified);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void PcViewLock_TestsTargetConnectionOnly()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var target = GameObject.Create("target", isPc: true);
            ObjectRegistry.AddObject(target);
            target.IsConnected = false;
            var viewer = GameObject.Create("viewer", isPc: true);
            ObjectRegistry.AddObject(viewer);
            viewer.IsConnected = true;
            // Connected viewer, offline target: denied (old code granted).
            Assert.False(target.Access(viewer, "view"));
            target.IsConnected = true;
            Assert.True(target.Access(viewer, "view"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
