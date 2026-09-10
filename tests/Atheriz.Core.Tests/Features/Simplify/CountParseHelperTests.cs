// Pins for the shared count parse (CommandHelpers.TryGetCount): per-caller
// min/default — spam keeps its established no-floor behavior (0 runs an
// empty loop with the count messages) while wander refuses non-positive.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class CountParseHelperTests
{
    private static GameArgumentParser.ParsedArgs SpamArgs(params string[] argv)
        => new SpamCommand().Parser!.ParseArgs(argv);

    [Fact]
    public void TryGetCount_IntPassesThrough_StringParses_OtherDefaults()
    {
        var pa = SpamArgs("7");
        Assert.True(CommandHelpers.TryGetCount(pa, "count", 0, null, out int count));
        Assert.Equal(7, count);
        Assert.True(CommandHelpers.TryGetCount(null, "count", 10, 1, out int dflt));
        Assert.Equal(10, dflt);
    }

    [Fact]
    public void TryGetCount_BelowMin_ReturnsFalse_AtOrAbove_True()
    {
        var zero = SpamArgs("0");
        Assert.True(CommandHelpers.TryGetCount(zero, "count", 0, null, out int spamCount));
        Assert.Equal(0, spamCount);
        Assert.False(CommandHelpers.TryGetCount(zero, "count", 10, 1, out _));
        // A pre-parsed negative int (bypasses flag-like argv tokenizing).
        var neg = new GameArgumentParser.ParsedArgs { ["count"] = -3 };
        Assert.False(CommandHelpers.TryGetCount(neg, "count", 10, 1, out _));
    }

    [Fact]
    public void Spam_ZeroCount_RunsEmptyLoop_WithCountMessages()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("Admin", privilege: Atheriz.Core.Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        admin.ClearMessages();
        new SpamCommand().Run(admin, SpamArgs("0"));
        var msgs = admin.PeekMessages();
        Assert.Contains(msgs, m => m == "Creating 0 accounts and characters...");
        Assert.Contains(msgs, m => m.StartsWith("Created 0 accounts/chars in ", StringComparison.Ordinal));
    }

    [Fact]
    public void Wander_NonPositiveCount_Refuses()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = GameObject.Create("Builder", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        builder.ClearMessages();
        new WanderCommand().Run(builder, new WanderCommand().Parser!.ParseArgs(["0"]));
        Assert.Contains(builder.PeekMessages(), m => m == "Count must be a positive number.");
    }
}
