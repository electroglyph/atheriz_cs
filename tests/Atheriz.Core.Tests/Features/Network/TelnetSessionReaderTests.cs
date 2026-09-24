using System.Text;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;
using telnet_cs.Server;
using telnet_cs.Transport;

namespace Atheriz.Core.Tests.Features.Network;

// TelnetSessionReader framing over live sessions: the engine's ReadCappedLines
// semantics (overlong-drop, split-CRLF holdback, \r\x00 strip, EOF tail) fed by
// session text instead of socket bytes.
[Collection("Ported")]
public sealed class TelnetSessionReaderTests
{
    private static TelnetServerOptions QuietOptions() => new()
    {
        TextEncoding = Encoding.UTF8,
        RequestCharacterSet = false,
        IdleTimeout = Timeout.InfiniteTimeSpan,
        HandshakeTimeout = Timeout.InfiniteTimeSpan,
        StatusInterval = null,
        Log = null,
    };

    private static async Task<List<string?>> CollectLinesAsync(
        TelnetSessionReader reader, int maxLine)
    {
        var lines = new List<string?>();
        // No WithCancellation here on purpose: the reader already carries the
        // stopping token and reports EOF on cancel, so a cancelled wrapper token
        // could only race the final MoveNextAsync into a throw.
        await foreach (var line in TelnetProtocol.ReadCappedLines(reader, maxLine))
        {
            lines.Add(line);
        }

        return lines;
    }

    private static Task<List<string?>> CollectLinesBoundedAsync(TelnetSessionReader reader, int maxLine) =>
        CollectLinesAsync(reader, maxLine).WaitAsync(TimeSpan.FromSeconds(15));

    [Fact]
    public async Task TelnetSessionReader_PlainLinesRoundTrip()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 65536);
            await peer.WriteAsync(Encoding.UTF8.GetBytes("hello\r\nworld\r\n"), 0, 7 + 7, cts.Token);
            peer.Close();
            var lines = await collect;
            Assert.Equal(new string?[] { "hello", "world" }, lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_SplitCrlfHoldback()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 65536);
            await peer.WriteAsync(Encoding.UTF8.GetBytes("ab\r"), 0, 3, cts.Token);
            await Task.Delay(150, cts.Token);
            await peer.WriteAsync(Encoding.UTF8.GetBytes("\ncd\r\n"), 0, 5, cts.Token);
            peer.Close();
            var lines = await collect;
            Assert.Equal(new string?[] { "ab", "cd" }, lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_OverlongDropsAndRecovers()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 32);
            var flood = new string('x', 50) + "\r\nfine\r\n";
            var bytes = Encoding.UTF8.GetBytes(flood);
            await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            peer.Close();
            var lines = await collect;
            Assert.Equal(new string?[] { null, "fine" }, lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_CrNulSplitAndEofTail()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 65536);
            var bytes = Encoding.UTF8.GetBytes("a\r\0b\ntail");
            await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            peer.Close();
            var lines = await collect;
            Assert.Equal(new string?[] { "a", "b", "tail" }, lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_CancelReadsAsEof()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 65536);
            await Task.Delay(150, cts.Token);
            cts.Cancel();
            var lines = await collect;
            Assert.Empty(lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_LeadBomNeverReachesDispatch()
    {
        // Boundary contract: a leading U+FEFF never reaches command dispatch.
        // The library preserves FEFF on the hermetic path (raw ReadAsync probe),
        // so this is the reader's one-shot preamble guard doing the work — kept
        // because the live socket path needs it (PortedServerIntegrationTests
        // telnet login fails neutered with a FEFF-prefixed command word).
        // This pins the dispatch-visible outcome end to end.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 65536);
            var bytes = Encoding.UTF8.GetBytes("\uFEFFcmd\r\n");
            await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            peer.Close();
            var lines = await collect;
            Assert.Equal(new string?[] { "cmd" }, lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_LaterBomPreserved()
    {
        // Only a leading FEFF is framing; an interior one is data (also
        // library-provided). Slice-coalescing independent: whether the writes
        // arrive as one slice or two, the first non-empty slice starts with
        // 'a' either way.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var collect = CollectLinesBoundedAsync(reader, 65536);
            var bytes = Encoding.UTF8.GetBytes("a\r\n\uFEFFb\r\n");
            await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            peer.Close();
            var lines = await collect;
            Assert.Equal(new string?[] { "a", "\uFEFFb" }, lines);
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_QuietThenData()
    {
        // Live-but-quiet slices are waited through, not reported as EOF: data
        // arriving after several empty 100 ms slices still delivers.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var buf = new char[16];
            var readTask = reader.ReadAsync(buf.AsMemory(), cts.Token).AsTask();
            await Task.Delay(350, cts.Token);
            Assert.False(readTask.IsCompleted, "quiet session must keep the read pending, not EOF");
            var bytes = Encoding.UTF8.GetBytes("hi\r\n");
            await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            var n = await readTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("hi\r\n", new string(buf, 0, n));
            peer.Close();
        }
        finally
        {
            peer.Dispose();
        }
    }

    // NOTE (probed 2026-09-17): the session layer drops a "\n" that immediately
    // follows "\r" at end-of-stream ("hello\r\n"+close arrives as "hello\r";
    // bare trailing "\n" survives). Dispatch-invisible — ReadCappedLines treats
    // "\r" and "\r\n" as the same terminator — so byte-exact tests below simply
    // avoid a trailing "\n" instead of pinning the upstream quirk.
    [Fact]
    public async Task TelnetSessionReader_PartialCarryAcrossSmallReads()
    {
        // A slice bigger than the destination is served across calls via the
        // carry cursor — no bytes lost or duplicated at the boundary.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reader = new TelnetSessionReader(session, cts.Token);
            var bytes = Encoding.UTF8.GetBytes("hello\r\nXY");
            await peer.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            peer.Close();
            var sb = new StringBuilder();
            var buf = new char[2];
            while (true)
            {
                var n = await reader.ReadAsync(buf.AsMemory(), cts.Token);
                if (n == 0) break;
                sb.Append(buf, 0, n);
            }
            Assert.Equal("hello\r\nXY", sb.ToString());
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_PreCancelledTokenReadsAsEof()
    {
        // A cancelled stopping token reports EOF instead of blocking: the
        // library maps a cancelled wait to empty, and the reader maps
        // empty-plus-cancelled to 0.
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            var reader = new TelnetSessionReader(session, new CancellationToken(true));
            var buf = new char[8];
            Assert.Equal(0, await reader.ReadAsync(buf.AsMemory()));
        }
        finally
        {
            peer.Dispose();
        }
    }

    [Fact]
    public async Task TelnetSessionReader_ArgumentValidation()
    {
        using var env = GlobalTestEnv.Enter();
        var (peer, serverStream) = InMemoryPipe.Create();
        using var session = new ServerSession(serverStream, QuietOptions(), CancellationToken.None);
        try
        {
            var reader = new TelnetSessionReader(session);
            await Assert.ThrowsAsync<ArgumentNullException>(() => reader.ReadAsync(null!, 0, 1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ReadAsync(new char[4], -1, 1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ReadAsync(new char[4], 0, -1));
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(new char[4], 3, 2));
            Assert.Equal(0, await reader.ReadAsync(Memory<char>.Empty));
        }
        finally
        {
            peer.Dispose();
        }
    }
}
