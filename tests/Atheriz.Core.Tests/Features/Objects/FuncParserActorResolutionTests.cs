// Pins for the actor/gender resolution helpers (FuncParser.cs): the mapped-actor
// lookup treats absent keys, absent mappings, and non-object values as the
// fallback caller, and gender reads prefer IGenderProvider over Gender.
// The four null-actor ParsingError messages stay distinct per callable.
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class FuncParserActorResolutionTests
{
    private sealed class PluralProvider : GameObject, IGenderProvider
    {
        public PluralProvider()
        {
            Name = "swarm";
            Gender = "male";
        }

        public string? GetGender() => "plural";
    }

    private static FuncParser ActorParser() => new(FuncParser.ActorStanceCallables);

    [Fact]
    public void You_MappedKey_ResolvesMappedActor()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero");
        var villain = GameObject.Create("Villain");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["target"] = villain };

        Assert.Equal("you", parser.Parse("$you(target)", hero, villain, mapping)?.ToString());
    }

    [Fact]
    public void You_UnmappedKey_FallsBackToCaller()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero");
        var villain = GameObject.Create("Villain");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["other"] = villain };

        Assert.Equal("Hero", parser.Parse("$you(target)", hero, villain, mapping)?.ToString());
    }

    [Fact]
    public void You_NonObjectValuedKey_FallsBackToCaller()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero");
        var villain = GameObject.Create("Villain");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["target"] = "not-an-object" };

        Assert.Equal("Hero", parser.Parse("$you(target)", hero, villain, mapping)?.ToString());
    }

    [Fact]
    public void Your_MappedKey_ResolvesMappedActor()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero");
        var villain = GameObject.Create("Villain");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["target"] = villain };

        Assert.Equal("your", parser.Parse("$your(target)", hero, villain, mapping)?.ToString());
    }

    [Fact]
    public void Your_UnmappedKey_FallsBackToCaller()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero");
        var villain = GameObject.Create("Villain");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["other"] = villain };

        Assert.Equal("Hero's", parser.Parse("$your(target)", hero, villain, mapping)?.ToString());
    }

    [Fact]
    public void Conj_MappedKey_ResolvesMappedActor()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        var bob = GameObject.Create("Bob");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["tommy"] = bob };

        Assert.Equal("jumps", parser.Parse("$conj(jump, tommy)", alice, alice, mapping)?.ToString());
    }

    [Fact]
    public void Conj_UnmappedKey_FallsBackToCaller()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        var bob = GameObject.Create("Bob");
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["tommy"] = bob };

        Assert.Equal("jump", parser.Parse("$conj(jump, nobody)", alice, alice, mapping)?.ToString());
    }

    [Fact]
    public void PConj_MappedPluralKey_UsesPluralConjugation()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        var other = GameObject.Create("Other");
        var plural = GameObject.Create("Swarm");
        plural.Gender = "plural";
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["grp"] = plural };

        Assert.Equal("jump", parser.Parse("$pconj(jump, grp)", alice, other, mapping)?.ToString());
    }

    [Fact]
    public void PConj_MappedSingularKey_UsesThirdPerson()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        var other = GameObject.Create("Other");
        var male = GameObject.Create("Bob");
        male.Gender = "male";
        var parser = ActorParser();
        var mapping = new Dictionary<string, object?> { ["guy"] = male };

        Assert.Equal("jumps", parser.Parse("$pconj(jump, guy)", alice, other, mapping)?.ToString());
    }

    [Fact]
    public void Pron_GenderProvider_OverridesGenderProperty()
    {
        using var env = GlobalTestEnv.Enter();
        var swarm = new PluralProvider();
        var other = GameObject.Create("Other");
        var parser = ActorParser();

        Assert.Equal("they", parser.Parse("$pron(I)", swarm, other, null)?.ToString());
    }

    [Fact]
    public void PConj_GenderProvider_OverridesGenderProperty()
    {
        using var env = GlobalTestEnv.Enter();
        var swarm = new PluralProvider();
        var other = GameObject.Create("Other");
        var parser = ActorParser();

        Assert.Equal("jump", parser.Parse("$pconj(jump)", swarm, other, null)?.ToString());
    }

    [Fact]
    public void NullActor_ErrorsStayDistinctPerCallable()
    {
        using var env = GlobalTestEnv.Enter();
        var parser = ActorParser();

        var youEx = Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$you()", raiseErrors: true));
        var yourEx = Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$your()", raiseErrors: true));
        var conjEx = Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$conj(jump)", raiseErrors: true));

        Assert.Contains("$you", youEx.Message);
        Assert.Contains("$your", yourEx.Message);
        Assert.Contains("$conj", conjEx.Message);
        Assert.NotEqual(youEx.Message, yourEx.Message);
        Assert.NotEqual(youEx.Message, conjEx.Message);
    }
}
