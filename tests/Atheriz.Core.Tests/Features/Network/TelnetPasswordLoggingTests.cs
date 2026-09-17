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
        var (got, log) = await RunMaskingCaseAsync($"connect alice {password}");
        Assert.Equal($"connect alice {password}", got);
        Assert.DoesNotContain(password, log);
        Assert.Contains("connect alice ***", log);
    }

    [Theory]
    [InlineData("connect alice s3cr3t-pw", "connect alice s3cr3t-pw", "connect alice ***", "s3cr3t-pw")]
    [InlineData("CONNECT Alice MixedCase-Pw9", "CONNECT Alice MixedCase-Pw9", "connect Alice ***", "MixedCase-Pw9")]
    [InlineData("connect   spaced   wide-pw   extra", "connect   spaced   wide-pw   extra", "connect spaced ***", "wide-pw")]
    [InlineData("connect solopw", "connect solopw", "connect solopw ***", null)]
    [InlineData("connect", "connect", "[Telnet] recv 'connect' from", null)]
    public async Task Telnet_ConnectVariants_RedactedInLogVerbatimInDispatch(
        string input, string expectedDispatched, string expectedLogFragment, string? secret)
    {
        // Masking shape: dispatch always gets the raw line (no over-redaction),
        // the transport trace keeps at most `connect <account> ***`.
        var (dispatched, log) = await RunMaskingCaseAsync(input);
        Assert.Equal(expectedDispatched, dispatched);
        Assert.Contains(expectedLogFragment, log);
        if (secret is not null) Assert.DoesNotContain(secret, log);
    }

    private static async Task<(string Dispatched, string Log)> RunMaskingCaseAsync(string input)
    {
        using var env = GlobalTestEnv.Enter();
        // The recv trace logs at Debug, below the default Information gate, so
        // enable debug capture for the duration (restored in finally). A temp
        // SavePath keeps file logging out of the repo tree.
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = logDir });
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
            string got;
            using (var cap = new CaptureAtherizLog())
            {
                var task = TelnetProtocol.HandleSessionAsync(session, "127.0.0.1", mgr, settings, cts.Token);
                var bytes = Encoding.UTF8.GetBytes(input + "\r\n");
                await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
                got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                peer.Close();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(50);
                log = cap.Read();
            }
            return (got, log);
        }
        finally
        {
            try { peer.Dispose(); } catch { }
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = prevGlobal;
            try { AtherizLogger.ApplySettings(); } catch { }
            try { Directory.Delete(logDir, true); } catch { }
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
