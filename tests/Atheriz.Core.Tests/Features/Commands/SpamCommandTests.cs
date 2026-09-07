using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Python parity (spam.py): a zero-iteration run still rewrites the
// credentials file (header only) and reports "Created 0". Pinned here so a
// future guard preserves the report contract.
[Collection("Ported")]
public class SpamCommandTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void Spam_ZeroCount_ReportsCreatedZeroAndRewritesFile()
    {
        // Zero iterations: file truncated to header-only, "Created 0" reported.
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
            Assert.Contains("Created 0", msgs);
            Assert.DoesNotContain("SENTINEL-KEEP", File.ReadAllText(creds));
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }

    [Fact]
    public void Spam_NegativeCount_BehavesLikeZero()
    {
        // range(1, negative+1) is empty in Python: same header-only file +
        // "Created 0" report.
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
            Assert.Contains("Created 0", msgs);
            Assert.DoesNotContain("SENTINEL-KEEP", File.ReadAllText(creds));
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }
}
