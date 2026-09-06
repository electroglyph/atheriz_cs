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
    public void Telnet_Write_ToNonReadingPeer_ReturnsPromptly()
    {
        // Behavior: writes run on the game thread with a timeout — a peer
        // that never drains must not stall the worker forever.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        TcpClient? server = null;
        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            server = listener.AcceptTcpClient();
            var writer = new TelnetStreamWriter(server.GetStream(), server);
            var big = new string('x', 8 << 20);
            var writeTask = Task.Run(() => writer.Write(big));
            // Completed promptly (a bounded SocketException counts — the point
            // is it never hangs); an unobserved fault would be worse.
            bool done;
            try { done = writeTask.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { done = true; }
            if (writeTask.IsFaulted) _ = writeTask.Exception;
            Assert.True(done, "write to a non-draining peer must not block the game thread indefinitely");
        }
        finally
        {
            try { server?.Close(); } catch { }
            try { client?.Close(); } catch { }
            try { listener.Stop(); } catch { }
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
