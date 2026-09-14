using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Tests.Features.Objects;

// The tense tables fold case, so tense queries must too.
[Collection("Ported")]
public class VerbTenseCaseTests
{
    [Fact]
    public void VerbTense_IsCaseInsensitive()
    {
        Assert.Equal("3rd singular present", Conjugate.VerbTense("IS"));
        Assert.True(Conjugate.VerbIsPresent("IS", "3"));
        Assert.True(Conjugate.VerbIsPast("WAS", "1"));
        Assert.True(Conjugate.VerbIsTense("RUNNING", "present participle"));
    }
}
