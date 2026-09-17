using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Ported;
using telnet_cs;
using telnet_cs.Server;

namespace Atheriz.Core.Tests.Features.Network;

// Loopback integration over the real accept path: raw TCP peers against
// TelnetProtocol.BuildAcceptFilter + BuildServerOptions + AcceptLoopAsync.
// Complements the hermetic InMemoryPipe pins (TelnetCsWriterTests /
// TelnetSessionReaderTests) by proving the wiring — filter, loop, NAWS
// re-apply, echo toggles, TLS autodetect — end to end.
//
// Close-path contract (telnet_cs 0.13.0): every close below is ABORTIVE
// (SO_LINGER 0 => RST) on purpose. A clean FIN is delivered by the socket
// as a 0-byte read, which the library reports as an empty slice with no
// error and never closes on — while ServerSession.IsConnected is the local
// socket flag, so it stays true. TelnetSessionReader therefore reads a
// clean FIN as "live but quiet" forever: the handler never exits and the
// connection ghosts (slot + per-IP count + a 100ms read spin leak). An RST
// instead surfaces as an IOException through the read, which unwinds the
// handler into Disconnect — so RST disconnect IS covered here, and the FIN
// gap is tracked separately (upstream library behavior; see telnet.md).
[Collection("Ported")]
public sealed class TelnetServerIntegrationTests
{
    // Owns a live loopback stack: manager + TelnetServer on an ephemeral
    // port + the accept loop. Teardown cancels the loop first (it breaks on
    // OperationCanceledException), then stops the listener and the pool.
    private sealed class LiveStack : IDisposable
    {
        public ConnectionManager Manager = null!;
        public TelnetServer Server = null!;
        public Task LoopTask = Task.CompletedTask;
        public CancellationTokenSource Cts = new();
        public int Port => Server.Port;

        public void Dispose()
        {
            try { Cts.Cancel(); } catch { }
            try { LoopTask.Wait(TimeSpan.FromSeconds(10)); } catch { }
            try { Server.Stop(); } catch { }
            try { Server.Dispose(); } catch { }
            try { Manager.Atp.Stop(wait: false); } catch { }
            try { Cts.Dispose(); } catch { }
        }
    }

    private static async Task<LiveStack> StartAsync(AtherizSettings settings, X509Certificate2? cert = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var mgr = PortedHelpers.MakeManager(settings);
        var handoff = new ConcurrentQueue<string?>();
        var filter = TelnetProtocol.BuildAcceptFilter(mgr, handoff);
        var options = TelnetProtocol.BuildServerOptions(settings, cert, filter);
        var server = new TelnetServer(0, options);
        server.Start();
        var cts = new CancellationTokenSource();
        var loop = TelnetProtocol.AcceptLoopAsync(server, handoff, mgr, settings, cts.Token);
        await Task.Yield();
        return new LiveStack { Manager = mgr, Server = server, LoopTask = loop, Cts = cts };
    }

    private static readonly byte[] ClearScreen = [(byte)'\r', (byte)'\n', 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)'H', 0x1B, (byte)'[', (byte)'2', (byte)'J'];
    private static readonly byte[] DoTtype = [255, 253, 24];

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return false;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    // Reads until both markers arrive or the deadline passes. The opening
    // preset (DO TTYPE) goes out during NegotiateAsync and the clear-screen
    // prompt follows from HandleSessionAsync, so framing is two writes.
    private static async Task<byte[]> ReadUntilAsync(NetworkStream stream, byte[][] markers, int timeoutMs = 10000)
    {
        var buf = new List<byte>();
        var tmp = new byte[4096];
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!markers.All(m => Contains([.. buf], m)))
            {
                var n = await stream.ReadAsync(tmp, cts.Token);
                if (n == 0) break;
                buf.AddRange(tmp.Take(n));
            }
        }
        catch (OperationCanceledException) { }
        return [.. buf];
    }

    private static async Task<TelnetConnection?> WaitForSessionAsync(ConnectionManager mgr, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var snap = mgr.ConnectionsSnapshot;
            if (snap.Count == 1 && snap.Values.First() is TelnetConnection tc) return tc;
            await Task.Delay(25);
        }
        return null;
    }

    // Abortive close (RST, discards buffers): the only peer-close path the
    // 0.13.0 read contract surfaces, so the only one this suite asserts.
    // See the class comment for the clean-FIN gap.
    private static void CloseAbortive(TcpClient client)
    {
        try { client.LingerState = new LingerOption(true, 0); } catch { }
        try { client.Close(); } catch { }
    }

    private static async Task<bool> WaitForEmptyAsync(ConnectionManager mgr, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (mgr.ConnectionsSnapshot.Count == 0) return true;
            await Task.Delay(25);
        }
        return false;
    }

    [Fact]
    public async Task PlaintextLifecycle_PresetPromptTextRoundTripAndDisconnect()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        var got = new ConcurrentQueue<string>();
        using var stack = await StartAsync(settings);
        var mgr = stack.Manager;
        mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
        {
            if (a.Count > 0) got.Enqueue(a[0]?.ToString() ?? "");
        }));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, stack.Port);
        using var stream = client.GetStream();
        var opening = await ReadUntilAsync(stream, [DoTtype, ClearScreen]);
        Assert.True(Contains(opening, DoTtype), "expected DO TTYPE preset, got: " + Convert.ToHexString(opening));
        Assert.True(Contains(opening, ClearScreen), "expected clear-screen prompt, got: " + Convert.ToHexString(opening));

        var session = await WaitForSessionAsync(mgr);
        Assert.NotNull(session);

        var line = Encoding.UTF8.GetBytes("hello\r\n");
        await stream.WriteAsync(line);
        Assert.True(await PortedHelpers.WaitAsync(() => !got.IsEmpty, 10000), "text dispatch never arrived");
        Assert.Equal("hello", got.First());

        CloseAbortive(client);
        Assert.True(await WaitForEmptyAsync(mgr), "connection lingered after abortive close");
    }

    [Fact]
    public async Task BannedIp_RefusedPreSpawnWithNoBytes()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        using var stack = await StartAsync(settings);
        ObjectRegistry.BanIp("127.0.0.1");
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, stack.Port);
            using var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            // The filter refuses inside AcceptTcpAsync: the peer sees EOF
            // with no greeting bytes at all.
            Assert.Equal(-1, stream.ReadByte());
        }
        finally { ObjectRegistry.UnbanIp("127.0.0.1"); }
        Assert.Empty(stack.Manager.ConnectionsSnapshot);
    }

    [Fact]
    public async Task OverCap_RefusedPreSpawnKeepsExistingSession()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1", MaxTotalConnections = 1 };
        using var stack = await StartAsync(settings);
        var mgr = stack.Manager;
        Assert.True(mgr.RegisterConnection("dummy", new TestConnection("dummy")));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, stack.Port);
        using var stream = client.GetStream();
        stream.ReadTimeout = 5000;
        Assert.Equal(-1, stream.ReadByte());

        var snap = mgr.ConnectionsSnapshot;
        Assert.Single(snap);
        Assert.True(snap.ContainsKey("dummy"));
    }

    [Fact]
    public async Task Naws_ReportClampedLiveOntoSession()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings
        {
            TelnetInterface = "127.0.0.1",
            TelnetNawsMaxCols = 100,
            TelnetNawsMaxRows = 40,
        };
        var got = new ConcurrentQueue<string>();
        using var stack = await StartAsync(settings);
        var mgr = stack.Manager;
        mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
        {
            if (a.Count > 0) got.Enqueue(a[0]?.ToString() ?? "");
        }));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, stack.Port);
        using var stream = client.GetStream();
        _ = await ReadUntilAsync(stream, [ClearScreen]);
        var session = await WaitForSessionAsync(mgr);
        Assert.NotNull(session);

        // Absurd 5000x1000 report must land clamped to the configured max,
        // applied on the next received line.
        byte[] naws = [255, 250, 31, 0x13, 0x88, 0x03, 0xE8, 255, 240];
        await stream.WriteAsync(naws);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("nop\r\n"));
        Assert.True(await PortedHelpers.WaitAsync(() => !got.IsEmpty, 10000), "line after NAWS never dispatched");
        Assert.True(await PortedHelpers.WaitAsync(() => session.Session.TermWidth == 100, 5000), $"TermWidth={session.Session.TermWidth}");
        Assert.Equal(40, session.Session.TermHeight);

        CloseAbortive(client);
        Assert.True(await WaitForEmptyAsync(mgr), "connection lingered after abortive close");
    }

    [Fact]
    public async Task EchoToggle_FusesWillEchoAndPromptOverWire()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        using var stack = await StartAsync(settings);
        var mgr = stack.Manager;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, stack.Port);
        using var stream = client.GetStream();
        _ = await ReadUntilAsync(stream, [ClearScreen]);
        var session = await WaitForSessionAsync(mgr);
        Assert.NotNull(session);

        session.SendCommand("prompt_masked", new List<object?> { "Password: " });
        var masked = await ReadUntilAsync(stream, [[255, 251, 1], Encoding.UTF8.GetBytes("Password: ")]);
        Assert.True(Contains(masked, [255, 251, 1]), "expected IAC WILL ECHO, got: " + Convert.ToHexString(masked));
        Assert.True(Contains(masked, Encoding.UTF8.GetBytes("Password: ")), "expected fused prompt, got: " + Convert.ToHexString(masked));

        // RFC1143: our WILL ECHO is outstanding until the peer answers, and
        // the library queues a WONT behind an unacked WILL — so ACK with
        // IAC DO ECHO before asking for echo back, exactly as a real client
        // (which auto-replies to our DO/WILL preset) would.
        await stream.WriteAsync(new byte[] { 255, 253, 1 });
        await Task.Delay(250);
        session.SendCommand("echo_on");
        var bare = await ReadUntilAsync(stream, [[255, 252, 1]]);
        Assert.True(Contains(bare, [255, 252, 1]), "expected IAC WONT ECHO, got: " + Convert.ToHexString(bare));

        CloseAbortive(client);
        Assert.True(await WaitForEmptyAsync(mgr), "connection lingered after abortive close");
    }

    private static (string Dir, X509Certificate2 Cert) MakeCert()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("cn=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));
        var keyPem = rsa.ExportRSAPrivateKey();
        var certPem = cert.ExportCertificatePem();
        var keyBase64 = Convert.ToBase64String(keyPem, Base64FormattingOptions.InsertLineBreaks);
        File.WriteAllText(Path.Combine(dir, "key.pem"), $"-----BEGIN RSA PRIVATE KEY-----\n{keyBase64}\n-----END RSA PRIVATE KEY-----\n");
        File.WriteAllText(Path.Combine(dir, "cert.pem"), certPem);
        File.WriteAllText(Path.Combine(dir, "combined.pem"), certPem + File.ReadAllText(Path.Combine(dir, "key.pem")));
        var ctx = TelnetProtocol.BuildTelnetSslContext(new AtherizSettings { SslCertFile = Path.Combine(dir, "combined.pem") });
        Assert.NotNull(ctx);
        return (dir, ctx);
    }

    [Fact]
    public async Task TlsAndPlaintext_CoexistOnSamePort()
    {
        using var env = GlobalTestEnv.Enter();
        var (dir, cert) = MakeCert();
        try
        {
            var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
            var got = new ConcurrentQueue<string>();
            using var stack = await StartAsync(settings, cert);
            var mgr = stack.Manager;
            mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
            {
                if (a.Count > 0) got.Enqueue(a[0]?.ToString() ?? "");
            }));

            // Plaintext peer first: autodetect must let it through as-is.
            using var plain = new TcpClient();
            await plain.ConnectAsync(IPAddress.Loopback, stack.Port);
            using var pstream = plain.GetStream();
            var pOpening = await ReadUntilAsync(pstream, [ClearScreen]);
            Assert.True(Contains(pOpening, ClearScreen), "plaintext peer got no prompt: " + Convert.ToHexString(pOpening));
            await pstream.WriteAsync(Encoding.UTF8.GetBytes("via-plain\r\n"));
            Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "via-plain"), 10000), "plaintext line lost");

            // TLS peer on the same port.
            using var tls = new TcpClient();
            await tls.ConnectAsync(IPAddress.Loopback, stack.Port);
            using var ssl = new SslStream(tls.GetStream(), false, (_, _, _, _) => true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await ssl.AuthenticateAsClientAsync("localhost", null, false);
            var tOpening = new List<byte>();
            var tmp = new byte[4096];
            using var tcts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (!Contains([.. tOpening], ClearScreen))
                {
                    var n = await ssl.ReadAsync(tmp, tcts.Token);
                    if (n == 0) break;
                    tOpening.AddRange(tmp.Take(n));
                }
            }
            catch (OperationCanceledException) { }
            Assert.True(Contains([.. tOpening], ClearScreen), "TLS peer got no prompt: " + Convert.ToHexString([.. tOpening]));
            var tline = Encoding.UTF8.GetBytes("via-tls\r\n");
            await ssl.WriteAsync(tline, cts.Token);
            Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "via-tls"), 10000), "TLS line lost");

            Assert.True(await PortedHelpers.WaitAsync(() => mgr.ConnectionsSnapshot.Count == 2, 10000), "expected both peers registered");
            // No teardown assert here: the TLS session's close path is the
            // same upstream gap as plaintext FIN — transport errors inside
            // the decrypt loop collapse to -1/empty (ByteStreamHandler
            // TryReadByteCore maps IOException/InvalidOperation to -1), so
            // even an abortive close never surfaces. Plaintext RST teardown
            // IS covered by the tests above; TLS close rides the same
            // upstream fix. Just release the peers.
            ssl.Dispose();
            CloseAbortive(plain);
            CloseAbortive(tls);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
