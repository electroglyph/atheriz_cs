using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Deleting an object of equal/higher privilege is refused (delete.py:92-94,
// _privilege_denied at set.py:58-70: self-exempt, target >= caller denied).
[Collection("Ported")]
public class DeletePrivilegeGateTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void Delete_HigherPrivilegeTarget_IsRefused()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("delpriv", 0, 0, 0));
            nh.AddNode(node);
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(node));
            var relic = GameObject.Create("relic", isItem: true, privilege: Privilege.Admin);
            ObjectRegistry.AddObject(relic);
            Assert.True(relic.MoveTo(node));
            builder.ClearMessages();
            RunJob(CommandDispatcher.DispatchLoggedIn(builder, "delete relic", immediate: true));
            Assert.Contains("equal or higher privilege", string.Join("\n", builder.PeekMessages()));
            Assert.NotEmpty(ObjectRegistry.Get(relic.Id));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Delete_LowerPrivilegeTarget_Proceeds()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("delpriv2", 0, 0, 0));
            nh.AddNode(node);
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            Assert.True(admin.MoveTo(node));
            var trinket = GameObject.Create("trinket", isItem: true);
            ObjectRegistry.AddObject(trinket);
            Assert.True(trinket.MoveTo(node));
            admin.ClearMessages();
            RunJob(CommandDispatcher.DispatchLoggedIn(admin, "delete trinket", immediate: true));
            Assert.DoesNotContain("equal or higher privilege", string.Join("\n", admin.PeekMessages()));
            Assert.Empty(ObjectRegistry.Get(trinket.Id));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
