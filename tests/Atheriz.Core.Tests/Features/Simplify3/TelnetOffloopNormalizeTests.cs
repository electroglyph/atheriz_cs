// Pins for the single-normalize OffloopWrite path (TelnetProtocol.cs):
// OffloopWrite runs TelnetText once, WriterWrite passes the text through
// unchanged, so lone LF becomes CRLF exactly once while existing CRLF and
// bare CR pass through untouched.
using System.Text;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class TelnetOffloopNormalizeTests
{
    private sealed class CaptureWriter : ITelnetWriter
    {
        public readonly List<string> Writes = [];
        public void Write(string text) => Writes.Add(text);
        public void Iac(byte cmd, byte opt) { }
        public void Close() { }
        public int? GetWriteBufferSize() => 0;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "1.2.3.4";
    }

    private static (TelnetConnection Conn, CaptureWriter Writer) MakeConn()
    {
        var writer = new CaptureWriter();
        return (new TelnetConnection(new object(), writer), writer);
    }

    private static string WriteOnce(string text)
    {
        var (conn, writer) = MakeConn();
        var nb = Encoding.UTF8.GetByteCount(text);
        conn.Limiter.TryReserve(nb);
        conn.OffloopWrite(text, nb);
        Assert.Single(writer.Writes);
        Assert.Equal(0, conn.PendingBytes);
        return writer.Writes[0];
    }

    [Fact]
    public void OffloopWrite_LoneLf_WritesCrLf()
    {
        Assert.Equal("hi\r\n", WriteOnce("hi\n"));
    }

    [Fact]
    public void OffloopWrite_ExistingCrLf_WritesUnchanged()
    {
        Assert.Equal("hi\r\n", WriteOnce("hi\r\n"));
    }

    [Fact]
    public void OffloopWrite_BareCr_WritesUnchanged()
    {
        Assert.Equal("hi\rbye", WriteOnce("hi\rbye"));
    }

    [Fact]
    public void OffloopWrite_MixedEndings_NormalizesEachLoneLfOnce()
    {
        Assert.Equal("a\r\nb\r\nc\rd\r\n", WriteOnce("a\nb\r\nc\rd\n"));
    }
}
