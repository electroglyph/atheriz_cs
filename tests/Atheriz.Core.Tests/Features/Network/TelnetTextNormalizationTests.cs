using System.Reflection;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// Telnet newline normalization in one pass: lone \n becomes \r\n,
// existing \r\n is untouched, bare \r stays bare.
[Collection("Ported")]
public sealed class TelnetTextNormalizationTests
{
    private static readonly MethodInfo TelnetTextMethod =
        typeof(TelnetConnection).GetMethod("TelnetText", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string Norm(string text) => (string)TelnetTextMethod.Invoke(null, [text])!;

    [Fact]
    public void LoneLf_GetsCr()
    {
        Assert.Equal("a\r\nb", Norm("a\nb"));
        Assert.Equal("\r\n", Norm("\n"));
        Assert.Equal("a\r\n\r\nb", Norm("a\n\nb"));
    }

    [Fact]
    public void ExistingCrLf_Untouched()
    {
        Assert.Equal("a\r\nb", Norm("a\r\nb"));
        Assert.Equal("\r\n", Norm("\r\n"));
        Assert.Equal("a\r\n\r\nb", Norm("a\n\r\nb"));
        Assert.Equal("\r\r\n", Norm("\r\r\n"));
    }

    [Fact]
    public void BareCr_StaysBare()
    {
        Assert.Equal("a\rb", Norm("a\rb"));
        Assert.Equal("\r", Norm("\r"));
        Assert.Equal("\r\n\r", Norm("\n\r"));
    }

    [Fact]
    public void PlainAndEmpty_Unchanged()
    {
        Assert.Equal("", Norm(""));
        Assert.Equal("hello", Norm("hello"));
    }
}
