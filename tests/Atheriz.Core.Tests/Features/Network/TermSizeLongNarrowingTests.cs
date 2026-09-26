using Atheriz.Core.Network;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// Audit 10 finding 5: a JSON integer outside Int32 arrives as long, and the
// long arm narrowed it unchecked, so 2^32+1 wrapped to 1 and passed the
// 0 < w guard. Out-of-Int32 longs are now rejected; in-range longs still work.
[Collection("Ported")]
public sealed class TermSizeLongNarrowingTests
{
    [Fact]
    public void TermSize_RejectsLongWrappingToOne()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        int before = conn.Session.TermWidth;
        new InputFuncs().TermSize(conn, [(object?)(long)4294967297, (long)24], []);
        new InputFuncs().TermSize(conn, [(object?)(long)-4294967295, (long)24], []);
        Assert.Equal(before, conn.Session.TermWidth);
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void TermSize_RejectsLongAboveInt32Max()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        int before = conn.Session.TermWidth;
        new InputFuncs().TermSize(conn, [(object?)(long)9999999999, (long)24], []);
        Assert.Equal(before, conn.Session.TermWidth);
        Assert.Empty(conn.Sent);
    }

    [Fact]
    public void TermSize_AcceptsInRangeLong()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new InputFuncs().TermSize(conn, [(object?)(long)80, (long)24], []);
        Assert.Equal(80, conn.Session.TermWidth);
        Assert.Equal(24, conn.Session.TermHeight);
    }
}
