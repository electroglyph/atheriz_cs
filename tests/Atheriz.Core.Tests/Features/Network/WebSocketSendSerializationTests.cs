using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// WebSocket send path serializes once and reserves the exact byte length:
// the bytes on the wire stay byte-identical to the old
// Serialize-then-GetByteCount/GetBytes form.
[Collection("Ported")]
public sealed class WebSocketSendSerializationTests
{
    private sealed class CapturingWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        private readonly object _gate = new();
        public readonly List<byte[]> SentBytes = [];
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            var copy = new byte[buffer.Count];
            Buffer.BlockCopy(buffer.Array!, buffer.Offset, copy, 0, buffer.Count);
            lock (_gate) SentBytes.Add(copy);
            return Task.CompletedTask;
        }
        public int SendCount { lock (_gate) return SentBytes.Count; }
    }

    [Fact]
    public void SendCommand_WireBytes_MatchLegacySerialization()
    {
        var socket = new CapturingWebSocket();
        var conn = new WebSocketConnection(socket, "ser", null, "9.9.9.9");
        try
        {
            var args = new List<object?> { "hello" };
            var kwargs = new Dictionary<string, object?>();
            conn.SendCommand("text", args, kwargs);
            Assert.True(PortedHelpers.WaitFor(() => socket.SendCount == 1 && conn.PendingBytes == 0, 5000),
                "send did not complete");
            // Legacy form this replaces: Serialize string, then UTF8 bytes.
            var expected = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[] { "text", args, kwargs }));
            Assert.Equal(expected, socket.SentBytes[0]);
        }
        finally { try { conn.Dispose(); } catch { } }
    }

    [Fact]
    public void SendCommand_NullArgsDefault_EmptyPayloadShape()
    {
        var socket = new CapturingWebSocket();
        var conn = new WebSocketConnection(socket, "ser2", null, "9.9.9.9");
        try
        {
            conn.SendCommand("text");
            Assert.True(PortedHelpers.WaitFor(() => socket.SendCount == 1 && conn.PendingBytes == 0, 5000),
                "send did not complete");
            var expected = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new object[] { "text", new List<object?>(), new Dictionary<string, object?>() }));
            Assert.Equal(expected, socket.SentBytes[0]);
        }
        finally { try { conn.Dispose(); } catch { } }
    }
}
