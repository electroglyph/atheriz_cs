// Pins for order-preserving choice dedup (CommandHelpers.TryAddChoice):
// insertion order — never set order — feeds BestMatch's stable tie-breaks,
// and runtime-added socials stay visible (no Aliases cache).
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class ChoiceDedupOrderTests
{
    [Fact]
    public void TryAddChoice_KeepsFirstInsertionOrder_SkipsDuplicates()
    {
        List<string> ordered = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        Assert.True(CommandHelpers.TryAddChoice(ordered, seen, "beta"));
        Assert.True(CommandHelpers.TryAddChoice(ordered, seen, "alpha"));
        Assert.False(CommandHelpers.TryAddChoice(ordered, seen, "beta"));
        Assert.Equal(["beta", "alpha"], ordered);
    }

    [Fact]
    public void NoneCommand_TiedLocalKeys_SuggestsFirstInserted()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("Seeker", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var internalCs = new CmdSet();
        internalCs.Add(new TieAlphaCommand());
        internalCs.Add(new TieBetaCommand());
        caller.InternalCmdSet = internalCs;
        // "aaaz" sits at distance 1 from both locals (and farther from every
        // real command key); the first-inserted wins the stable tie-break.
        new NoneCommand().Run(caller, new NoneCommand().Parser!.ParseArgs(["aaaz"]));
        var msg = string.Join("\n", caller.PeekMessages());
        Assert.Contains("did you mean", msg);
        Assert.Contains("aaax", msg);
    }

    [Fact]
    public void Socials_RuntimeAddedSocial_StaysVisibleInAliases()
    {
        using var env = GlobalTestEnv.Enter();
        const string key = "frobnicate-test-social";
        try
        {
            SocialsCommand.SocialsDict[key] = ("$You() $conj(frobnicate).", "$You() $conj(frobnicate) at $you(target).");
            Assert.Contains(key, new SocialsCommand().Aliases);
        }
        finally
        {
            SocialsCommand.SocialsDict.Remove(key);
        }
        Assert.DoesNotContain(key, new SocialsCommand().Aliases);
    }

    private sealed class TieAlphaCommand : Command
    {
        public override string Key => "aaax";
        public override string Desc => "tie alpha";
        protected override void SetupParser(GameArgumentParser p) { }
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class TieBetaCommand : Command
    {
        public override string Key => "aaay";
        public override string Desc => "tie beta";
        protected override void SetupParser(GameArgumentParser p) { }
        public override void Run(IMessageTarget caller, object? args) { }
    }
}
