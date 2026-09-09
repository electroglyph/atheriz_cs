using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// message-dialect centralization — CommandHelpers message homes emit
// the exact verbatim strings the commands previously inlined.
// Touches process-global registry state: serialized with the Ported stream
// (flaky under cross-collection parallelism otherwise).
[Collection("Ported")]
public sealed class CommandMessageDialectTests
{
    private sealed class Recorder : Atheriz.Core.Commands.IMessageTarget
    {
        public readonly List<string> Texts = new();
        public void Msg(string text) => Texts.Add(text);
    }

    [Fact]
    public void Dialects_EmitVerbatimStrings()
    {
        var go = new Recorder();
        CommandHelpers.MsgNo(go);
        CommandHelpers.MsgNowhere(go);
        CommandHelpers.MsgNowhereExclaim(go);
        CommandHelpers.MsgInvalidLocation(go);
        CommandHelpers.MsgObjectNotFound(go);
        CommandHelpers.MsgNoMatchFound(go, "thing");
        CommandHelpers.MsgMultipleMatches(go, "thing");
        CommandHelpers.MsgMultipleMatchesColon(go, "thing");
        CommandHelpers.MsgMultipleMatchesFound(go, "thing");
        var texts = go.Texts;
        Assert.Contains("No.", texts);
        Assert.Contains("You are nowhere.", texts);
        Assert.Contains("You are nowhere!", texts);
        Assert.Contains("You have an invalid location.", texts);
        Assert.Contains("Object not found.", texts);
        Assert.Contains("No match found for 'thing'.", texts);
        Assert.Contains("Multiple matches for 'thing'.", texts);
        Assert.Contains("Multiple matches for 'thing':", texts);
        Assert.Contains("Multiple matches found for 'thing'.", texts);
    }

    [Fact]
    public void FormatMultipleMatchesIdList_MatchesPuppetForm()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("ann", isPc: true);
        var b = GameObject.Create("ann2", isPc: true);
        var s = CommandHelpers.FormatMultipleMatchesIdList(new[] { a, b });
        Assert.Equal($"Multiple matches: #{a.Id} ann, #{b.Id} ann2. Use #id to pick one.", s);
        Assert.Equal($"No match found for 'q'.", CommandHelpers.FormatNoMatchFound("q"));
    }
}
