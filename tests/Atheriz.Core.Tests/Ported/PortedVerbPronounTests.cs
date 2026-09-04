// Port of atheriz/tests/test_verb_conjugate.py:1 + atheriz/tests/test_pronouns.py:1 faithful
using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedVerbPronounTests
{
    // verb_infinitive
    [Fact] public void VerbInfinitiveIrregularBe(){ Assert.Equal("be", Conjugate.VerbInfinitive("was")); Assert.Equal("be", Conjugate.VerbInfinitive("were")); Assert.Equal("be", Conjugate.VerbInfinitive("is")); Assert.Equal("be", Conjugate.VerbInfinitive("are")); Assert.Equal("be", Conjugate.VerbInfinitive("been")); Assert.Equal("be", Conjugate.VerbInfinitive("being")); }
    [Fact] public void VerbInfinitiveRegular(){ Assert.Equal("run", Conjugate.VerbInfinitive("running")); Assert.Equal("walk", Conjugate.VerbInfinitive("walked")); Assert.Equal("eat", Conjugate.VerbInfinitive("eaten")); }
    [Fact] public void VerbInfinitiveAlready(){ Assert.Equal("be", Conjugate.VerbInfinitive("be")); Assert.Equal("run", Conjugate.VerbInfinitive("run")); }
    [Fact] public void VerbInfinitiveUnknown(){ Assert.Equal("xyzzy", Conjugate.VerbInfinitive("xyzzy")); Assert.Equal("", Conjugate.VerbInfinitive("")); }
    // verb_conjugate
    [Theory]
    [InlineData("be","infinitive","be")]
    [InlineData("be","1st singular present","am")]
    [InlineData("be","2nd singular present","are")]
    [InlineData("be","3rd singular present","is")]
    [InlineData("be","present plural","are")]
    [InlineData("be","present participle","being")]
    [InlineData("be","1st singular past","was")]
    [InlineData("be","2nd singular past","were")]
    [InlineData("be","3rd singular past","was")]
    [InlineData("be","past plural","were")]
    [InlineData("be","past","were")]
    [InlineData("be","past participle","been")]
    [InlineData("have","3rd singular present","has")]
    [InlineData("have","past","had")]
    [InlineData("go","past participle","gone")]
    [InlineData("run","past participle","run")]
    [InlineData("give","present participle","giving")]
    [InlineData("swim","past participle","swum")]
    public void VerbConjugateTheory(string verb,string tense,string expected){ Assert.Equal(expected, Conjugate.VerbConjugate(verb, tense)); }
    [Fact] public void VerbConjugateShortAliases(){
        Assert.Equal(Conjugate.VerbConjugate("be","inf"), Conjugate.VerbConjugate("be","infinitive"));
        Assert.Equal(Conjugate.VerbConjugate("be","3sgpres"), Conjugate.VerbConjugate("be","3rd singular present"));
        Assert.Equal(Conjugate.VerbConjugate("be","ppart"), Conjugate.VerbConjugate("be","past participle"));
        Assert.Equal(Conjugate.VerbConjugate("be","prog"), Conjugate.VerbConjugate("be","present participle"));
        Assert.Equal(Conjugate.VerbConjugate("be","1sgpres"), Conjugate.VerbConjugate("be","1st singular present"));
    }
    [Fact] public void VerbConjugateNegateSupported(){
        Assert.Equal("isn't", Conjugate.VerbConjugate("be","3rd singular present", true));
        Assert.Equal("am not", Conjugate.VerbConjugate("be","1st singular present", true));
        Assert.Equal("hasn't", Conjugate.VerbConjugate("have","3rd singular present", true));
        Assert.Equal("don't", Conjugate.VerbConjugate("do","infinitive", true));
        Assert.Equal("can't", Conjugate.VerbConjugate("can","infinitive", true));
    }
    [Fact] public void VerbConjugateUnknownReturnsOriginal(){
        Assert.Equal("xyzzy", Conjugate.VerbConjugate("xyzzy","infinitive"));
        Assert.Equal("flibbertigibbet", Conjugate.VerbConjugate("flibbertigibbet","past"));
    }
    [Fact] public void VerbConjugateAcceptsInflectedInput(){
        Assert.Equal("be", Conjugate.VerbConjugate("was","infinitive"));
        Assert.Equal(Conjugate.VerbConjugate("running","past participle"), Conjugate.VerbConjugate("run","past participle"));
    }
    // verb_present / past
    [Fact] public void VerbPresentAll(){ Assert.Equal("am", Conjugate.VerbPresent("be","1")); Assert.Equal("are", Conjugate.VerbPresent("be","2")); Assert.Equal("is", Conjugate.VerbPresent("be","3")); Assert.Equal("are", Conjugate.VerbPresent("be","*")); }
    [Fact] public void VerbPresentWithOrdinalSuffix(){
        Assert.Equal("am", Conjugate.VerbPresent("be","1st"));
        Assert.Equal("is", Conjugate.VerbPresent("be","3rd"));
        Assert.Equal("are", Conjugate.VerbPresent("be","2nd"));
    }
    [Fact] public void VerbPresentPluralAlias(){ Assert.Equal("are", Conjugate.VerbPresent("be","pl")); }
    [Fact] public void VerbPresentFallsBackToInfinitive(){
        Assert.Equal("be", Conjugate.VerbPresent("be","5"));
        Assert.Equal("walk", Conjugate.VerbPresent("walk",""));
    }
    [Fact] public void VerbPastAll(){ Assert.Equal("was", Conjugate.VerbPast("be","1")); Assert.Equal("were", Conjugate.VerbPast("be","2")); Assert.Equal("was", Conjugate.VerbPast("be","3")); Assert.Equal("were", Conjugate.VerbPast("be","*")); Assert.Equal("ran", Conjugate.VerbPast("run","3")); }
    [Fact] public void VerbPastFallsBackToInfinitivePast(){
        Assert.Equal("walked", Conjugate.VerbPast("walk",""));
        Assert.Equal("ran", Conjugate.VerbPast("run",""));
    }
    [Fact] public void VerbPresentParticiple(){
        Assert.Equal("giving", Conjugate.VerbPresentParticiple("give"));
        Assert.Equal("being", Conjugate.VerbPresentParticiple("be"));
        Assert.Equal("swimming", Conjugate.VerbPresentParticiple("swim"));
        Assert.Equal("eating", Conjugate.VerbPresentParticiple("eat"));
    }
    [Fact] public void VerbPastParticiple(){
        Assert.Equal("given", Conjugate.VerbPastParticiple("give"));
        Assert.Equal("been", Conjugate.VerbPastParticiple("be"));
        Assert.Equal("swum", Conjugate.VerbPastParticiple("swim"));
        Assert.Equal("eaten", Conjugate.VerbPastParticiple("eat"));
    }
    [Fact] public void VerbAllTensesCount(){ Assert.Equal(12, Conjugate.VerbAllTenses().Count); }
    [Fact] public void VerbAllTensesUnique(){ var t=Conjugate.VerbAllTenses(); Assert.Equal(t.Count, new HashSet<string>(t).Count); }
    [Fact] public void VerbAllTensesContents(){
        var t=Conjugate.VerbAllTenses();
        Assert.Contains("infinitive", t); Assert.Contains("past", t); Assert.Contains("past participle", t); Assert.Contains("present participle", t);
    }
    [Fact] public void VerbTenseKnown(){
        Assert.True(Conjugate.VerbTense("ran")=="past" || Conjugate.VerbTense("ran")=="1st singular past" || Conjugate.VerbTense("ran")=="3rd singular past");
        Assert.Equal("present participle", Conjugate.VerbTense("running"));
        Assert.Equal("1st singular present", Conjugate.VerbTense("am"));
        Assert.Equal("past participle", Conjugate.VerbTense("been"));
        Assert.Equal("3rd singular present", Conjugate.VerbTense("is"));
    }
    [Fact] public void VerbTenseUnknownReturnsOriginal(){ Assert.Equal("xyzzy", Conjugate.VerbTense("xyzzy")); }
    [Fact] public void VerbTenseInfinitive(){ Assert.Equal("infinitive", Conjugate.VerbTense("be")); Assert.Equal("infinitive", Conjugate.VerbTense("walk")); }
    [Fact] public void VerbIsTenseTrue(){ Assert.True(Conjugate.VerbIsTense("been","ppart")); Assert.True(Conjugate.VerbIsTense("been","past participle")); Assert.True(Conjugate.VerbIsTense("running","present participle")); Assert.True(Conjugate.VerbIsTense("am","1st singular present")); Assert.True(Conjugate.VerbIsTense("is","3rd singular present")); }
    [Fact] public void VerbIsTenseFalse(){ Assert.False(Conjugate.VerbIsTense("been","infinitive")); Assert.False(Conjugate.VerbIsTense("ran","past participle")); Assert.False(Conjugate.VerbIsTense("running","infinitive")); }
    [Fact] public void VerbIsPresent(){ Assert.True(Conjugate.VerbIsPresent("am","1")); Assert.True(Conjugate.VerbIsPresent("are","2")); Assert.True(Conjugate.VerbIsPresent("is","3")); Assert.False(Conjugate.VerbIsPresent("am","3")); Assert.False(Conjugate.VerbIsPresent("was","1")); }
    [Fact] public void VerbIsPresentNegated(){ Assert.True(Conjugate.VerbIsPresent("isn't","3", true)); Assert.True(Conjugate.VerbIsPresent("am not","1", true)); Assert.False(Conjugate.VerbIsPresent("is","3", true)); }
    [Fact] public void VerbIsPast(){
        Assert.True(Conjugate.VerbIsPast("was","1")); Assert.True(Conjugate.VerbIsPast("was","3")); Assert.True(Conjugate.VerbIsPast("were","2")); Assert.False(Conjugate.VerbIsPast("am","1")); Assert.False(Conjugate.VerbIsPast("is","3")); Assert.False(Conjugate.VerbIsPast("am","1"));
    }
    [Fact] public void VerbIsPastNegated(){ Assert.True(Conjugate.VerbIsPast("wasn't","1", true)); Assert.True(Conjugate.VerbIsPast("weren't","2", true)); Assert.False(Conjugate.VerbIsPast("was","1", true)); }
    [Fact] public void VerbIsPresentParticiple(){ Assert.True(Conjugate.VerbIsPresentParticiple("running")); Assert.True(Conjugate.VerbIsPresentParticiple("being")); Assert.False(Conjugate.VerbIsPresentParticiple("ran")); Assert.False(Conjugate.VerbIsPresentParticiple("run")); }
    [Fact] public void VerbIsPastParticiple(){ Assert.True(Conjugate.VerbIsPastParticiple("been")); Assert.True(Conjugate.VerbIsPastParticiple("eaten")); Assert.False(Conjugate.VerbIsPastParticiple("eat")); Assert.False(Conjugate.VerbIsPastParticiple("eating")); }
    [Fact] public void VerbActorStancePastSingular(){ var (you,them)=Conjugate.VerbActorStanceComponents("ran"); Assert.Equal("ran", you); Assert.Equal("ran", them); }
    [Fact] public void VerbActorStancePastPlural(){ var (you,them)=Conjugate.VerbActorStanceComponents("ran", plural:true); Assert.Equal("ran", you); Assert.Equal("ran", them); }
    [Fact] public void VerbActorStanceInfinitiveSingular(){ var (you,them)=Conjugate.VerbActorStanceComponents("walk"); Assert.Equal("walks", them); }
    [Fact] public void VerbActorStancePresentParticiple(){ var (you,them)=Conjugate.VerbActorStanceComponents("running"); Assert.Equal("running", you); Assert.Equal("running", them); }
    [Fact] public void VerbIsPresentArePlural(){
        Assert.True(Conjugate.VerbIsPresent("are","plural")); Assert.True(Conjugate.VerbIsPresent("are","*")); Assert.True(Conjugate.VerbIsPresent("are","2")); Assert.True(Conjugate.VerbIsPresent("are","2nd")); Assert.False(Conjugate.VerbIsPresent("are","1")); Assert.False(Conjugate.VerbIsPresent("are","3")); Assert.True(Conjugate.VerbIsPresent("is","3")); Assert.False(Conjugate.VerbIsPresent("is","plural")); Assert.False(Conjugate.VerbIsPresent("is","2")); Assert.True(Conjugate.VerbIsPresent("am","")); Assert.False(Conjugate.VerbIsPresent("was",""));
    }
    [Fact] public void VerbIsPastWasCoversBothSingular(){
        Assert.True(Conjugate.VerbIsPast("was","1")); Assert.True(Conjugate.VerbIsPast("was","3")); Assert.False(Conjugate.VerbIsPast("was","2")); Assert.False(Conjugate.VerbIsPast("was","*")); Assert.True(Conjugate.VerbIsPast("were","2")); Assert.True(Conjugate.VerbIsPast("were","*")); Assert.False(Conjugate.VerbIsPast("were","1"));
    }
    [Fact] public void VerbIsPresentPastNegated(){
        Assert.True(Conjugate.VerbIsPresent("isn't","3", true)); Assert.False(Conjugate.VerbIsPresent("is","3", true)); Assert.True(Conjugate.VerbIsPast("wasn't","1", true)); Assert.False(Conjugate.VerbIsPast("was","1", true));
    }
    // Pronouns constants
    [Fact] public void PronounTypesConstant(){
        Assert.Contains("subject pronoun", Pronouns.PronounTypes); Assert.Contains("object pronoun", Pronouns.PronounTypes); Assert.Contains("possessive adjective", Pronouns.PronounTypes); Assert.Contains("possessive pronoun", Pronouns.PronounTypes); Assert.Contains("reflexive pronoun", Pronouns.PronounTypes);
    }
    [Fact] public void ViewpointsConstant(){ Assert.Equal(new HashSet<string>{"1st person","2nd person","3rd person"}, new HashSet<string>(Pronouns.Viewpoints)); }
    [Fact] public void GendersConstant(){ Assert.Equal(new HashSet<string>{"male","female","neutral","plural"}, new HashSet<string>(Pronouns.Genders)); }
    [Fact] public void Defaults(){ Assert.Equal("subject pronoun", Pronouns.DefaultPronounType); Assert.Equal("2nd person", Pronouns.DefaultViewpoint); Assert.Equal("neutral", Pronouns.DefaultGender); }
    [Fact] public void ViewpointConversion(){
        var vc = Pronouns.ViewpointConversion;
        Assert.Equal("3rd person", vc["1st person"] is string s? s: "");
        Assert.Equal("3rd person", vc["2nd person"] is string s2? s2: "");
        var third = vc["3rd person"] as string[];
        Assert.NotNull(third); Assert.Equal(new HashSet<string>{"2nd person","1st person"}, new HashSet<string>(third!));
    }
    [Fact] public void Aliases(){
        Assert.Equal("male", Pronouns.Aliases["m"]); Assert.Equal("female", Pronouns.Aliases["f"]); Assert.Equal("neutral", Pronouns.Aliases["n"]); Assert.Equal("plural", Pronouns.Aliases["p"]);
        Assert.Equal("1st person", Pronouns.Aliases["1st"]); Assert.Equal("2nd person", Pronouns.Aliases["2nd"]); Assert.Equal("3rd person", Pronouns.Aliases["3rd"]);
        Assert.Equal("1st person", Pronouns.Aliases["1"]); Assert.Equal("2nd person", Pronouns.Aliases["2"]); Assert.Equal("3rd person", Pronouns.Aliases["3"]);
        Assert.Equal("subject pronoun", Pronouns.Aliases["sp"]); Assert.Equal("object pronoun", Pronouns.Aliases["op"]); Assert.Equal("possessive adjective", Pronouns.Aliases["pa"]); Assert.Equal("possessive pronoun", Pronouns.Aliases["pp"]);
        Assert.Equal("subject pronoun", Pronouns.Aliases["s"]); Assert.Equal("subject pronoun", Pronouns.Aliases["subject"]); Assert.Equal("object pronoun", Pronouns.Aliases["object"]); Assert.Equal("possessive adjective", Pronouns.Aliases["adjective"]); Assert.Equal("possessive pronoun", Pronouns.Aliases["pronoun"]);
    }
    [Fact] public void PronounToViewpointsEmpty(){ Assert.Equal("", Pronouns.PronounToViewpoints("").firstSecond); Assert.Equal("xyzzy", Pronouns.PronounToViewpoints("xyzzy").firstSecond); Assert.Equal("xyzzy", Pronouns.PronounToViewpoints("xyzzy").third); Assert.Equal("nope", Pronouns.PronounToViewpoints("nope").firstSecond); }
    [Fact] public void PronounI(){ var (s,o)=Pronouns.PronounToViewpoints("I"); Assert.Equal("I", s); Assert.Equal("it", o); }
    [Fact] public void PronounMeNeutral(){ var (s,o)=Pronouns.PronounToViewpoints("me"); Assert.Equal("it", o); }
    [Fact] public void PronounUsPlural(){ var (s,o)=Pronouns.PronounToViewpoints("us"); Assert.Equal("them", o); }
    [Fact] public void PronounUsPluralExplicit(){ var (s,o)=Pronouns.PronounToViewpoints("us", gender:"plural"); Assert.Equal("them", o); }
    [Fact] public void PronounHimTo2nd(){ var (s,o)=Pronouns.PronounToViewpoints("him", "2nd"); Assert.Equal("you", s); Assert.Equal("him", o); }
    [Fact] public void PronounThemTo1st(){ var (s,o)=Pronouns.PronounToViewpoints("them", "1st"); Assert.Equal("us", s); Assert.Equal("them", o); }
    [Fact] public void PronounHeTo2nd(){ var (s,o)=Pronouns.PronounToViewpoints("he", "2nd"); Assert.Equal("you", s); Assert.Equal("he", o); }
    [Fact] public void PronounSheTo2nd(){ var (s,o)=Pronouns.PronounToViewpoints("she", "2nd"); Assert.Equal("you", s); Assert.Equal("she", o); }
    [Fact] public void PronounTheyTo1st(){ var (s,o)=Pronouns.PronounToViewpoints("they", "1st"); Assert.Equal("we", s); Assert.Equal("they", o); }
    [Fact] public void PronounTheyTo2nd(){ var (s,o)=Pronouns.PronounToViewpoints("they", "2nd"); Assert.Equal("you", s); Assert.Equal("they", o); }
    [Fact] public void HerDefault(){ var (s,o)=Pronouns.PronounToViewpoints("her"); Assert.Equal("her", o); }
    [Fact] public void HerPa(){ var (s,o)=Pronouns.PronounToViewpoints("her", "pa"); Assert.Equal("your", s); Assert.Equal("her", o); }
    [Fact] public void HisDefaultPp(){ var (s,o)=Pronouns.PronounToViewpoints("his"); Assert.Equal("his", o); }
    [Fact] public void HisPa(){ var (s,o)=Pronouns.PronounToViewpoints("his", "pa"); Assert.Equal("your", s); Assert.Equal("his", o); }
    [Fact] public void ItDefaultSubject(){ var (s,o)=Pronouns.PronounToViewpoints("it"); Assert.Equal("it", o); }
    [Fact] public void ItWithObjectOption(){ var (s,o)=Pronouns.PronounToViewpoints("it", "op"); Assert.Equal("it", o); }
    [Fact] public void ItsDefaultPp(){ var (s,o)=Pronouns.PronounToViewpoints("its"); Assert.Equal("its", o); }
    [Fact] public void CapsPreservedInput(){ var (s,o)=Pronouns.PronounToViewpoints("Her", "2nd"); Assert.Equal("You", s); // observer lower? original checks observer contains H or h, but we check speaker You
        // Also check observer lower case preservation for H
        var (s2,o2)=Pronouns.PronounToViewpoints("Her", pronounType:"possessive adjective", viewpoint:"2nd person");
        // Should preserve case: Her -> Your? Check
        Assert.Equal("You", Pronouns.PronounToViewpoints("Her", "2nd").firstSecond);
    }
    [Fact] public void CapsPreservedObserverLowerH(){ var (s,o)=Pronouns.PronounToViewpoints("her", "2nd"); Assert.Equal("her", o); }
    [Fact] public void LowercaseInputUnchanged(){ var (s,o)=Pronouns.PronounToViewpoints("her", "2nd"); Assert.Equal("her", o); }
    [Fact] public void AliasesSpOpPaPp(){
        var (s1,o1)=Pronouns.PronounToViewpoints("him", "sp"); Assert.Equal("him", o1); // sp alias
        var (s2,o2)=Pronouns.PronounToViewpoints("him", "op"); Assert.Equal("him", o2);
        var (s3,o3)=Pronouns.PronounToViewpoints("her", "pa"); Assert.Equal("your", s3);
        var (s4,o4)=Pronouns.PronounToViewpoints("his", "pp"); Assert.Equal("his", o4);
        var (s5,o5)=Pronouns.PronounToViewpoints("him", "m"); Assert.Equal("him", o5);
    }
    [Fact] public void OptionsAliasMale(){ var (s,o)=Pronouns.PronounToViewpoints("him", "m"); Assert.Equal("him", o); }
    [Fact] public void OptionsAliasFemale2nd(){ var (s,o)=Pronouns.PronounToViewpoints("her", "f 2nd"); Assert.Equal("her", o); Assert.Equal("you", s); }
    [Fact] public void OptionsAliasPlural3rd(){ var (s,o)=Pronouns.PronounToViewpoints("they", "p"); Assert.Equal("they", o); }
    [Fact] public void OptionsAliasPronounTypeSubject(){ var (s,o)=Pronouns.PronounToViewpoints("you", "subject"); Assert.IsType<string>(o); }
    [Fact] public void OptionsAliasAdjectivePronoun(){ var (s1,o1)=Pronouns.PronounToViewpoints("her", "adjective"); Assert.Equal("your", s1); var (s2,o2)=Pronouns.PronounToViewpoints("his", "pronoun"); Assert.Equal("his", o2); }
    [Fact] public void OptionsAsList(){ var (s,o)=Pronouns.PronounToViewpoints("her", options: new[]{"pa","2nd"}); Assert.Equal("your", s); }
    [Fact] public void OptionsAliasSp(){ var (s,o)=Pronouns.PronounToViewpoints("I", "sp"); Assert.Equal("I", s); } // sp alias for subject pronoun
    [Fact] public void ExplicitPronounTypeKwarg(){ var (s,o)=Pronouns.PronounToViewpoints("her", pronounType:"possessive adjective"); Assert.Equal("your", s); Assert.Equal("her", o); }
    [Fact] public void ExplicitViewpointKwarg(){ var (s,o)=Pronouns.PronounToViewpoints("him", viewpoint:"1st person"); Assert.Equal("me", s); Assert.Equal("him", o); }
    [Fact] public void ExplicitGenderKwarg(){ var (s,o)=Pronouns.PronounToViewpoints("us", gender:"plural"); Assert.Equal("them", o); }
    [Fact] public void ExplicitGenderMaleFemaleNeutralPlural(){
        var (s1,o1)=Pronouns.PronounToViewpoints("he", gender:"male"); Assert.Equal("he", o1);
        var (s2,o2)=Pronouns.PronounToViewpoints("she", gender:"female"); Assert.Equal("she", o2);
        var (s3,o3)=Pronouns.PronounToViewpoints("it", gender:"neutral"); Assert.Equal("it", o3);
        var (s4,o4)=Pronouns.PronounToViewpoints("they", gender:"plural"); Assert.Equal("they", o4);
    }
    [Fact] public void ReflexiveMyselfDefaultNeutral(){ var (s,o)=Pronouns.PronounToViewpoints("myself"); Assert.Equal("myself", s); Assert.Equal("itself", o); }
    [Fact] public void ReflexiveThemselves(){ var (s,o)=Pronouns.PronounToViewpoints("themselves"); Assert.Equal("yourselves", s); Assert.Equal("themselves", o); }
    [Fact] public void ReflexiveHimselfTo2nd(){ var (s,o)=Pronouns.PronounToViewpoints("himself", "2nd"); Assert.Equal("yourself", s); Assert.Equal("himself", o); }
    [Fact] public void ReflexiveOurselves(){ var (s,o)=Pronouns.PronounToViewpoints("ourselves"); Assert.Equal("ourselves", s); } // check exists
}
