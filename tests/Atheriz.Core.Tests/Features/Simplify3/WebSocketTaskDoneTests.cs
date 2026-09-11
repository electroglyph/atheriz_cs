// Pins for the single-test TaskDone fault filter (WebSocketProtocol.cs):
// cancelled and disposed sends stay silent while unexpected faults are
// logged, and every path releases its limiter reservation.
using System.Net.WebSockets;
using System.Reflection;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class WebSocketTaskDoneTests
{
    private sealed class ThrowingWebSocket(Func<Exception> thrower) : WebSocket
    {
        private readonly Func<Exception> _thrower = thrower;
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
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) =>
            throw _thrower();
    }

    private static string SendAndCapture(WebSocket socket, string sessionId)
    {
        var conn = new WebSocketConnection(socket, sessionId, null, "127.0.0.1");
        try
        {
            using var cap = new CaptureAtherizLog();
            conn.SendCommand("text", ["hello"], []);
            Assert.True(PortedHelpers.WaitFor(() => conn.PendingBytes == 0, 10000),
                "faulted send did not release its reservation");
            Thread.Sleep(500);
            return cap.Read();
        }
        finally { try { conn.Dispose(); } catch { } }
    }

    [Fact]
    public void SendCommand_OperationCanceledSend_StaysSilentAndReleases()
    {
        var log = SendAndCapture(new ThrowingWebSocket(() => new OperationCanceledException("boom")), "ws-td-oce");
        Assert.DoesNotContain("Async task failed", log);
    }

    [Fact]
    public void SendCommand_ObjectDisposedSend_StaysSilentAndReleases()
    {
        var log = SendAndCapture(new ThrowingWebSocket(() => new ObjectDisposedException("ws")), "ws-td-ode");
        Assert.DoesNotContain("Async task failed", log);
    }

    [Fact]
    public void SendCommand_FailingSend_LogsAsyncTaskFailedAndReleases()
    {
        var socket = new ThrowingWebSocket(() => new InvalidOperationException("boom"));
        var conn = new WebSocketConnection(socket, "ws-td-fault", null, "127.0.0.1");
        try
        {
            using var cap = new CaptureAtherizLog();
            conn.SendCommand("text", ["hello"], []);
            Assert.True(PortedHelpers.WaitFor(() => conn.PendingBytes == 0, 10000),
                "faulted send did not release its reservation");
            Assert.True(PortedHelpers.WaitFor(() => cap.Read().Contains("Async task failed"), 10000),
                "unexpected send fault was not logged");
        }
        finally { try { conn.Dispose(); } catch { } }
    }

    [Fact]
    public void TaskDone_CanceledTask_StaysSilent()
    {
        var socket = new ThrowingWebSocket(() => new InvalidOperationException("unused"));
        var conn = new WebSocketConnection(socket, "ws-td-canceled", null, "127.0.0.1");
        try
        {
            var tcs = new TaskCompletionSource();
            tcs.TrySetCanceled();
            var canceled = tcs.Task;
            Assert.True(canceled.IsCanceled);

            using var cap = new CaptureAtherizLog();
            var taskDone = typeof(WebSocketConnection).GetMethod("TaskDone", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var ex = Record.Exception(() => taskDone.Invoke(conn, [canceled]));
            Thread.Sleep(250);
            Assert.Null(ex);
            Assert.DoesNotContain("Async task failed", cap.Read());
            Assert.Equal(0, conn.PendingBytes);
        }
        finally { try { conn.Dispose(); } catch { } }
    }
}
