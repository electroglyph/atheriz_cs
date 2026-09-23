// Async-dispose joins: DisposeAsync must await in-flight I/O instead of
// parking the caller (2.5), then release the same resources sync Dispose does.
using System.Net.WebSockets;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

[Collection("Ported")]
public sealed class ConnectionDisposeAsyncTests
{
    // Writer double whose Write blocks on a gate: lets the test hold a
    // TelnetConnection write in flight across the DisposeAsync call.
    private sealed class GateWriter : ITelnetWriter, IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false);
        public readonly ManualResetEventSlim Entered = new(false);
        public bool Disposed { get; private set; }
        public void Open() => _gate.Set();
        public void Write(string text)
        {
            Entered.Set();
            _gate.Wait(TimeSpan.FromSeconds(10));
        }
        public void Iac(byte cmd, byte opt) { }
        public void Close() { }
        public int? GetWriteBufferSize() => null;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "127.0.0.1";
        public void Dispose() { Disposed = true; }
    }

    private sealed class GateWebSocket : WebSocket
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private readonly ManualResetEventSlim _entered = new(false);
        private int _sends;
        public bool Entered => _entered.IsSet;
        public int SendCount => Volatile.Read(ref _sends);
        public void Open() => _gate.Set();
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) =>
            Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            _entered.Set();
            _gate.Wait(TimeSpan.FromSeconds(10));
            Interlocked.Increment(ref _sends);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TelnetConnection_DisposeAsync_JoinsInflightWrite()
    {
        using var env = GlobalTestEnv.Enter();
        var writer = new GateWriter();
        // Construct off the test thread so SendCommand takes the off-loop
        // (ScheduleWrite) path instead of delivering inline.
        var conn = Task.Run(() => new TelnetConnection(new object(), writer, "dispose-join")).GetAwaiter().GetResult();
        conn.SendCommand("text", new List<object?> { "gated" }, new Dictionary<string, object?>());
        Assert.True(writer.Entered.Wait(TimeSpan.FromSeconds(5)), "offloaded write never started");
        var disposeTask = conn.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted, "DisposeAsync completed while a write was still in flight");
        writer.Open();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(writer.Disposed, "DisposeAsync did not release the writer");
    }

    [Fact]
    public async Task WebSocketConnection_DisposeAsync_JoinsInflightSend()
    {
        using var env = GlobalTestEnv.Enter();
        var socket = new GateWebSocket();
        var conn = new WebSocketConnection(socket, "dispose-join", null, "9.9.9.9");
        conn.SendCommand("text", new List<object?> { "gated" }, new Dictionary<string, object?>());
        Assert.True(PortedHelpers.WaitFor(() => socket.Entered, 5000), "send never started");
        var disposeTask = conn.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted, "DisposeAsync completed while a send was still in flight");
        socket.Open();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(1, socket.SendCount);
        Assert.Equal(0, conn.PendingCount);
    }
}
