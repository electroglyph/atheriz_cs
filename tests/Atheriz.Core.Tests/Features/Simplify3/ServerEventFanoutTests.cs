using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The three lifecycle overloads fan out through one Fire core, preserving the
// [sender]-vs-[] params shape per arm: each event reaches only its own
// listeners, with the sender propagated or null.
[Collection("Ported")]
public class ServerEventFanoutTests
{
    private sealed class RecordingServerObject : GameObject
    {
        public List<(string Hook, object? Sender)> Calls { get; } = new();
        public override void AtServerStart(object? sender) => Calls.Add(("start", sender));
        public override void AtServerStop(object? sender) => Calls.Add(("stop", sender));
        public override void AtServerReload(object? sender) => Calls.Add(("reload", sender));
    }

    private sealed class StartHook
    {
        public List<object?> Seen { get; } = new();
        [Before]
        public void Call(object? sender) { Seen.Add(sender); }
    }

    private static RecordingServerObject AddRecorder()
    {
        var rec = new RecordingServerObject();
        rec.Id = GameObject.GetNextId();
        rec.Name = "fanout-recorder";
        ObjectRegistry.AddObject(rec);
        return rec;
    }

    [Fact]
    public void AtServerStart_WithSender_DispatchesStartOnlyWithSender()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var rec = AddRecorder();
            var sender = new object();
            ServerEvents.AtServerStart(sender);
            var calls = rec.Calls.ToList();
            Assert.Single(calls);
            Assert.Equal("start", calls[0].Hook);
            Assert.Same(sender, calls[0].Sender);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtServerStop_WithSender_DispatchesStopOnlyWithSender()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var rec = AddRecorder();
            var sender = new object();
            ServerEvents.AtServerStop(sender);
            var calls = rec.Calls.ToList();
            Assert.Single(calls);
            Assert.Equal("stop", calls[0].Hook);
            Assert.Same(sender, calls[0].Sender);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtServerReload_WithSender_DispatchesReloadOnlyWithSender()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var rec = AddRecorder();
            var sender = new object();
            ServerEvents.AtServerReload(sender);
            var calls = rec.Calls.ToList();
            Assert.Single(calls);
            Assert.Equal("reload", calls[0].Hook);
            Assert.Same(sender, calls[0].Sender);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void LifecycleHooks_WithoutSender_DispatchNullSenderPerEvent()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var rec = AddRecorder();
            ServerEvents.AtServerStart();
            ServerEvents.AtServerStop();
            ServerEvents.AtServerReload();
            var calls = rec.Calls.ToList();
            Assert.Equal(3, calls.Count);
            Assert.Equal("start", calls[0].Hook);
            Assert.Equal("stop", calls[1].Hook);
            Assert.Equal("reload", calls[2].Hook);
            Assert.All(calls, c => Assert.Null(c.Sender));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtServerStart_HookReceivesSenderArg()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("hook-target");
            ObjectRegistry.AddObject(obj);
            var hook = new StartHook();
            obj.InstallHook("at_server_start", hook.Call);
            var sender = new object();
            ServerEvents.AtServerStart(sender);
            Assert.Single(hook.Seen);
            Assert.Same(sender, hook.Seen[0]);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtServerStart_WithoutSender_SkipsSenderArityHook()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("hook-target-arity");
            ObjectRegistry.AddObject(obj);
            var hook = new StartHook();
            obj.InstallHook("at_server_start", hook.Call);
            ServerEvents.AtServerStart();
            Assert.Empty(hook.Seen);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
