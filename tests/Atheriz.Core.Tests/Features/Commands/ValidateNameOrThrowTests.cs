using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The throwing name validator returns the trimmed name when valid and
// throws the exact ValidateName message otherwise.
[Collection("Ported")]
public sealed class ValidateNameOrThrowTests
{
    [Fact]
    public void ValidateNameOrThrow_ValidName_ReturnsTrimmedName()
    {
        Assert.Equal("Alice", Validation.ValidateNameOrThrow("  Alice  ", 20));
        Assert.Equal("O'Brien", Validation.ValidateNameOrThrow("O'Brien", 30));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData("", 20)]
    [InlineData("ab", 20)]
    [InlineData("this name is way too long for sure", 10)]
    [InlineData("Bad!!Name", 20)]
    [InlineData("123", 20)]
    [InlineData("a  b", 20)]
    [InlineData("here", 20)]
    public void ValidateNameOrThrow_InvalidName_ThrowsExactValidateNameMessage(string? name, int maxLen)
    {
        string? expected = Validation.ValidateName(name, maxLen);
        Assert.NotNull(expected);
        var ex = Assert.Throws<ArgumentException>(() => Validation.ValidateNameOrThrow(name, maxLen));
        Assert.Equal(expected, ex.Message);
    }
}
