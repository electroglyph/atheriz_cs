using System.Text;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// Telnet credential handling: the per-line transport trace must never persist secrets.
[Collection("Ported")]
public class TelnetPasswordLoggingTests
{
    [Fact]
    public async Task Telnet_ConnectLine_PasswordNeverReachesLog()
    {
        // Behavior: typing `connect <account> <password>` must not leave the
        // password in the server log. The transport trace in
        // TelnetProtocol.HandleSessionAsync records the raw line verbatim, so
        // credentials would land in save/server.log. Correct behavior redacts
        // the secret before logging.
        const string password = "s3cr3t-pw-xyz9";
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        var (peer, serverStream) = telnet_cs.Transport.InMemoryPipe.Create();
        using var session = new telnet_cs.Server.ServerSession(serverStream, QuietSessionOptions(), CancellationToken.None);
        try
        {
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            mgr.RegisterHandler("text", new Action<BaseConnection, List<object?>, Dictionary<string, object?>>((c, a, k) =>
            {
                received.TrySetResult(a.Count > 0 ? a[0]?.ToString() ?? "" : "");
            }));
            var settings = new AtherizSettings();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                var task = TelnetProtocol.HandleSessionAsync(session, "127.0.0.1", mgr, settings, cts.Token);
                var bytes = Encoding.UTF8.GetBytes($"connect alice {password}\r\n");
                await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
                var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal($"connect alice {password}", got);
                peer.Close();
                // The in-memory peer close cannot flip the server end's
                // Connected flag (real sockets do on FIN), so also cancel the
                // stopping token: the reader reports EOF on cancel, exactly
                // like a dead transport at shutdown.
                cts.Cancel();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(50);
                log = cap.Read();
            }
            Assert.DoesNotContain(password, log);
        }
        finally
        {
            try { peer.Dispose(); } catch { }
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    private static telnet_cs.Server.TelnetServerOptions QuietSessionOptions() => new()
    {
        TextEncoding = Encoding.UTF8,
        RequestCharacterSet = false,
        IdleTimeout = Timeout.InfiniteTimeSpan,
        HandshakeTimeout = Timeout.InfiniteTimeSpan,
        StatusInterval = null,
        Log = null,
    };
}
