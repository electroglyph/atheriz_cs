using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Pre-login typo fallback suggests only usable verbs: alias keys are not
// commands (GetAll, not GetKeys), hidden/inaccessible verbs stay quiet like
// the logged-in twin, and settings-disabled verbs are not offered (dispatch
// demotes them to none).
[Collection("Ported")]
public sealed class UnloggedNoneSuggestionGateTests
{
    private static string RunNone(TestConnection conn, string text)
    {
        var pa = new GameArgumentParser.ParsedArgs();
        pa["none"] = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        new NoneCommand().Run(conn, pa);
        return string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
    }

    [Fact]
    public void UnloggedNone_DoesNotSuggestAliasKeys()
    {
        using var env = GlobalTestEnv.Enter();
        var text = RunNone(new TestConnection(), "h");
        Assert.Contains("did you mean", text);
        Assert.DoesNotContain("\"?\"", text);
    }

    [Fact]
    public void UnloggedNone_DoesNotSuggestDisabledVerbs()
    {
        using var env = GlobalTestEnv.Enter();
        var g = AtherizSettings.Global;
        bool ogAccount = g.AccountCreationEnabled;
        g.AccountCreationEnabled = false;
        CommandDispatcher.SetSettings(new AtherizSettings { AccountCreationEnabled = false });
        try
        {
            var text = RunNone(new TestConnection(), "cretae");
            Assert.Contains("did you mean", text);
            Assert.DoesNotContain("\"create\"", text);
        }
        finally
        {
            g.AccountCreationEnabled = ogAccount;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
    }
}
