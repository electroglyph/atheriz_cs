using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// DrawCommand (key "mapedit") must be reachable via the logged-in cmdset
// (cmdset.py:97); it was registered nowhere, so "mapedit" fell through.
[Collection("Ported")]
public class MapeditRegistrationTests
{
    [Fact]
    public void LoggedInCmdSet_ResolvesMapedit()
    {
        var cmd = CommandRegistry.LoggedIn.Get("mapedit");
        Assert.NotNull(cmd);
        Assert.IsType<DrawCommand>(cmd);
    }

    [Fact]
    public void Dispatch_Mapedit_ReachesDrawCommand()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("mapeditreg", 0, 0, 0));
            nh.AddNode(node);
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(node));
            builder.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(builder, "mapedit", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            // No session on a bare GameObject caller: DrawCommand itself ran
            // (not the none/auto-alias fallback).
            Assert.Contains("No active connection", string.Join("\n", builder.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
