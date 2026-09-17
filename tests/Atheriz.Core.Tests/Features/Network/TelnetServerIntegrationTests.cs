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
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using telnet_cs;
using telnet_cs.Server;

namespace Atheriz.Core.Tests.Features.Network;

// Loopback integration over the real accept path: raw TCP peers against
// TelnetProtocol.BuildAcceptFilter + BuildServerOptions + AcceptLoopAsync.
// Complements the hermetic InMemoryPipe pins (TelnetCsWriterTests /
// TelnetSessionReaderTests) by proving the wiring — filter, loop, NAWS
// re-apply, echo toggles, TLS autodetect — end to end.
//
// Close-path contract (telnet_cs 0.14.0): both peer-close paths are
// asserted. A clean FIN (plaintext `Shutdown(Send)`, TLS `close_notify`)
// is observed by the library and unwinds the handler into Disconnect; an
// abortive RST (SO_LINGER 0) flips the local Connected flag false with the
// same outcome. Pre-0.14.0 the clean FIN ghosted (slot + per-IP count +
// read spin leaked); see telnet.md.
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

    private static async Task<List<TelnetConnection>> WaitForSessionsAsync(ConnectionManager mgr, int n, int timeoutMs = 10000)    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var snap = mgr.ConnectionsSnapshot;
            if (snap.Count == n && snap.Values.All(v => v is TelnetConnection))
                return snap.Values.Cast<TelnetConnection>().ToList();
            await Task.Delay(25);
        }
        return [];
    }

    [Fact]
    public async Task ConcurrentClients_IndependentSessionsAndTeardown()
    {
        // The handoff correlation must hold under concurrent accepts: every
        // session lands labeled with its peer host, dispatches route to the
        // right connection, and all tear down cleanly.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings
        {
            TelnetInterface = "127.0.0.1",
            MaxConnectionsPerIp = 10,
            MaxTotalConnections = 100,
        };
        var got = new ConcurrentQueue<(string Conn, string Line)>();
        using var stack = await StartAsync(settings);
        var mgr = stack.Manager;
        mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
        {
            if (a.Count > 0) got.Enqueue((c.SessionId ?? "?", a[0]?.ToString() ?? ""));
        }));

        const int peers = 4;
        var clients = new List<TcpClient>();
        var streams = new List<NetworkStream>();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, peers).Select(async _ =>
            {
                var client = new TcpClient();
                lock (clients) clients.Add(client);
                await client.ConnectAsync(IPAddress.Loopback, stack.Port);
            }));
            foreach (var client in clients)
            {
                var stream = client.GetStream();
                streams.Add(stream);
                var opening = await ReadUntilAsync(stream, [ClearScreen]);
                Assert.True(Contains(opening, ClearScreen), "peer got no prompt: " + Convert.ToHexString(opening));
            }
            var sessions = await WaitForSessionsAsync(mgr, peers);
            Assert.Equal(peers, sessions.Count);
            Assert.Equal(peers, sessions.Select(s => s.SessionId).Distinct().Count());
            Assert.All(sessions, s => Assert.Equal("127.0.0.1", s.ClientHost));

            await Task.WhenAll(streams.Select((stream, i) =>
                stream.WriteAsync(Encoding.UTF8.GetBytes($"hello-{i}\r\n")).AsTask()));
            Assert.True(await PortedHelpers.WaitAsync(() => got.Count == peers, 10000),
                $"only {got.Count}/{peers} lines dispatched");
            Assert.Equal(peers, got.Select(g => g.Line).Distinct().Count());
        }
        finally
        {
            foreach (var stream in streams) try { stream.Dispose(); } catch { }
            foreach (var client in clients) CloseAbortive(client);
            foreach (var client in clients) try { client.Dispose(); } catch { }
        }
        Assert.True(await WaitForEmptyAsync(mgr), "connections lingered after concurrent teardown");
    }

    [Fact]
    public async Task PostNegotiationBan_RefusedWithoutSession()
    {
        // Banned between filter and handler (the TOCTOU window): HandleSessionAsync
        // must refuse with no registration and no dispatch, even past the filter.
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        const string host = "127.0.0.2";
        ObjectRegistry.BanIp(host);
        try
        {
            var ready = new ConcurrentQueue<string>();
            mgr.RegisterHandler("client_ready", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
            {
                ready.Enqueue(c.SessionId ?? "?");
            }));
            var (peer, serverStream) = telnet_cs.Transport.InMemoryPipe.Create();
            using var session = new ServerSession(serverStream, QuietOptionsForHandle(), CancellationToken.None);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await TelnetProtocol.HandleSessionAsync(session, host, mgr, new AtherizSettings(), cts.Token)
                    .WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { try { peer.Dispose(); } catch { } }
            Assert.Empty(mgr.ConnectionsSnapshot);
            await Task.Delay(500);
            Assert.Empty(ready);
        }
        finally
        {
            ObjectRegistry.UnbanIp(host);
            try { mgr.Atp.Stop(wait: false); } catch { }
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    [Fact]
    public async Task CapRaceAtHandle_RefusedWithoutSession()
    {
        // Lost the cap race after passing the filter: RegisterConnection refuses
        // inside HandleSessionAsync — no second session, no dispatch, no hang.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { MaxTotalConnections = 1 };
        var mgr = PortedHelpers.MakeManager(settings);
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            Assert.True(mgr.RegisterConnection("dummy", new TestConnection("dummy")));
            var ready = new ConcurrentQueue<string>();
            mgr.RegisterHandler("client_ready", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
            {
                ready.Enqueue(c.SessionId ?? "?");
            }));
            var (peer, serverStream) = telnet_cs.Transport.InMemoryPipe.Create();
            using var session = new ServerSession(serverStream, QuietOptionsForHandle(), CancellationToken.None);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await TelnetProtocol.HandleSessionAsync(session, "127.0.0.3", mgr, settings, cts.Token)
                    .WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { try { peer.Dispose(); } catch { } }
            var snap = mgr.ConnectionsSnapshot;
            Assert.Single(snap);
            Assert.True(snap.ContainsKey("dummy"));
            await Task.Delay(500);
            Assert.Empty(ready);
        }
        finally
        {
            try { mgr.Atp.Stop(wait: false); } catch { }
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    private static TelnetServerOptions QuietOptionsForHandle() => new()
    {
        TextEncoding = Encoding.UTF8,
        RequestCharacterSet = false,
        IdleTimeout = Timeout.InfiniteTimeSpan,
        HandshakeTimeout = Timeout.InfiniteTimeSpan,
        StatusInterval = null,
        Log = null,
    };

    [Fact]
    public async Task CleanFinChurn_ReleasesSlotEveryCycle()
    {
        // Five sequential graceful closes must each unwind: with the default
        // per-IP cap of 2, any leaked slot would refuse cycle 3+.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        using var stack = await StartAsync(settings);
        var mgr = stack.Manager;
        for (int i = 0; i < 5; i++)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, stack.Port);
            using var stream = client.GetStream();
            var opening = await ReadUntilAsync(stream, [ClearScreen]);
            Assert.True(Contains(opening, ClearScreen), $"cycle {i}: no prompt, slot leaked");
            var session = await WaitForSessionAsync(mgr);
            Assert.NotNull(session);
            CloseClean(client);
            Assert.True(await WaitForEmptyAsync(mgr), $"cycle {i}: connection ghosted after clean FIN");
        }
    }

    // Abortive close (RST, discards buffers): the pre-0.14.0 era's only
    // surfacing close path, still covered as the hostile-peer case.
    private static void CloseAbortive(TcpClient client)
    {
        try { client.LingerState = new LingerOption(true, 0); } catch { }
        try { client.Close(); } catch { }
    }

    // Clean FIN on the send direction only: the socket stays open for
    // reading (so the server's own FIN reply has somewhere to go) while the
    // server must observe our EOF. Shutdown is explicit, so unlike Close it
    // cannot turn into an RST from unread greeting bytes.
    private static void CloseClean(TcpClient client)
    {
        try { client.Client.Shutdown(SocketShutdown.Send); } catch { }
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
    public async Task CleanFin_DisconnectsSession()
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

        // Graceful close: FIN on our send direction, socket held open.
        // Pre-0.14.0 the server ghosted here (empty slices + Connected
        // stuck true); now the handler must unwind into Disconnect.
        CloseClean(client);
        Assert.True(await WaitForEmptyAsync(mgr), "connection ghosted after clean FIN");
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
            // Arm RST-before-SslStream-teardown: SslStream.Dispose closes the
            // socket first (a FIN the server ghosts on), so the linger must be
            // armed here for the close below to actually reset.
            tls.LingerState = new LingerOption(true, 0);
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
            // RST teardown on both peers (linger pre-armed above for TLS, so
            // the socket resets instead of FIN-closing through SslStream).
            tls.Close();
            ssl.Dispose();
            CloseAbortive(plain);
            Assert.True(await WaitForEmptyAsync(mgr), "connections lingered after abortive close");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TlsCloseNotify_DisconnectsSession()
    {
        using var env = GlobalTestEnv.Enter();
        var (dir, cert) = MakeCert();
        try
        {
            var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
            using var stack = await StartAsync(settings, cert);
            var mgr = stack.Manager;
            var got = new ConcurrentQueue<string>();
            mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
            {
                if (a.Count > 0) got.Enqueue(a[0]?.ToString() ?? "");
            }));

            using var tls = new TcpClient();
            await tls.ConnectAsync(IPAddress.Loopback, stack.Port);
            using var ssl = new SslStream(tls.GetStream(), false, (_, _, _, _) => true);
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
            Assert.True(Contains([.. tOpening], ClearScreen), "TLS peer got no prompt");
            var session = await WaitForSessionAsync(mgr);
            Assert.NotNull(session);

            // A full round-trip first: the session must be live and past any
            // handshake tracking before the close, so only the close itself
            // can explain the disconnect below.
            var tline = Encoding.UTF8.GetBytes("via-tls\r\n");
            await ssl.WriteAsync(tline);
            Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "via-tls"), 10000), "TLS line lost");

            // Graceful TLS close: send close_notify but never await the
            // reciprocal notify the server never sends, and hold the raw
            // socket open so the server observes decrypt-EOS rather than
            // TCP FIN. Pre-0.14.0 this ghosted like plaintext FIN.
            _ = ssl.ShutdownAsync().ContinueWith(t => t.Exception, TaskScheduler.Default);
            Assert.True(await WaitForEmptyAsync(mgr), "TLS connection ghosted after close_notify");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }

    [Fact]
    public async Task HandlerPipeline_ClientReadyEmptySkipOverlongThrottle()
    {
        // The live line pipeline: client_ready fires once, blank/whitespace lines
        // never dispatch, an overlong line drops with ONE throttled warning (never
        // its bytes), and the stream recovers on the next line.
        using var env = GlobalTestEnv.Enter();
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = logDir });
        try
        {
            var settings = new AtherizSettings { TelnetInterface = "127.0.0.1", TelnetMaxLine = 16 };
            var got = new ConcurrentQueue<string>();
            var ready = new ConcurrentQueue<string>();
            using var stack = await StartAsync(settings);
            var mgr = stack.Manager;
            mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
            {
                if (a.Count > 0) got.Enqueue(a[0]?.ToString() ?? "");
            }));
            mgr.RegisterHandler("client_ready", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
            {
                ready.Enqueue(c.SessionId ?? "?");
            }));

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, stack.Port);
            using var stream = client.GetStream();
            _ = await ReadUntilAsync(stream, [ClearScreen]);
            var session = await WaitForSessionAsync(mgr);
            Assert.NotNull(session);
            Assert.True(await PortedHelpers.WaitAsync(() => !ready.IsEmpty, 10000), "client_ready never dispatched");
            Assert.Equal(session.SessionId, ready.First());

            string log;
            using (var cap = new CaptureAtherizLog())
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("\r\n   \r\n"));
                await Task.Delay(750);
                Assert.Empty(got);
                var floodX = new string('x', 64) + "\r\nfine\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(floodX));
                await stream.WriteAsync(Encoding.UTF8.GetBytes(new string('y', 64) + "\r\n"));
                Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "fine"), 10000),
                    "stream did not recover after overlong drop");
                await Task.Delay(250);
                log = cap.Read();
            }
            Assert.Equal("fine", got.Single());
            Assert.DoesNotContain(new string('x', 64), log);
            Assert.DoesNotContain(new string('y', 64), log);
            Assert.Equal(1, CountOccurrences(log, "[Telnet] dropped overlong input line"));

            CloseAbortive(client);
            Assert.True(await WaitForEmptyAsync(mgr), "connection lingered after abortive close");
        }
        finally
        {
            try { AtherizLogger.ApplySettings(); } catch { }
            try { Directory.Delete(logDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Naws_VariantsApplyLive()
    {
        // NAWS re-apply against custom bounds: in-bounds lands verbatim, a zero
        // report is ignored, and out-of-range clamps to the configured min.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings
        {
            TelnetInterface = "127.0.0.1",
            TelnetNawsMinCols = 40,
            TelnetNawsMaxCols = 200,
            TelnetNawsMinRows = 10,
            TelnetNawsMaxRows = 50,
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

        static byte[] Naws(int cols, int rows) =>
            [255, 250, 31, (byte)(cols >> 8), (byte)cols, (byte)(rows >> 8), (byte)rows, 255, 240];

        await stream.WriteAsync(Naws(100, 40));
        await stream.WriteAsync(Encoding.UTF8.GetBytes("one\r\n"));
        Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "one"), 10000), "line after NAWS never dispatched");
        Assert.True(await PortedHelpers.WaitAsync(() => session.Session.TermWidth == 100, 5000), $"TermWidth={session.Session.TermWidth}");
        Assert.Equal(40, session.Session.TermHeight);

        await stream.WriteAsync(Naws(0, 0));
        await stream.WriteAsync(Encoding.UTF8.GetBytes("two\r\n"));
        Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "two"), 10000), "line after zero NAWS never dispatched");
        await Task.Delay(500);
        Assert.Equal(100, session.Session.TermWidth);
        Assert.Equal(40, session.Session.TermHeight);

        await stream.WriteAsync(Naws(10, 5));
        await stream.WriteAsync(Encoding.UTF8.GetBytes("three\r\n"));
        Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "three"), 10000), "line after small NAWS never dispatched");
        Assert.True(await PortedHelpers.WaitAsync(() => session.Session.TermWidth == 40, 5000), $"TermWidth={session.Session.TermWidth}");
        Assert.Equal(10, session.Session.TermHeight);

        CloseAbortive(client);
        Assert.True(await WaitForEmptyAsync(mgr), "connection lingered after abortive close");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitForTcpAsync(IPAddress addr, int port, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(addr, port);
                try { probe.LingerState = new LingerOption(true, 0); } catch { }
                return true;
            }
            catch { await Task.Delay(100); }
        }
        return false;
    }

    private static async Task<bool> IsTcpOpenAsync(IPAddress addr, int port)
    {
        try
        {
            using var probe = new TcpClient();
            await probe.ConnectAsync(addr, port).WaitAsync(TimeSpan.FromSeconds(3));
            try { probe.LingerState = new LingerOption(true, 0); } catch { }
            return true;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Setup_IHostPath_ServesTelnetAndStopsWithHost()
    {
        // The real IHost wiring (not the lifespan-composition branch): Setup starts
        // a serving listener, and host shutdown stops it via ApplicationStopping.
        using var env = GlobalTestEnv.Enter();
        var port = FreePort();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1", TelnetPort = port };
        var mgr = PortedHelpers.MakeManager(settings);
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddSingleton(settings);
                s.AddSingleton(mgr);
            })
            .Build();
        try
        {
            new TelnetProtocol().Setup(host);
            await host.StartAsync();
            Assert.True(await WaitForTcpAsync(IPAddress.Loopback, port), "telnet never listened via Setup");
            Assert.True(await WaitForEmptyAsync(mgr), "probe session lingered");

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            var opening = await ReadUntilAsync(stream, [ClearScreen]);
            Assert.True(Contains(opening, ClearScreen), "no prompt via Setup-started server: " + Convert.ToHexString(opening));
            CloseAbortive(client);
            Assert.True(await WaitForEmptyAsync(mgr), "session lingered");

            await host.StopAsync();
            await Task.Delay(500);
            Assert.False(await IsTcpOpenAsync(IPAddress.Loopback, port), "telnet still listening after host stop");
        }
        finally
        {
            try { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            try { host.Dispose(); } catch { }
            try { mgr.Atp.Stop(wait: false); } catch { }
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    [Fact]
    public async Task Setup_IHostPath_SkippedWhenDisabled()
    {
        using var env = GlobalTestEnv.Enter();
        var port = FreePort();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1", TelnetPort = port, TelnetEnabled = false };
        var mgr = PortedHelpers.MakeManager(settings);
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddSingleton(settings);
                s.AddSingleton(mgr);
            })
            .Build();
        try
        {
            new TelnetProtocol().Setup(host);
            await host.StartAsync();
            await Task.Delay(1000);
            Assert.False(await IsTcpOpenAsync(IPAddress.Loopback, port), "telnet listened while disabled");
            await host.StopAsync();
        }
        finally
        {
            try { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            try { host.Dispose(); } catch { }
            try { mgr.Atp.Stop(wait: false); } catch { }
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    [Fact]
    public async Task AcceptLoop_DisposedServer_BreaksPromptly()
    {
        // Teardown ordering: a stopped listener with a cancelled token must break
        // the accept loop (not spin on ObjectDisposed or hang in AcceptTcpAsync).
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
            var handoff = new ConcurrentQueue<string?>();
            var filter = TelnetProtocol.BuildAcceptFilter(mgr, handoff);
            var options = TelnetProtocol.BuildServerOptions(settings, null, filter);
            using var server = new TelnetServer(0, options);
            server.Start();
            server.Stop();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await TelnetProtocol.AcceptLoopAsync(server, handoff, mgr, settings, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { try { mgr.Atp.Stop(wait: false); } catch { } }
    }

    [Fact]
    public async Task TlsHandshakeTimeout_DropsStalledPeerAndSurvives()
    {
        // A peer that starts TLS (0x16) then stalls past the 10 s handshake deadline
        // is dropped by the accept path, and the loop keeps serving afterwards.
        // Covers both surfacing races: TimeoutException→warn+continue AND
        // OperationCanceledException→continue (the library's linked CTS can win the
        // race against its own TimeoutException under load; the loop used to break
        // on that path and silently stop listening — full-suite failure with
        // loop done=True, no fault, neither arm logged).
        // Debug capture identifies the accept arm on failure: "handshake timeout"
        // (TimeoutException→continue) vs "accept failed" (InvalidOperation→break).
        using var env = GlobalTestEnv.Enter();
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = logDir });
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

            string log;
            using (var cap = new CaptureAtherizLog())
            {
                using var stalled = new TcpClient();
                await stalled.ConnectAsync(IPAddress.Loopback, stack.Port);
                using var sstream = stalled.GetStream();
                sstream.ReadTimeout = 25000;
                await sstream.WriteAsync(new byte[] { 0x16 });
                // The server must close the stalled handshake itself (EOF, not a
                // read-timeout IOException which would mean we are still waiting).
                Assert.Equal(-1, sstream.ReadByte());
                Assert.Empty(mgr.ConnectionsSnapshot);

                using var good = new TcpClient();
                await good.ConnectAsync(IPAddress.Loopback, stack.Port);
                using var gstream = good.GetStream();
                var opening = await ReadUntilAsync(gstream, [ClearScreen], timeoutMs: 20000);
                var dbg = cap.Read();
                Assert.True(Contains(opening, ClearScreen),
                    $"loop did not survive the handshake timeout; loop done={stack.LoopTask.IsCompleted} " +
                    $"loop fault={stack.LoopTask.Exception?.GetBaseException().Message} " +
                    $"handshake-timeouts={CountOccurrences(dbg, "handshake timeout")} " +
                    $"accept-failed={CountOccurrences(dbg, "accept failed")}");
                await gstream.WriteAsync(Encoding.UTF8.GetBytes("after-timeout\r\n"));
                Assert.True(await PortedHelpers.WaitAsync(() => got.Any(s => s == "after-timeout"), 10000), "good peer lost");
                CloseAbortive(good);
                Assert.True(await WaitForEmptyAsync(mgr), "connections lingered");
                log = cap.Read();
            }
            Assert.True(log.Contains("handshake timeout", StringComparison.Ordinal)
                || log.Contains("accept cancelled without shutdown", StringComparison.Ordinal),
                "expected a known continue-arm trace; log tail: " + log.Substring(Math.Max(0, log.Length - 2000)));
            Assert.DoesNotContain("accept failed", log);
        }
        finally
        {
            try { AtherizLogger.ApplySettings(); } catch { }
            try { Directory.Delete(logDir, true); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task OpeningPreset_IsExactlyDoTtype()
    {
        // Wire lock: with charset negotiation off and no peer TTYPE answer, the
        // only negotiation bytes before our clear-screen prompt are DO TTYPE.
        // A library upgrade adding preset bytes fails here deliberately.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        using var stack = await StartAsync(settings);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, stack.Port);
        using var stream = client.GetStream();
        var opening = await ReadUntilAsync(stream, [ClearScreen]);
        Assert.True(Contains(opening, ClearScreen), "no prompt: " + Convert.ToHexString(opening));
        var idx = IndexOf(opening, ClearScreen);
        Assert.True(idx >= 0, "clear-screen marker missing");
        Assert.Equal(DoTtype, opening[..idx]);
        CloseAbortive(client);
        Assert.True(await WaitForEmptyAsync(stack.Manager), "connection lingered");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
