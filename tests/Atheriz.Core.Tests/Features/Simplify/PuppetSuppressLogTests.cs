using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Suppressed-log fan-out: every site catches Exception, logs only the message
// under its own byte-identical context prefix, and swallows — verified here
// for the Puppet tail (a throwing game hook still completes the puppet).
[Collection("Ported")]
public class PuppetSuppressLogTests
{
    private sealed class ThrowingPuppet : GameObject
    {
        public override void AtPuppet(GameObject caller) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Puppet_ThrowingHook_LogsContextPrefix_AndCompletes()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        ObjectRegistry.ClearAll();
        try
        {
            var conn = new TestConnection("suppress");
            var session = new Session(conn);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            var npc = new ThrowingPuppet();
            npc.Id = GameObject.GetNextId();
            npc.Name = "npc";
            ObjectRegistry.AddObject(npc);

            string log;
            using (var cap = new CaptureAtherizLog())
            {
                Assert.True(caller.Puppet(session, npc));
                log = cap.Read();
            }

            Assert.Contains("Suppressed GameObject.Puppet: boom", log);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }
}
