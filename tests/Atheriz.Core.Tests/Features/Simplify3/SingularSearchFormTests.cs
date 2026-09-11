// Pins for the FrozenSet membership table: singular-word search forms keep
// their established behavior through the real object search path — singular
// words never widen to plural matches, case variants still hit, and genuine
// plurals still expand.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class SingularSearchFormTests
{
    private static GameObject MakeBag(params string[] names)
    {
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        foreach (var n in names)
        {
            var o = GameObject.Create(n, isItem: true);
            ObjectRegistry.AddObject(o);
            bag.AddObject(o);
        }
        return bag;
    }

    [Fact]
    public void Search_SingularWord_ReturnsSingleMatch()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeBag("glass", "glass");

        // A singular word stays a required term: no plural widening, so one
        // hit even with two matching objects present.
        Assert.Single(bag.Search("glass"));
    }

    [Fact]
    public void Search_SingularWord_DoesNotMatchPluralForm()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeBag("bus", "buses");

        var res = bag.Search("bus");
        Assert.Single(res);
        Assert.Equal("bus", res[0].Name);
    }

    [Fact]
    public void Search_CaseVariantQuery_MatchesSingularWord()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeBag("glass", "bus");

        Assert.Single(bag.Search("GLASS"));
        var bus = bag.Search("Bus");
        Assert.Single(bus);
        Assert.Equal("bus", bus[0].Name);
    }

    [Fact]
    public void Search_PluralQuery_ExpandsToSingular()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeBag("photo", "photo");

        // Control: the genuine plural path still finds every singular form.
        Assert.Equal(2, bag.Search("photos").Count);
    }
}
