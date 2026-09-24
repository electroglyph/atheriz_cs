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

// Telnet transport behavior: read errors, write deadlines, and capped line parsing.
[Collection("Ported")]
public class TelnetConnectionTests
{
    // --- Telnet parsing/write without sockets where possible ---

    private sealed class FaultReader : System.IO.TextReader
    {
        public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
            throw new IOException("boom");
    }

    [Fact]
    public async Task Telnet_FaultingReader_SurfacesError_NotCleanEof()
    {
        // Behavior: a read error must surface to the caller, not convert via
        // catch{read=0} into clean EOF that looks like a graceful disconnect.
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in TelnetProtocol.ReadCappedLines(new FaultReader(), 100)) { }
        });
    }

    [Fact]
    public async Task Telnet_OverlongLine_YieldsNullThenRecovers()
    {
        // Pin: an overlong line is reported as null (dropped) and the stream
        // continues with the next line.
        var lines = new List<string?>();
        await foreach (var l in TelnetProtocol.ReadCappedLines(
            new System.IO.StringReader("ok\n" + new string('y', 200) + "\nrecovered\n"), 100))
            lines.Add(l);
        Assert.Equal(new string?[] { "ok", null, "recovered" }, lines);
    }

    [Fact]
    public async Task Telnet_Write_ToNonReadingPeer_ReturnsPromptly()
    {
        // Behavior: writes run on the game thread with a deadline — a peer
        // that never drains must not stall the worker forever. The bound moved
        // from the deleted TelnetStreamWriter socket SendTimeout into
        // TelnetCsWriter.RunWrite (5 s) over the session write.
        using var env = GlobalTestEnv.Enter();
        var options = new telnet_cs.Server.TelnetServerOptions
        {
            TextEncoding = System.Text.Encoding.UTF8,
            RequestCharacterSet = false,
            DisableAllNegotiation = true,
            IdleTimeout = Timeout.InfiniteTimeSpan,
            HandshakeTimeout = Timeout.InfiniteTimeSpan,
            StatusInterval = null,
            Log = null,
        };
        using var server = new telnet_cs.Server.TelnetServer(0, options);
        server.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var acceptTask = server.AcceptTcpAsync(cts.Token);
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, server.Port);
            var pending = await acceptTask.WaitAsync(TimeSpan.FromSeconds(10));
            // NegotiateAsync returns the same instance it is given, so only
            // the negotiated session is disposed (the library Dispose is not
            // idempotent).
            telnet_cs.Server.ServerSession session;
            try
            {
                session = await server.NegotiateAsync(pending, cts.Token).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                try { pending.Dispose(); } catch { }
                throw;
            }
            var writer = new TelnetCsWriter(session, "127.0.0.1");
            try
            {
                var big = new string('x', 8 << 20);
                var writeTask = Task.Run(() => writer.Write(big));
                // Prompt return AND the right fault: the 5 s RunWrite deadline must
                // surface as IOException("telnet write timed out") — the old
                // SocketException shape — so a wedged peer closes the connection
                // instead of parking the game thread (an unobserved fault or a
                // hang would both be worse).
                try { await writeTask.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception) { }
                Assert.True(writeTask.IsCompleted, "write to a non-draining peer must not block the game thread indefinitely");
                Assert.True(writeTask.IsFaulted, "8 MiB into an undrained peer must exceed the write deadline");
                var io = Assert.IsType<IOException>(writeTask.Exception!.InnerException);
                Assert.Contains("telnet write timed out", io.Message);
            }
            finally
            {
                writer.Dispose();
            }
        }
        finally
        {
            try { server.Stop(); } catch { }
        }
    }

    // --- Parser complexity ---

    [Fact]
    public async Task Telnet_LongLine_ParsesInLinearTime()
    {
        // Behavior: line parsing must be linear, not O(n²) (buf += chunk /
        // Substring per line). An 8 MB single line must parse in linear time
        // (quadratic needs ~10 s+ here; linear needs < 1 s).
        var input = new string('a', 8 << 20) + "\n";
        var sw = Stopwatch.StartNew();
        int count = 0;
        await foreach (var line in TelnetProtocol.ReadCappedLines(new System.IO.StringReader(input), 1 << 24))
        {
            count++;
            Assert.NotNull(line);
        }
        sw.Stop();
        Assert.Equal(1, count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"8 MB line took {sw.Elapsed} (quadratic)");
    }
}
