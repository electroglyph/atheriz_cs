using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// An unrecognized group subcommand falls through to a group message
// instead of erroring.
[Collection("Ported")]
public sealed class GroupUnknownSubcommandTests
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
}
