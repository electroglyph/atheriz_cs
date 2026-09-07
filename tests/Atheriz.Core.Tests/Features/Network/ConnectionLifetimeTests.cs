using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Network;

// Connection lifecycle: disposal and queued-input retry under pool pressure.
[Collection("Ported")]
public class ConnectionLifetimeTests
{
    // --- Connection pool/queue lifecycle ---

    [Fact]
    public void BaseConnection_ImplementsIDisposable()
    {
        // Behavior: connections own queues/sessions (subclasses add
        // SemaphoreSlim/TcpClient/WebSocket) so the base type must be disposable.
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(BaseConnection)));
    }

    [Fact]
    public void DroppedInput_IsRetriedAfterPoolFrees()
    {
        // Pin: pool-full drops schedule RetryDrain chains (50 ms) that deliver
        // once capacity returns — no silent loss on transient pressure.
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            mgr.Atp.QueueLimit = 0;
            var conn = new TestConnection();
            var tcs = new TaskCompletionSource<bool>();
            conn.EnqueueInput(
                new Action<BaseConnection, List<object?>, Dictionary<string, object?>>((c, a, k) => tcs.TrySetResult(true)),
                new List<object?>(), new Dictionary<string, object?>());
            mgr.Atp.QueueLimit = 10000;
            Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(2)), "dropped input was never retried");
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }
}
