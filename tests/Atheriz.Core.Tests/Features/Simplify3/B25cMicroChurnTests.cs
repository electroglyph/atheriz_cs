// Pins for simplify3 §7 U15–U17 micro-churn: DelegateInvoker record→class
// (identity-table behavior unchanged), MazeCommand visited HashSet
// (generation still valid, each cell claimed once), Session.Msg delegation
// (null-connection no-op plus the full msgType mapping preserved).
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B25cMicroChurnTests
{
    private static int Double(int x) => x * 2;
    private static int Triple(int x) => x * 3;

    [Fact]
    public void Invoke_DistinctDelegates_KeepSeparateMetadata()
    {
        Func<int, int> doubleFn = Double;
        Func<int, int> tripleFn = Triple;
        Assert.Equal(42, DelegateInvoker.Invoke(doubleFn, [21]));
        Assert.Equal(63, DelegateInvoker.Invoke(tripleFn, [21]));
        Assert.Equal(42, DelegateInvoker.Invoke(doubleFn, [21]));
    }

    [Fact]
    public void Invoke_EqualButDistinctInstances_BehaveIdentically()
    {
        // Capturing lambdas: each evaluation allocates a distinct closure
        // instance (method groups and non-capturing lambdas are compiler-cached
        // to one instance, so they cannot prove distinctness).
        int two = 2;
        Func<int, int> first = x => x * two;
        Func<int, int> second = x => x * two;
        Assert.False(ReferenceEquals(first, second));
        Assert.Equal(42, DelegateInvoker.Invoke(first, [21]));
        Assert.Equal(42, DelegateInvoker.Invoke(second, [21]));
    }

    [Fact]
    public void CreateMaze_GenerationValid_NonEmptyWithAdjacentEdges()
    {
        using var env = GlobalTestEnv.Enter();
        const int w = 6;
        const int h = 6;
        var maze = MazeCommand.CreateMaze(w, h);
        Assert.NotEmpty(maze);
        foreach (var (cell, neighbors) in maze)
        {
            Assert.InRange(cell.Item1, 0, w - 1);
            Assert.InRange(cell.Item2, 0, h - 1);
            foreach (var next in neighbors)
            {
                Assert.InRange(next.Item1, 0, w - 1);
                Assert.InRange(next.Item2, 0, h - 1);
                Assert.Equal(1, Math.Abs(next.Item1 - cell.Item1) + Math.Abs(next.Item2 - cell.Item2));
            }
        }
    }

    [Fact]
    public void CreateMaze_VisitedSemantics_NeighborCellsClaimedOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var maze = MazeCommand.CreateMaze(6, 6);
        var seen = new HashSet<(int, int)>();
        foreach (var neighbors in maze.Values)
            foreach (var next in neighbors)
                Assert.True(seen.Add(next), $"cell {next} claimed twice");
    }

    [Fact]
    public void Msg_NullConnection_IsNoOp()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Session();
        Assert.Null(Record.Exception(() => session.Msg("hi")));
        Assert.Null(Record.Exception(() => session.Msg("hi", "prompt")));
    }

    [Fact]
    public void Msg_TextOnly_SendsTextCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        var session = new Session(connection: conn);
        session.Msg("hi");
        var sent = conn.Sent;
        Assert.Single(sent);
        Assert.Equal("text", sent[0].Cmd);
        Assert.Contains("hi", sent[0].Args[0]?.ToString() ?? "");
    }

    [Fact]
    public void Msg_WithMsgType_SendsNamedCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        var session = new Session(connection: conn);
        session.Msg("hi", "prompt");
        var sent = conn.Sent;
        Assert.Single(sent);
        Assert.Equal("prompt", sent[0].Cmd);
        Assert.Equal("hi", sent[0].Args[0]);
    }
}
