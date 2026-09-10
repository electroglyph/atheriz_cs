using System.Text.Json;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// TermSize/MapSize shared size coercion: JsonElement/int/long inputs are
// coerced identically, each command keeps its own bounds, and bad input is
// a silent return (no sends, session untouched).
[Collection("Ported")]
public sealed class MapEditSizeCoercionTests
{
    private static List<object?> JsonArgs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => (object?)e.Clone()).ToList();
    }

    [Fact]
    public void TermSize_AcceptsIntPair_WithinBounds()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new InputFuncs().TermSize(conn, [80, 24], []);
        Assert.Equal(80, conn.Session.TermWidth);
        Assert.Equal(24, conn.Session.TermHeight);
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void TermSize_AcceptsJsonElementNumbers()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new InputFuncs().TermSize(conn, JsonArgs("[100, 40]"), []);
        Assert.Equal(100, conn.Session.TermWidth);
        Assert.Equal(40, conn.Session.TermHeight);
    }

    [Fact]
    public void TermSize_AcceptsLongPair()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new InputFuncs().TermSize(conn, [(object?)(long)90, (long)30], []);
        Assert.Equal(90, conn.Session.TermWidth);
        Assert.Equal(30, conn.Session.TermHeight);
    }

    [Fact]
    public void TermSize_RejectsOutOfBounds_Silently()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        int wBefore = conn.Session.TermWidth;
        int hBefore = conn.Session.TermHeight;
        new InputFuncs().TermSize(conn, [1001, 24], []);
        new InputFuncs().TermSize(conn, [80, 0], []);
        new InputFuncs().TermSize(conn, [-1, 24], []);
        Assert.Equal(wBefore, conn.Session.TermWidth);
        Assert.Equal(hBefore, conn.Session.TermHeight);
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void TermSize_RejectsBadType_Silently()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new InputFuncs().TermSize(conn, ["wide", 24], []);
        new InputFuncs().TermSize(conn, JsonArgs("[\"wide\", 24]"), []);
        new InputFuncs().TermSize(conn, [80], []);
        Assert.Equal(78, conn.Session.TermWidth);
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void MapSize_AcceptsPair_AndRejectsBadInput_Silently()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new InputFuncs().MapSize(conn, [50, 50], []);
        Assert.Equal(50, conn.Session.MapWidth);
        Assert.Equal(50, conn.Session.MapHeight);
        new InputFuncs().MapSize(conn, JsonArgs("[60, 61]"), []);
        Assert.Equal(60, conn.Session.MapWidth);
        Assert.Equal(61, conn.Session.MapHeight);
        new InputFuncs().MapSize(conn, [(object?)(long)70, (long)71], []);
        Assert.Equal(70, conn.Session.MapWidth);
        Assert.Equal(71, conn.Session.MapHeight);
        // Rejects: over max, zero, wrong type — session keeps last good value.
        new InputFuncs().MapSize(conn, [5000, 10], []);
        new InputFuncs().MapSize(conn, [10, 0], []);
        new InputFuncs().MapSize(conn, ["x", 10], []);
        Assert.Equal(70, conn.Session.MapWidth);
        Assert.Equal(71, conn.Session.MapHeight);
        Assert.Empty(conn.Sent);
    }
}
