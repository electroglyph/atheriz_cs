// Desc command on a Node location: the redundant Node type-test is gone, so
// setting a description writes straight through to the location.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class DescCommandTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (NodeHandler Nh, Node Node, GameObject Builder) EnterBuilderInNode(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        var builder = GameObject.Create("desc_builder", isPc: true, privilege: Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        Assert.True(builder.MoveTo(node));
        builder.ClearMessages();
        return (nh, node, builder);
    }

    [Fact]
    public void DescCommand_RunOnNodeLocation_UpdatesDesc()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, builder) = EnterBuilderInNode("descpinnode");
        try
        {
            RunJob(CommandDispatcher.DispatchLoggedIn(builder, "desc A quiet room", immediate: true));
            Assert.Equal("A quiet room", node.Desc);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
