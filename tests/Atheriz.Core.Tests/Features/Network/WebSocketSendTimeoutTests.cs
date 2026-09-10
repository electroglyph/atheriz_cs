using System.Net.WebSockets;
using System.Reflection;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// LockedSendAsync uses one CTS (send phase) plus an alloc-free lock wait:
// lock-timeout drops quietly without Abort, send-timeout Aborts. Magic
// deadlines are named constants with identical values.
[Collection("Ported")]
public sealed class WebSocketSendTimeoutTests
{
    private abstract class ScriptedWebSocket : WebSocket
    {
        protected WebSocketState _state = WebSocketState.Open;
        private int _aborts;
        public int AbortCount => Volatile.Read(ref _aborts);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { Interlocked.Increment(ref _aborts); _state = WebSocketState.Aborted; }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
    }

    private sealed class InstantWebSocket : ScriptedWebSocket
    {
        private int _sends;
        public int SendCount => Volatile.Read(ref _sends);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            Interlocked.Increment(ref _sends);
            return Task.CompletedTask;
        }
    }

    private sealed class HangingWebSocket : ScriptedWebSocket
    {
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            // Mirrors a wedged peer: completes only via the send CTS.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
    }

    private static TimeSpan StaticSpan(string name) =>
        (TimeSpan)typeof(WebSocketConnection).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void SendDeadlines_KeepDocumentedValues()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), StaticSpan("SendLockTimeout"));
        Assert.Equal(TimeSpan.FromSeconds(5), StaticSpan("SendTimeout"));
        Assert.Equal(TimeSpan.FromMilliseconds(250), StaticSpan("CloseDrainTimeout"));
        Assert.Equal(TimeSpan.FromSeconds(2), StaticSpan("CloseHandshakeTimeout"));
    }

    [Fact]
    public void LockTimeout_DropsQuietly_WithoutAbortOrSend()
    {
        var socket = new InstantWebSocket();
        var conn = new WebSocketConnection(socket, "lockto", null, "9.9.9.9");
        var sendLock = (SemaphoreSlim)typeof(WebSocketConnection)
            .GetField("_sendLock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(conn)!;
        sendLock.Wait();
        try
        {
            conn.SendCommand("text", ["held"], []);
            // The send faults past the 5s lock deadline and releases its
            // reservation; the holder's "send" is never attempted or aborted.
            Assert.True(PortedHelpers.WaitFor(() => conn.PendingBytes == 0, 15000),
                "lock-timed-out send did not settle");
            Assert.Equal(0, socket.SendCount);
            Assert.Equal(0, socket.AbortCount);
        }
        finally
        {
            sendLock.Release();
            try { conn.Dispose(); } catch { }
        }
    }

    [Fact]
    public void SendTimeout_AbortsSocket()
    {
        var socket = new HangingWebSocket();
        var conn = new WebSocketConnection(socket, "sendto", null, "9.9.9.9");
        try
        {
            conn.SendCommand("text", ["hang"], []);
            Assert.True(PortedHelpers.WaitFor(() => socket.AbortCount == 1, 15000),
                "wedged send did not abort past the deadline");
        }
        finally { try { conn.Dispose(); } catch { } }
    }
}
