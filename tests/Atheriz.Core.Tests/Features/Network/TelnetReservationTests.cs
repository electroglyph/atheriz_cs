using System.Text;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Network;

// The telnet pending-bytes reservation must cover the bytes actually
// emitted: SendCommand normalizes lone LF to CRLF before measuring, so a
// 3-byte "a\nb" reserves 4 bytes and trips a 3-byte budget.
[Collection("Ported")]
public sealed class TelnetReservationTests
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

    private static (TelnetConnection Conn, CaptureWriter Writer) MakeConn(int maxBytes)
    {
        var writer = new CaptureWriter();
        var settings = new AtherizSettings { TelnetMaxPendingBytes = maxBytes };
        return (new TelnetConnection(new object(), writer, "rsv", settings), writer);
    }

    [Fact]
    public void SendCommand_TextWithNewline_ReservesNormalizedBytes()
    {
        var (conn, writer) = MakeConn(3);
        conn.SendCommand("text", "a\nb");
        Assert.True(conn.IsClosing);
        Assert.Empty(writer.Writes);
        Assert.Equal(0, conn.PendingBytes);
    }

    [Fact]
    public void SendCommand_PromptMaskedWithNewline_ReservesNormalizedBytes()
    {
        var (conn, writer) = MakeConn(3);
        conn.SendCommand("prompt_masked", "a\nb");
        Assert.True(conn.IsClosing);
        Assert.Empty(writer.Writes);
        Assert.Equal(0, conn.PendingBytes);
    }

    [Fact]
    public void SendCommand_TextWithoutNewline_WithinBudgetStaysOpen()
    {
        var (conn, _) = MakeConn(3);
        Assert.Equal(2, Encoding.UTF8.GetByteCount("ab"));
        conn.SendCommand("text", "ab");
        Assert.False(conn.IsClosing);
    }
}
