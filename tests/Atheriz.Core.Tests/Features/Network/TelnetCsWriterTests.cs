using System.Text;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;
using telnet_cs.Server;
using telnet_cs.Transport;

namespace Atheriz.Core.Tests.Features.Network;

// Hermetic pins for the telnet_cs bridge: TelnetCsWriter over InMemoryPipe sessions.
// No sockets; loopback/TLS admission lives in TelnetServerIntegrationTests.
[Collection("Ported")]
public sealed class TelnetCsWriterTests
{
    private static TelnetServerOptions QuietOptions(bool disableNegotiation = false) => new()
    {
        TextEncoding = Encoding.UTF8,
        RequestCharacterSet = false,
        IdleTimeout = Timeout.InfiniteTimeSpan,
        HandshakeTimeout = Timeout.InfiniteTimeSpan,
        StatusInterval = null,
        Log = null,
        DisableAllNegotiation = disableNegotiation,
    };

    private static byte[] ReadExactly(IByteStream peer, int count, int timeoutMs = 5000)
    {
        peer.ReceiveTimeout = timeoutMs;
        var buf = new byte[count];
        for (int i = 0; i < count; i++)
        {
            int b = peer.ReadByte();
            if (b < 0)
            {
                throw new IOException($"peer EOF after {i}/{count} bytes");
            }

            buf[i] = (byte)b;
        }

        return buf;
    }

    private static void AssertPeerSilent(IByteStream peer, int quietMs = 300)
    {
        peer.ReceiveTimeout = quietMs;
        Assert.Throws<IOException>(() => peer.ReadByte());
    }

    [Fact]
    public void TelnetCsWriter_TextDeliversDecodedBytes()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        using var conn = new TelnetConnection(new object(), new TelnetCsWriter(session, "1.2.3.4"));
        try
        {
            conn.SendCommand("text", new List<object?> { "hello" });
            Assert.Equal("hello", Encoding.UTF8.GetString(ReadExactly(peer, 5)));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_LoneLfNormalizedBeforeWire()
    {
        // TelnetText runs in OffloopWrite, not the writer: lone \n must arrive as \r\n.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        using var conn = new TelnetConnection(new object(), new TelnetCsWriter(session, "1.2.3.4"));
        try
        {
            conn.SendCommand("text", new List<object?> { "a\nb" });
            Assert.Equal("a\r\nb", Encoding.UTF8.GetString(ReadExactly(peer, 4)));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_PromptMaskedFusesWillEchoAndPrompt()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        using var conn = new TelnetConnection(new object(), new TelnetCsWriter(session, "1.2.3.4"));
        try
        {
            conn.SendCommand("prompt_masked", new List<object?> { "Password: " });
            var prompt = Encoding.UTF8.GetBytes("Password: ");
            var got = ReadExactly(peer, 3 + prompt.Length);
            Assert.Equal(new byte[] { 255, 251, 1 }, got[..3]);
            Assert.Equal(prompt, got[3..]);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetCsWriter_EchoOnAfterMaskedSendsWontEcho()
    {
        // Paired sequence (password prompt then restore): WILL moves negotiation
        // state, so the restoring WONT goes out on the wire.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        using var conn = new TelnetConnection(new object(), new TelnetCsWriter(session, "1.2.3.4"));
        try
        {
            conn.SendCommand("prompt_masked", new List<object?> { "Password: " });
            var prompt = Encoding.UTF8.GetBytes("Password: ");
            ReadExactly(peer, 3 + prompt.Length);
            // The WILL is outstanding (raw peer never auto-replies), so the restoring
            // WONT queues behind it per RFC 1143 and drains on the peer's reply —
            // the real client order (prompt, echo_on, then the client's DO).
            conn.SendCommand("echo_on");
            var doEcho = new byte[] { 255, 253, 1 };
            await peer.WriteAsync(doEcho, 0, doEcho.Length, CancellationToken.None);
            Assert.Equal(new byte[] { 255, 252, 1 }, ReadExactly(peer, 3, timeoutMs: 10000));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_EchoOnFromDefaultStateIsSilent()
    {
        // Deliberate idempotency delta vs the old always-send bytes: from the default
        // (never-suppressed) state there is no opposite request outstanding and the peer
        // is already in WONT, so the RFC 1143 machine emits nothing. Unobservable to
        // clients: a bare WONT ECHO against default state is a protocol no-op.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        using var conn = new TelnetConnection(new object(), writer);
        try
        {
            conn.SendCommand("echo_on");
            AssertPeerSilent(peer);
            Assert.False(conn.IsClosing);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_IdempotentEchoResendIsSilent()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        try
        {
            writer.Iac(251, 1);
            Assert.Equal(new byte[] { 255, 251, 1 }, ReadExactly(peer, 3));
            writer.Iac(251, 1);
            AssertPeerSilent(peer);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_DisableAllNegotiationSuppressesToggleButWritesPrompt()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(disableNegotiation: true), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        try
        {
            writer.IacWithText(251, 1, "hi");
            Assert.Equal("hi", Encoding.UTF8.GetString(ReadExactly(peer, 2)));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_OpeningPresetSendsDoTtype()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            session.SendOpeningPresetAsync().GetAwaiter().GetResult();
            Assert.Equal(new byte[] { 255, 253, 24 }, ReadExactly(peer, 3));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_NonEchoOptionThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.Iac(251, 31));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.IacWithText(251, 31, "hi"));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_UnknownCommandThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.Iac(253, 1));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_NullTextThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        try
        {
            Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
            Assert.Throws<ArgumentNullException>(() => writer.IacWithText(251, 1, null!));
            Assert.Throws<ArgumentNullException>(() => writer.SetExtCallback(31, null!));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_PeerHostSnapshotAndNullBuffer()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            Assert.Equal("10.9.8.7", new TelnetCsWriter(session, "10.9.8.7").GetPeerHost());
            var fallback = new TelnetCsWriter(session, null);
            Assert.Equal("?", fallback.GetPeerHost());
            Assert.Null(fallback.GetWriteBufferSize());
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_LimiterReleasedAfterInlineSend()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        using var conn = new TelnetConnection(new object(), new TelnetCsWriter(session, "1.2.3.4"));
        try
        {
            conn.SendCommand("text", new List<object?> { "hello" });
            Assert.Equal("hello", Encoding.UTF8.GetString(ReadExactly(peer, 5)));
            Assert.Equal(0, conn.PendingBytes);
            Assert.False(conn.IsClosing);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_OffloopSendDelivers()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        using var conn = new TelnetConnection(new object(), new TelnetCsWriter(session, "1.2.3.4"));
        try
        {
            var t = new Thread(() => conn.SendCommand("text", new List<object?> { "aloha" }));
            t.Start();
            t.Join();
            Assert.Equal("aloha", Encoding.UTF8.GetString(ReadExactly(peer, 5)));
            Assert.Equal(0, conn.PendingBytes);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public void TelnetCsWriter_ConcurrentWritesArriveWhole()
    {
        // Write serialization moved from the deleted TelnetStreamWriter lock
        // into the session send gate: concurrent writers must each land as one
        // contiguous frame, never byte-interleaved.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        var writer = new TelnetCsWriter(session, "1.2.3.4");
        const int threads = 8;
        const int perThread = 25;
        try
        {
            var tokens = Enumerable.Range(0, threads).Select(i => $"T{i:D2}!!").ToArray();
            var workers = tokens.Select(token => new Thread(() =>
            {
                for (int i = 0; i < perThread; i++) writer.Write(token);
            })).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join(TimeSpan.FromSeconds(15)));
            Assert.All(workers, t => Assert.False(t.IsAlive));
            var got = Encoding.UTF8.GetString(ReadExactly(peer, threads * perThread * 5));
            var frames = Enumerable.Range(0, threads * perThread).Select(i => got.Substring(i * 5, 5)).ToList();
            var expected = tokens.SelectMany(token => Enumerable.Repeat(token, perThread)).OrderBy(x => x).ToList();
            Assert.Equal(expected, frames.OrderBy(x => x).ToList());
        }
        finally
        {
            peer.Dispose();
        }
    }
}
