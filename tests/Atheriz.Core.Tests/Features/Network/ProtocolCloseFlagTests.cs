// Pins for the hoisted close flag (WebSocketProtocol.cs / TelnetProtocol.cs):
// the first Close marks the connection closing and runs the close exactly
// once; a second Close keeps the flag and performs no further close work.
using System.Net.WebSockets;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

[Collection("Ported")]
public sealed class ProtocolCloseFlagTests
{
    private sealed class CaptureWriter : ITelnetWriter
    {
        public int Closes;
        public void Write(string text) { }
        public void Iac(byte cmd, byte opt) { }
        public void Close() => Closes++;
        public int? GetWriteBufferSize() => 0;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "1.2.3.4";
    }

    private sealed class CountingWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        private int _closeCalls;
        public int CloseCalls => Volatile.Read(ref _closeCalls);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        {
            Interlocked.Increment(ref _closeCalls);
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            CloseAsync(status, description, ct);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) =>
            Task.CompletedTask;
    }

    [Fact]
    public void Close_FirstCall_MarksClosingAndClosesTelnetWriter()
    {
        var writer = new CaptureWriter();
        var conn = new TelnetConnection(new object(), writer);
        Assert.False(conn.IsClosing);

        conn.Close();

        Assert.True(conn.IsClosing);
        Assert.Equal(1, writer.Closes);
    }

    [Fact]
    public void Close_SecondCall_KeepsClosingWithoutSecondTelnetWriterClose()
    {
        var writer = new CaptureWriter();
        var conn = new TelnetConnection(new object(), writer);

        conn.Close();
        conn.Close();

        Assert.True(conn.IsClosing);
        Assert.Equal(1, writer.Closes);
    }

    [Fact]
    public void Close_FirstCall_MarksClosingAndStartsWebSocketHandshake()
    {
        var socket = new CountingWebSocket();
        var conn = new WebSocketConnection(socket, "ws-close-1", null, "127.0.0.1");
        try
        {
            Assert.False(conn.IsClosing);

            conn.Close();

            Assert.True(conn.IsClosing);
            Assert.True(PortedHelpers.WaitFor(() => socket.CloseCalls == 1, 10000),
                "first close did not run the close handshake");
        }
        finally { try { conn.Dispose(); } catch { } }
    }

    [Fact]
    public void Close_SecondCall_KeepsClosingWithoutSecondWebSocketHandshake()
    {
        var socket = new CountingWebSocket();
        var conn = new WebSocketConnection(socket, "ws-close-2", null, "127.0.0.1");
        try
        {
            conn.Close();
            Assert.True(PortedHelpers.WaitFor(() => socket.CloseCalls == 1, 10000),
                "first close did not run the close handshake");

            conn.Close();

            Thread.Sleep(250);
            Assert.True(conn.IsClosing);
            Assert.Equal(1, socket.CloseCalls);
        }
        finally { try { conn.Dispose(); } catch { } }
    }
}
