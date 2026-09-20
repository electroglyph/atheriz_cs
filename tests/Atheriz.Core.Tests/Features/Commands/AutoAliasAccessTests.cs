using Atheriz.Core.Commands;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Near-miss alias resolution skips access-denied commands, so a prefix
// shared with a hidden command behaves exactly like gibberish.
[Collection("Ported")]
public sealed class AutoAliasAccessTests
{
    private sealed class AccessStubCommand : Command
    {
        private readonly string _key;
        private readonly bool _allow;
        public AccessStubCommand(string key, bool allow) { _key = key; _allow = allow; }
        public override string Key => _key;
        public override bool Access(IMessageTarget caller) => _allow;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void AutoAlias_SkipsAccessDeniedCandidate()
    {
        // Prefix "se" is shared by denied "secret" (sorts first) and
        // allowed "see": resolution must skip the denied candidate.
        using var env = GlobalTestEnv.Enter();
        var caller = PortedHelpers.MakeCaller("aliasu");
        var set = new CmdSet();
        var see = new AccessStubCommand("see", true);
        set.Add(new AccessStubCommand("secret", false));
        set.Add(see);
        var (cmd, alias) = CommandDispatcher.AutoAlias(set, "se", false, caller);
        Assert.Same(see, cmd);
        Assert.Equal("see", alias);
    }
}
