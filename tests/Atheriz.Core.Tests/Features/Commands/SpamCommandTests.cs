using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Non-positive spam counts must be rejected without wiping stored credentials (SpamCommand.cs:31-68).
[Collection("Ported")]
public class SpamCommandTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void Spam_ZeroCount_PreservesCredentialsFile()
    {
        // Zero must not truncate an existing credentials file nor report success.
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "spamdir");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        try
        {
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var creds = Path.Combine(tmp, "spam_accounts.txt");
            File.WriteAllText(creds, "SENTINEL-KEEP\n");
            var job = CommandDispatcher.DispatchLoggedIn(admin, "spam 0", immediate: true);
            RunJob(job);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("SENTINEL-KEEP", File.ReadAllText(creds));
            Assert.DoesNotContain("Created 0", msgs);
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }

    [Fact]
    public void Spam_NegativeCount_IsRejectedWithoutWipingFile()
    {
        // Negative counts run zero iterations yet still rewrite the file today.
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "spamneg");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        try
        {
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var creds = Path.Combine(tmp, "spam_accounts.txt");
            File.WriteAllText(creds, "SENTINEL-KEEP\n");
            var pa = new GameArgumentParser.ParsedArgs();
            pa["count"] = -5;
            new SpamCommand().Run(admin, pa);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("SENTINEL-KEEP", File.ReadAllText(creds));
            Assert.DoesNotContain("Created 0", msgs);
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }
}
