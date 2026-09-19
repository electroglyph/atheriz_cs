// Regression pins: node move validation, object hash stability, safe arithmetic, parser limits, messaging.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class CorrectnessBatchATests
{
    // Two moves into the same empty cell must both fail validation, so
    // ApplyMoves never overwrites (first node lost with a stale coord).
    [Fact]
    public void ValidateMoves_DuplicateDst_BothFail()
    {
        var grid = new NodeGrid("mvdup", 0);
        grid.AddNode(new Node(new Coord("mvdup", 0, 0, 0)));
        grid.AddNode(new Node(new Coord("mvdup", 1, 0, 0)));
        var failed = grid.CheckMoves([((0, 0), (5, 5)), ((1, 0), (5, 5))]);
        Assert.Equal([0, 1], failed.OrderBy(i => i).ToList());
    }

    [Fact]
    public void ApplyMoves_DuplicateDst_NothingMoves()
    {
        var grid = new NodeGrid("mvdupb", 0);
        var a = new Node(new Coord("mvdupb", 0, 0, 0));
        var b = new Node(new Coord("mvdupb", 1, 0, 0));
        grid.AddNode(a);
        grid.AddNode(b);
        var failed = grid.ApplyMoves([((0, 0), (5, 5)), ((1, 0), (5, 5))]);
        Assert.Equal(2, failed.Count);
        Assert.Same(a, grid.GetNode((0, 0)));
        Assert.Same(b, grid.GetNode((1, 0)));
        Assert.Null(grid.GetNode((5, 5)));
        Assert.Equal(new Coord("mvdupb", 0, 0, 0), a.Coord);
        Assert.Equal(new Coord("mvdupb", 1, 0, 0), b.Coord);
    }

    [Fact]
    public void ApplyMoves_SwapStillAllowed()
    {
        var grid = new NodeGrid("mvdupc", 0);
        var a = new Node(new Coord("mvdupc", 0, 0, 0));
        var b = new Node(new Coord("mvdupc", 1, 0, 0));
        grid.AddNode(a);
        grid.AddNode(b);
        var failed = grid.ApplyMoves([((0, 0), (1, 0)), ((1, 0), (0, 0))]);
        Assert.Empty(failed);
        Assert.Same(a, grid.GetNode((1, 0)));
        Assert.Same(b, grid.GetNode((0, 0)));
    }

    // Hash-snapshot semantics — Id reassignment after hash-container
    // insertion keeps the member findable; same-Id instances arranged
    // before first hashing still hash equal.
    [Fact]
    public void HashSet_MembershipSurvivesIdReassignment()
    {
        var o = GameObject.Create("member");
        var set = new HashSet<GameObject> { o };
        Assert.Contains(o, set);
        int hBefore = o.GetHashCode();
        o.Id = o.Id + 1000;
        Assert.Equal(hBefore, o.GetHashCode());
        Assert.Contains(o, set);
        Assert.True(set.TryGetValue(o, out _));
    }

    // Same-Id duplicates (reload pattern) are arranged via the load-path
    // re-key, which moves the hash snapshot with the id.
    [Fact]
    public void SameIdInstances_ArrangedBeforeHashing_HashEqual()
    {
        var a = GameObject.Create("pin-a");
        var b = GameObject.Create("pin-b");
        b.SetIdRaw(a.Id);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        var set = new HashSet<GameObject> { a };
        Assert.True(set.TryGetValue(b, out var found));
        Assert.Same(a, found);
    }

    // Divide/modulo by zero must raise, not render Infinity/NaN.
    [Theory]
    [InlineData("1/0")]
    [InlineData("1%0")]
    [InlineData("1//0")]
    [InlineData("4/(2-2)")]
    public void SafeArithEval_DivideByZero_Throws(string expr)
    {
        Assert.Throws<InvalidOperationException>(() => FuncParserHelpers.SafeArithEval(expr));
    }

    // Oversize input honors raiseErrors:false by echoing raw text.
    [Fact]
    public void InstanceParse_OversizeNoRaise_EchoesRaw()
    {
        var parser = new FuncParser();
        string big = new('x', FuncParser.MaxMessageSize + 1);
        var res = parser.Parse(big, raiseErrors: false);
        Assert.Equal(big, res);
    }

    [Fact]
    public void InstanceParse_OversizeRaise_Throws()
    {
        var parser = new FuncParser();
        string big = new('x', FuncParser.MaxMessageSize + 1);
        Assert.Throws<FuncParser.ParsingError>(() => parser.Parse(big, raiseErrors: true));
    }

    [Fact]
    public void StaticParse_OversizeNoRaise_EchoesRaw()
    {
        string big = new('y', FuncParser.MaxMessageSize + 1);
        Assert.Equal(big, FuncParser.Parse(big, null, null, null));
    }

    // Location all_receivers renders display names, never the CLR type.
    [Fact]
    public void AtSayFull_LocationAllReceivers_UsesDisplayNames()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("pinloc", 0, 0, 0));
        nh.AddNode(node);
        GameObject Mk(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            Assert.True(o.MoveTo(node));
            o.ClearMessages();
            return o;
        }
        var speaker = Mk("Alice");
        var bob = Mk("Bob");
        var carol = Mk("Carol");

        speaker.AtSayFull("hello", msgSelf: false,
            msgLocation: "{object} says to {all_receivers}, \"{speech}\"",
            receivers: [bob]);

        var seen = Assert.Single(carol.PeekMessages());
        Assert.Equal("Alice says to Bob, \"hello\"", seen);
        Assert.DoesNotContain("Atheriz.Core.Objects.GameObject", seen);
    }
}
