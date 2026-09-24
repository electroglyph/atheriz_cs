using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Tests.Features.Objects;

// Person normalization contract: free text collapses to a person key
// ("1"/"2"/"3"/"*"/"") through an explicit alias map. Unknown text falls
// back to "" (the any-tense/default path) — never a letter-eaten fragment
// ("past"→"p") or a dug-out digit ("2nd person"→"2").
[Collection("Ported")]
public sealed class PersonNormalizationTests
{
    [Theory]
    [InlineData("1", "am")]
    [InlineData("1st", "am")]
    [InlineData("2", "are")]
    [InlineData("2nd", "are")]
    [InlineData("3", "is")]
    [InlineData("3rd", "is")]
    [InlineData("*", "are")]
    [InlineData("pl", "are")]
    [InlineData("plural", "are")]
    [InlineData(" 2 ", "are")]
    [InlineData("2ND", "are")]
    public void VerbPresent_CanonicalPersons_Resolve(string person, string expected)
    {
        Assert.Equal(expected, Conjugate.VerbPresent("be", person));
    }

    [Theory]
    [InlineData("1", "was")]
    [InlineData("2", "were")]
    [InlineData("3", "was")]
    [InlineData("*", "were")]
    [InlineData("plural", "were")]
    public void VerbPast_CanonicalPersons_Resolve(string person, string expected)
    {
        Assert.Equal(expected, Conjugate.VerbPast("be", person));
    }

    [Theory]
    [InlineData("past")]
    [InlineData("present")]
    [InlineData("x")]
    [InlineData("s")]
    [InlineData("2nd person")]
    [InlineData("singular")]
    public void VerbPresent_UnknownPerson_FallsBackToDefault(string person)
    {
        Assert.Equal(Conjugate.VerbPresent("run", ""), Conjugate.VerbPresent("run", person));
        Assert.Equal(Conjugate.VerbPast("run", ""), Conjugate.VerbPast("run", person));
    }

    [Fact]
    public void VerbIsPresent_UnknownPerson_MatchesDefaultPath()
    {
        Assert.Equal(Conjugate.VerbIsPresent("are", ""), Conjugate.VerbIsPresent("are", "past"));
    }

    [Fact]
    public void VerbPresent_DugOutDigit_NoLongerResolves()
    {
        // "2nd person" used to yield "2" via digit extraction ("are");
        // a person key must be exact — this falls back to the infinitive.
        Assert.Equal("be", Conjugate.VerbPresent("be", "2nd person"));
    }
}
