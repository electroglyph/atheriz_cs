using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for audit batch B (F7-F12).
[Collection("Ported")]
public sealed class Audit6BatchBTests
{
    private sealed class AppearanceMarker
    {
        [After]
        public string Hook(GameObject? looker, string result) => result + "[hooked]";
    }

    [Fact]
    public void NodeAddScript_NonScriptId_NotRecorded()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("f7room", 0, 0, 0));
            var junk = GameObject.Create("f7junk");
            ObjectRegistry.AddObject(node);
            ObjectRegistry.AddObject(junk);

            node.AddScript(junk);
            node.AddScript(987654321);

            Assert.DoesNotContain(junk.Id, node.ScriptsSnapshot);
            Assert.DoesNotContain(987654321, node.ScriptsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void DiceRollAverage_MaxFaces_NoOverflow()
    {
        Assert.Equal(1073741824.0, GameUtils.DiceRollAverage(1, int.MaxValue));
        Assert.Equal(7.0, GameUtils.DiceRollAverage(2, 6));
    }

    [Fact]
    public void DiceRollAverage_ZeroFaces_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameUtils.DiceRollAverage(2, 0));
    }

    [Theory]
    [InlineData("-2**2", -4.0)]
    [InlineData("2**-2", 0.25)]
    [InlineData("(-2)**2", 4.0)]
    [InlineData("2**3**2", 512.0)]
    [InlineData("2**10", 1024.0)]
    public void SafeArithEval_PowerBindsTighterThanUnaryMinus(string expr, double expected)
    {
        Assert.Equal(expected, FuncParserHelpers.SafeArithEval(expr));
    }

    [Theory]
    [InlineData("-7%3", 2.0)]
    [InlineData("7%-3", -2.0)]
    [InlineData("7%3", 1.0)]
    public void SafeArithEval_NegativeModulo_Floors(string expr, double expected)
    {
        Assert.Equal(expected, FuncParserHelpers.SafeArithEval(expr));
    }

    [Theory]
    // "me" (2 chars) is rejected earlier by the length check; the reserved
    // gate pins the words that clear it.
    [InlineData("here")]
    [InlineData("all")]
    [InlineData("Here")]
    [InlineData("ALL")]
    public void ValidateCharacterName_ReservedWords_Rejected(string name)
    {
        Assert.Equal("That name is reserved.", Validation.ValidateCharacterName(name));
    }

    [Fact]
    public void NodeReturnAppearance_AfterHook_Fires()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("f12room", 0, 0, 0));
            var looker = GameObject.Create("f12looker", isPc: true);
            ObjectRegistry.AddObject(node);
            ObjectRegistry.AddObject(looker);
            node.InstallHook("return_appearance", new Func<GameObject?, string, string>(new AppearanceMarker().Hook));

            Assert.Contains("[hooked]", node.ReturnAppearance(looker));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
