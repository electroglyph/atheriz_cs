using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// OffloopWrite/OffloopIacText share one guarded write cycle: buffer checked
// before AND after the write, reservation released exactly once per path,
// on every path (success, pre-check close, exception).
[Collection("Ported")]
public sealed class TelnetBufferedWriteTests
{
    private sealed class CaptureWriter : ITelnetWriter
    {
        public readonly List<string> Writes = [];
        public readonly List<(byte Cmd, byte Opt, string Text)> IacTexts = [];
        public int Closes;
        public bool ThrowOnWrite;
        public void Write(string text) { if (ThrowOnWrite) throw new InvalidOperationException("boom"); Writes.Add(text); }
        public void Iac(byte cmd, byte opt) { }
        public void IacWithText(byte cmd, byte opt, string text) => IacTexts.Add((cmd, opt, text));
        public void Close() => Closes++;
        public int? GetWriteBufferSize() => 0;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "1.2.3.4";
    }

    private sealed class BufferConn(object reader, object writer) : TelnetConnection(reader, writer)
    {
        public Func<int?>? BufFunc;
        public override int? GetWriteBufferSize() => BufFunc?.Invoke();
    }

    private static (BufferConn Conn, CaptureWriter Writer) MakeConn(int? firstBuf, int? secondBuf = null)
    {
        var writer = new CaptureWriter();
        var conn = new BufferConn(new object(), writer);
        int calls = 0;
        conn.BufFunc = () => ++calls == 1 ? firstBuf : (secondBuf ?? firstBuf);
        return (conn, writer);
    }

    private static int OverLimit() => AtherizSettings.Global.TelnetMaxPendingBytes + 1;

    [Fact]
    public void OffloopWrite_PreCheckExceeded_NoWrite_Closes_ReleasesOnce()
    {
        var (conn, writer) = MakeConn(OverLimit());
        conn.Limiter.TryReserve(5);
        conn.OffloopWrite("hello", 5);
        Assert.Empty(writer.Writes);
        Assert.True(conn.IsClosing);
        Assert.Equal(0, conn.PendingBytes);
    }

    [Fact]
    public void OffloopWrite_PostCheckExceeded_WritesOnce_ThenCloses_ReleasesOnce()
    {
        var (conn, writer) = MakeConn(0, OverLimit());
        conn.Limiter.TryReserve(5);
        conn.OffloopWrite("hello", 5);
        Assert.Single(writer.Writes);
        Assert.True(conn.IsClosing);
        Assert.Equal(0, conn.PendingBytes);
    }

    [Fact]
    public void OffloopWrite_HappyPath_WritesNormalized_ReleasesOnce()
    {
        var (conn, writer) = MakeConn(0);
        conn.Limiter.TryReserve(5);
        conn.OffloopWrite("hi\n", 5);
        Assert.Single(writer.Writes);
        Assert.Equal("hi\r\n", writer.Writes[0]);
        Assert.False(conn.IsClosing);
        Assert.Equal(0, conn.PendingBytes);
    }

    [Fact]
    public void OffloopWrite_Exception_Closes_ReleasesOnce()
    {
        var (conn, writer) = MakeConn(0);
        writer.ThrowOnWrite = true;
        conn.Limiter.TryReserve(5);
        conn.OffloopWrite("hello", 5);
        Assert.True(conn.IsClosing);
        Assert.Equal(0, conn.PendingBytes);
    }

    [Fact]
    public void OffloopIacText_SameGuardCycle()
    {
        var (pre, preWriter) = MakeConn(OverLimit());
        pre.Limiter.TryReserve(5);
        pre.OffloopIacText(251, 1, "hi", 5);
        Assert.Empty(preWriter.IacTexts);
        Assert.True(pre.IsClosing);
        Assert.Equal(0, pre.PendingBytes);

        var (ok, okWriter) = MakeConn(0);
        ok.Limiter.TryReserve(5);
        ok.OffloopIacText(251, 1, "hi\n", 5);
        Assert.Single(okWriter.IacTexts);
        Assert.Equal((251, 1, "hi\r\n"), (okWriter.IacTexts[0].Cmd, okWriter.IacTexts[0].Opt, okWriter.IacTexts[0].Text));
        Assert.False(ok.IsClosing);
        Assert.Equal(0, ok.PendingBytes);
    }
}
