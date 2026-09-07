using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// `get all` must not sweep up view-locked items (get.py:112-117), and the
// pickup broadcast uses the take template with msg_type "get".
[Collection("Ported")]
public class GetAllViewLockTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void GetAll_SkipsViewLockedItem()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("getallview", 0, 0, 0));
            nh.AddNode(node);
            var getter = GameObject.Create("getter", isPc: true);
            ObjectRegistry.AddObject(getter);
            getter.IsConnected = true;
            Assert.True(getter.MoveTo(node));
            var plain = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(plain);
            Assert.True(plain.MoveTo(node));
            var hidden = GameObject.Create("cloak", isItem: true);
            ObjectRegistry.AddObject(hidden);
            hidden.AddLock("view", _ => false);
            Assert.True(hidden.MoveTo(node));
            getter.ClearMessages();
            RunJob(CommandDispatcher.DispatchLoggedIn(getter, "get all", immediate: true));
            Assert.Contains(plain.Id, getter.ContentsSnapshot);
            Assert.DoesNotContain(hidden.Id, getter.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
