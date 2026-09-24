using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Glued single-character aliases ('hello) dispatch to the glued verb
// with the remainder as its text.
[Collection("Ported")]
public sealed class GluedAliasDispatchTests
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
