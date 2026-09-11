// Pins for the shared Node log context (Node.cs): suppressed-error logs
// across ForContents, AtHear, and AtDelete carry the "Node" prefix and
// category.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class NodeLogContextTests
{
    private sealed class ThrowingHearer : GameObject
    {
        public ThrowingHearer()
        {
            Id = IdGenerator.GetUniqueId();
            Name = "throwing-hearer";
            CanHear = true;
        }

        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay) =>
            throw new InvalidOperationException("hear-boom");
    }

    private sealed class ThrowingMsger : GameObject
    {
        public ThrowingMsger()
        {
            Id = IdGenerator.GetUniqueId();
            Name = "throwing-msger";
        }

        public override void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null) =>
            throw new InvalidOperationException("msg-boom");
    }

    private static string Capture(Action action)
    {
        using var cap = new CaptureAtherizLog();
        action();
        return cap.Read();
    }

    [Fact]
    public void ForContents_ThrowingFunc_LogsNodeContext()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("logctx", 0, 0, 0));
            var listener = GameObject.Create("ear");
            ObjectRegistry.AddObject(listener);
            Assert.True(listener.MoveTo(node));

            string log = Capture(() => node.ForContents(_ => throw new InvalidOperationException("boom")));

            Assert.Contains("Suppressed Node.ForContents: boom", log);
            Assert.Contains("Node: Suppressed Node.ForContents", log);
            Assert.DoesNotContain("NoIdMarker", log);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }

    [Fact]
    public void AtHear_ThrowingListener_LogsNodeContext()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("logctx", 1, 1, 0));
            var emitter = GameObject.Create("emitter");
            ObjectRegistry.AddObject(emitter);
            var hearer = new ThrowingHearer();
            ObjectRegistry.AddObject(hearer);
            Assert.True(hearer.MoveTo(node));

            string log = Capture(() => node.AtHear(emitter, "a shout", "hello", 60.0, true));

            Assert.Contains("Suppressed Node.AtHear: hear-boom", log);
            Assert.Contains("Node: Suppressed Node.AtHear", log);
            Assert.DoesNotContain("NoIdMarker", log);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }

    [Fact]
    public void AtDelete_DeniedWithThrowingMsg_LogsNodeContext_AndVetoes()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("logctx", 2, 2, 0));
            ObjectRegistry.AddObject(node);
            node.AddLock("delete", _ => false);
            var caller = new ThrowingMsger();
            ObjectRegistry.AddObject(caller);

            string log = Capture(() => Assert.Null(node.Delete(caller, recursive: true)));

            Assert.Contains("Suppressed Node.AtDelete: msg-boom", log);
            Assert.Contains("Node: Suppressed Node.AtDelete", log);
            Assert.DoesNotContain("NoIdMarker", log);
            Assert.NotEmpty(ObjectRegistry.Get(node.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }
}
