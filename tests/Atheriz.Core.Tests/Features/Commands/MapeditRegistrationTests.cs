using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

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

    [Fact]
    public void DrawCommand_WhenIpBucketFull_WarnsAndSendsNoLaunch()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("mapeditfull", 0, 0, 0));
            nh.AddNode(node);
            // The Draw tab needs its own socket: with the per-IP budget
            // already held, the grant could never connect, so the verb must
            // say so and send no launch instead of opening a dead tab.
            var mgr = PortedHelpers.MakeManager(new AtherizSettings { MaxConnectionsPerIp = 1 });
            var prev = ConnectionManager.GlobalInstance;
            ConnectionManager.GlobalInstance = mgr;
            try
            {
                var holder = new TestConnection();
                holder.ClientHost = "10.9.9.9";
                Assert.True(mgr.RegisterConnection("holder", holder));
                var caller = GameObject.Create("callerfull", isPc: true);
                ObjectRegistry.AddObject(caller);
                caller.PrivilegeLevel = Privilege.Builder;
                caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
                var conn = new FakeConnForFull("10.9.9.9");
                caller.Session = new Session(conn);
                caller.Session.Connection = conn;
                conn.Session.Puppet = caller;
                caller.ClearMessages();
                var draw = new DrawCommand();
                draw.Run(caller, null);
                Assert.Contains("Server is full", string.Join("\n", caller.PeekMessages()));
                Assert.DoesNotContain(conn.Sent, s => s.Cmd == "launch_draw");
            }
            finally
            {
                ConnectionManager.GlobalInstance = prev;
                mgr.Atp.Stop(wait: false);
            }
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); MapEdit.Reset(); }
    }

    [Fact]
    public void DrawCommand_WhenIpBucketHasRoom_LaunchesNormally()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("mapeditroom", 0, 0, 0));
            nh.AddNode(node);
            // One held socket under a budget of two leaves room for the Draw
            // tab: the full-bucket warning must not fire on a healthy server.
            var mgr = PortedHelpers.MakeManager(new AtherizSettings { MaxConnectionsPerIp = 2 });
            var prev = ConnectionManager.GlobalInstance;
            ConnectionManager.GlobalInstance = mgr;
            try
            {
                var holder = new TestConnection();
                holder.ClientHost = "10.8.8.8";
                Assert.True(mgr.RegisterConnection("holder", holder));
                var caller = GameObject.Create("callerroom", isPc: true);
                ObjectRegistry.AddObject(caller);
                caller.PrivilegeLevel = Privilege.Builder;
                caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
                var conn = new FakeConnForFull("10.8.8.8");
                caller.Session = new Session(conn);
                caller.Session.Connection = conn;
                conn.Session.Puppet = caller;
                var draw = new DrawCommand();
                draw.Run(caller, null);
                Assert.Contains(conn.Sent, s => s.Cmd == "launch_draw");
            }
            finally
            {
                ConnectionManager.GlobalInstance = prev;
                mgr.Atp.Stop(wait: false);
            }
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); MapEdit.Reset(); }
    }

    private sealed class FakeConnForFull : BaseConnection
    {
        public FakeConnForFull(string host) : base("test") { ClientHost = host; }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null) { lock (Sent) Sent.Add((cmd, args ?? new(), kwargs ?? new())); }
        public override void Close() { }
        public List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kw)> Sent = new();
    }
}
