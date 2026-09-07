using System.Collections.Concurrent;
using System.Net.WebSockets;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Connection bookkeeping stays consistent under parallel churn.
[Collection("Ported")]
public class ConnectionConcurrencyTests
{
    [Fact]
    public void ParallelRegisterAndDisconnect_KeepsCountsConsistent()
    {
        // Parallel RegisterConnection/Disconnect (ConnectionManager.cs:767,816)
        // must converge: every distinct registration is visible at the barrier,
        // and after each connection disconnects the count returns to zero.
        const int connections = 16;
        var previousGlobal = ConnectionManager.GlobalInstance;
        var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 10000, reliefLimit: 0);
        var manager = new ConnectionManager(pool, new AtherizSettings());
        var conns = Enumerable.Range(0, connections).Select(i => new TestConnection($"hammer-{i}")).ToList();
        var errors = new ConcurrentQueue<Exception>();
        using var barrier = new Barrier(connections + 1);
        var threads = conns.Select((conn, i) =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    Assert.True(manager.RegisterConnection($"hammer-{i}", conn));
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    manager.Disconnect(conn);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        try
        {
            threads.ForEach(t => t.Start());
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            // All registrations landed and no disconnect started yet.
            Assert.Equal(connections, manager.ConnectionCount);
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            foreach (var thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "worker did not finish; possible deadlock");
            Assert.Empty(errors);
            Assert.Equal(0, manager.ConnectionCount);
        }
        finally
        {
            ConnectionManager.GlobalInstance = previousGlobal;
            pool.Stop(wait: true, timeout: TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void ParallelWebSocketSend_DrainsPendingAccounting()
    {
        // Concurrent SendCommand calls (WebSocketProtocol.cs:95-126) must all be
        // accepted and every reservation released: pending bytes/count return to
        // zero with no half-open send pinning the connection.
        const int senders = 8;
        const int perSender = 25;
        using var socket = new ClientWebSocket();
        var conn = new WebSocketConnection(socket, "ws-hammer", new AtherizSettings(), "127.0.0.1");
        var errors = new ConcurrentQueue<Exception>();
        using var barrier = new Barrier(senders + 1);
        var threads = Enumerable.Range(0, senders).Select(s =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    for (int i = 0; i < perSender; i++)
                        conn.SendCommand("text", new List<object?> { $"hammer-{s}-{i}" });
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        try
        {
            threads.ForEach(t => t.Start());
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            foreach (var thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "sender did not finish; possible deadlock");
            Assert.Empty(errors);
            // 200 small sends stay far below the 256-send/4MB caps, so the
            // limiter must never trip closed mid-hammer.
            Assert.False(conn.IsClosing);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while ((conn.PendingCount != 0 || conn.PendingBytes != 0) && sw.Elapsed < TimeSpan.FromSeconds(30))
                Thread.SpinWait(1000);
            Assert.Equal(0, conn.PendingCount);
            Assert.Equal(0, conn.PendingBytes);
        }
        finally
        {
            try
            {
                conn.Dispose();
            }
            catch
            {
            }
        }
    }
}
