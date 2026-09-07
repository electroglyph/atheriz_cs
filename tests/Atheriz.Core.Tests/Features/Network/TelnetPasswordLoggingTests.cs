using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
        // password in the server log. The transport trace at
        // TelnetProtocol.cs:690 records the raw line verbatim, so credentials
        // land in save/server.log. Correct behavior redacts the secret (or
        // skips the trace for credential lines) before logging.
        const string password = "s3cr3t-pw-xyz9";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var mgr = PortedHelpers.MakeManager();
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        TcpClient? client = null;
        TcpClient? server = null;
        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            server = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            mgr.RegisterHandler("text", new Action<BaseConnection, List<object?>, Dictionary<string, object?>>((c, a, k) =>
            {
                received.TrySetResult(a.Count > 0 ? a[0]?.ToString() ?? "" : "");
            }));
            var settings = new AtherizSettings();
            var mi = typeof(TelnetProtocol).GetMethod("HandleTelnetClientAsync", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mi);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                var task = (Task)mi!.Invoke(null, new object?[] { server, null, mgr, settings, null })!;
                var stream = client.GetStream();
                var bytes = Encoding.UTF8.GetBytes($"connect alice {password}\r\n");
                await stream.WriteAsync(bytes, 0, bytes.Length);
                var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal($"connect alice {password}", got);
                client.Close();
                await task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(50);
                log = cap.Read();
            }
            Assert.DoesNotContain(password, log);
        }
        finally
        {
            try { client?.Close(); } catch { }
            try { server?.Close(); } catch { }
            try { listener.Stop(); } catch { }
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }
}
