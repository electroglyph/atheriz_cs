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
}
