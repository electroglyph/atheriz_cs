using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins for the section-4 Batch B refactors: shared #id resolution, keyword
// splits, channel-action precedence, the ExamFormatter tuple switch, group
// op dispatch, and the flattened dispatcher resolver chain.
[Collection("Ported")]
public class BatchBRefactorPinsTests
{
    private static GameObject MakeHero(string name = "hero")
    {
        ObjectRegistry.ClearAll();
        var hero = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(hero);
        hero.ClearMessages();
        return hero;
    }

    [Fact]
    public void IdTarget_BadFormat_ReportsUsage()
    {
        // The shared resolver rejects non-numeric #ids with the exact message
        // both Ban and Puppet used before the merge.
        var hero = MakeHero();
        try
        {
            var (target, err) = TargetResolution.ResolveById(hero, "#x");
            Assert.Null(target);
            Assert.Equal("Invalid ID format. Use #<number>.", err);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void IdTarget_UnknownId_ReportsNoObject()
    {
        // Unknown numeric ids report the id back verbatim.
        var hero = MakeHero();
        try
        {
            var (target, err) = TargetResolution.ResolveById(hero, "#299991");
            Assert.Null(target);
            Assert.Equal("No object found with ID 299991.", err);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void IdTarget_KnownId_ResolvesObject()
    {
        // A live id resolves to the object with no error.
        var hero = MakeHero();
        try
        {
            var (target, err) = TargetResolution.ResolveById(hero, $"#{hero.Id}");
            Assert.Null(err);
            Assert.Same(hero, target);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void KeywordSplit_FirstTo_WinsAndKeepsRest()
    {
        // Give's 'to' split takes the FIRST keyword case-insensitively and
        // keeps any later copies in the target half.
        Assert.True(TargetResolution.SplitOnFirstKeyword(
            ["sword", "TO", "bob", "to", "x"], out var before, out var after, "to"));
        Assert.Equal(["sword"], before);
        Assert.Equal(["bob", "to", "x"], after);
        Assert.False(TargetResolution.SplitOnFirstKeyword(
            ["sword"], out _, out _, "to", "from"));
    }

    private static ChannelAction ParseChannelAction(params string[] argv)
    {
        // Mirror ChannelCommand's full parser: common defs plus its own
        // -l/-c/-s extras (BaseChannelCommand has no list/subscribe flags).
        var p = new GameArgumentParser();
        ChannelActionParser.AddCommonArgs(p);
        p.AddArgument("-l", "--list").Help("List all channels").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--channel").Help("Channel to target");
        p.AddArgument("-s", "--subscribe").Help("Subscribe to channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        return ChannelActionParser.Parse(p.ParseArgs(argv));
    }

    [Fact]
    public void ChannelAction_ListBeatsUnsubscribe()
    {
        // -l wins over every other flag, matching the old triage order.
        Assert.Equal(ChannelAction.List, ParseChannelAction("-l", "-u"));
    }

    [Fact]
    public void ChannelAction_UnsubscribeBeatsSubscribeAndReplay()
    {
        // -u wins over -s and -r.
        Assert.Equal(ChannelAction.Unsubscribe, ParseChannelAction("-u", "-s", "-r"));
    }

    [Fact]
    public void ChannelAction_SubscribeBeatsReplay()
    {
        // -s wins over -r; a bare message (or nothing) is a send.
        Assert.Equal(ChannelAction.Subscribe, ParseChannelAction("-s", "-r"));
        Assert.Equal(ChannelAction.Replay, ParseChannelAction("-r"));
        Assert.Equal(ChannelAction.Send, ParseChannelAction("hello"));
        Assert.Equal(ChannelAction.Send, ParseChannelAction());
    }

    [Fact]
    public void ExamSwitch_LastTouchedBy_UsesTouchedRenderer()
    {
        // The or-pattern arm routes last_touched_by to the touched renderer.
        Assert.Equal("-1", ExamFormatter.FormatValue(-1, "last_touched_by") as string);
    }

    [Fact]
    public void ExamSwitch_ScriptsNonIds_FallThroughToGeneric()
    {
        // Non-id script members fall through to the generic list renderer.
        Assert.Equal("[x]", ExamFormatter.FormatValue(new List<string> { "x" }, "scripts") as string);
    }

    [Fact]
    public void ExamSwitch_SessionNull_RendersNone()
    {
        // The session arm renders a missing session as None.
        Assert.Equal("None", ExamFormatter.FormatValue(null, "session") as string);
    }

    [Fact]
    public void GroupOp_UnknownSub_SentAsGroupMessage()
    {
        // An unrecognized subcommand word is NOT an error: it falls through
        // to the message op and is sent to the group channel, exactly as the
        // old if-chain's fallthrough did.
        var hero = MakeHero();
        try
        {
            var chan = Channel.Create("b21typo");
            hero.GroupChannel = chan.Id;
            var before = chan.History.Count;
            var job = CommandDispatcher.DispatchLoggedIn(hero, "group frobnicate", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Equal(before + 1, chan.History.Count);
            Assert.Contains("frobnicate", chan.History);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Dispatcher_GluedQuoteAlias_SaysGluedText()
    {
        // The flattened chain still resolves a glued single-char alias: 'hi
        // reaches say with the remainder as its text.
        var hero = MakeHero();
        try
        {
            var job = CommandDispatcher.DispatchLoggedIn(hero, "'hello", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Contains("hello", string.Join("\n", hero.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
