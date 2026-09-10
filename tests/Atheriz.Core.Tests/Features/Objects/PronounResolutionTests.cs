using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Tests.Features.Objects;

// Pronoun resolution across viewpoints, genders, and option spellings: the
// HashSet membership and typed table decide exactly what the linear scans
// and boxed table decided.
[Collection("Ported")]
public class PronounResolutionTests
{
    [Fact]
    public void FirstPerson_DefaultsToThirdNeutral()
    {
        var (s, o) = Pronouns.PronounToViewpoints("I");
        Assert.Equal("I", s);
        Assert.Equal("it", o);
    }

    [Fact]
    public void ObjectPronouns_MapByGender()
    {
        Assert.Equal("it", Pronouns.PronounToViewpoints("me").third);
        Assert.Equal("them", Pronouns.PronounToViewpoints("us").third);
        Assert.Equal("them", Pronouns.PronounToViewpoints("us", gender: "plural").third);
    }

    [Fact]
    public void ThirdToSecondPerson_ConvertsSpeakerSide()
    {
        var (s, o) = Pronouns.PronounToViewpoints("him", "2nd");
        Assert.Equal("you", s);
        Assert.Equal("him", o);
        Assert.Equal("you", Pronouns.PronounToViewpoints("he", "2nd").firstSecond);
        Assert.Equal("you", Pronouns.PronounToViewpoints("she", "2nd").firstSecond);
    }

    [Fact]
    public void PluralToFirstPerson_ConvertsSpeakerSide()
    {
        Assert.Equal("we", Pronouns.PronounToViewpoints("they", "1st").firstSecond);
        Assert.Equal("you", Pronouns.PronounToViewpoints("they", "2nd").firstSecond);
    }

    [Fact]
    public void AmbiguousPossessive_NarrowsByOption()
    {
        var (s, o) = Pronouns.PronounToViewpoints("her", "pa");
        Assert.Equal("your", s);
        Assert.Equal("her", o);
        var (s2, o2) = Pronouns.PronounToViewpoints("his", "pa");
        Assert.Equal("your", s2);
        Assert.Equal("his", o2);
    }

    [Fact]
    public void SingleType_NarrowOption_Applies()
    {
        Assert.Equal("it", Pronouns.PronounToViewpoints("it", "op").third);
        Assert.Equal("its", Pronouns.PronounToViewpoints("its").third);
        Assert.Equal("it", Pronouns.PronounToViewpoints("it").third);
    }

    [Fact]
    public void OptionsListForm_MatchesStringForm()
    {
        var fromString = Pronouns.PronounToViewpoints("him", "2nd");
        var fromList = Pronouns.PronounToViewpoints("him", new List<string> { "2nd" });
        Assert.Equal(fromString, fromList);
    }

    [Fact]
    public void CapitalizedInput_PreservesCase()
    {
        Assert.Equal("You", Pronouns.PronounToViewpoints("Her", "2nd").firstSecond);
    }

    [Fact]
    public void UnknownPronoun_PassesThrough()
    {
        Assert.Equal("xyzzy", Pronouns.PronounToViewpoints("xyzzy").firstSecond);
        Assert.Equal("xyzzy", Pronouns.PronounToViewpoints("xyzzy").third);
    }
}
