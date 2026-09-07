using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Shutdown must validate the token before running stop side effects (ShutdownCommand.cs:22 vs :27-31).
[Collection("Ported")]
public class ShutdownCommandTests
{
    private sealed class StopProbe
    {
        public bool Called;
        [Before] public void OnStop() { Called = true; }
    }

    [Fact]
    public void Shutdown_MissingToken_DoesNotRunStopHooks()
    {
        // A refused shutdown (no token file) must leave stop hooks unrun.
        using var env = GlobalTestEnv.Enter();
        var origSecret = AtherizSettings.Global.SecretPath;
        var emptyDir = Path.Combine(env.TempPath, "emptysecret");
        Directory.CreateDirectory(emptyDir);
        AtherizSettings.Global.SecretPath = emptyDir;
        try
        {
            ObjectRegistry.ClearAll();
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var probeObj = GameObject.Create("probe");
            ObjectRegistry.AddObject(probeObj);
            var probe = new StopProbe();
            probeObj.InstallHook("at_server_stop", new Action(probe.OnStop));
            admin.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(admin, "shutdown", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("admin.token not found", msgs);
            Assert.False(probe.Called);
        }
        finally
        {
            AtherizSettings.Global.SecretPath = origSecret;
            ObjectRegistry.ClearAll();
        }
    }
}
