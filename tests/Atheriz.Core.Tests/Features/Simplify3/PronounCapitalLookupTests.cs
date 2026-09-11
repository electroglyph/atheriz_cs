// Pins for the dead "I" special-case removal (Pronouns.cs): the
// case-insensitive table resolves "I" and "i" to the same entry, so only the
// echo casing differs while the mapped pronoun is identical.
using Atheriz.Core.Objects;
using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class PronounCapitalLookupTests
{
    [Fact]
    public void PronounToViewpoints_UppercaseI_MaleThirdPerson_ResolvesHe()
    {
        var (firstSecond, third) = Pronouns.PronounToViewpoints("I", null, null, "male", null);

        Assert.Equal("I", firstSecond);
        Assert.Equal("he", third);
    }

    [Fact]
    public void PronounToViewpoints_LowercaseI_ResolvesSameEntry()
    {
        var (_, upperThird) = Pronouns.PronounToViewpoints("I", null, null, "male", null);
        var (_, lowerThird) = Pronouns.PronounToViewpoints("i", null, null, "male", null);

        Assert.Equal(upperThird, lowerThird);
        Assert.Equal("he", lowerThird);
    }

    [Fact]
    public void PronounToViewpoints_UppercaseYou_ResolvesSameEntry()
    {
        var (firstSecond, third) = Pronouns.PronounToViewpoints("YOU", null, null, "male", null);

        Assert.Equal("YOU", firstSecond);
        Assert.Equal("HE", third);
    }

    [Fact]
    public void PronounToViewpoints_UnknownGender_FallsBackToSource()
    {
        var (firstSecond, third) = Pronouns.PronounToViewpoints("I", null, null, "bogus-gender", null);

        Assert.Equal("I", firstSecond);
        Assert.Equal("it", third);
    }

    [Fact]
    public void PronCall_UpperAndLowerI_BothMapToHe()
    {
        using var env = GlobalTestEnv.Enter();
        var male = GameObject.Create("Bob");
        male.Gender = "male";
        var other = GameObject.Create("Other");
        var parser = new FuncParser(FuncParser.ActorStanceCallables);

        Assert.Equal("he", parser.Parse("$pron(I)", male, other, null)?.ToString());
        Assert.Equal("he", parser.Parse("$pron(i)", male, other, null)?.ToString());
    }
}
