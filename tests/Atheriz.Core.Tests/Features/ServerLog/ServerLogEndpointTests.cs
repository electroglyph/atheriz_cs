using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Atheriz.Core.Tests.Features.ServerLog;

// Snapshot + live stream backing the landing-page server log.
[Collection("Ported")]
public class ServerLogEndpointTests
{
    private static Channel NewChannel()
    {
        ObjectRegistry.ClearAll();
        return new Channel();
    }

    [Fact]
    public void SnapshotBody_PrefixesWebTextWithEpoch()
    {
        var ch = NewChannel();
        try
        {
            ch.Msg("hello", null);
            var body = ServerLogEndpoints.SnapshotBody(ch);
            Assert.Contains("hello", body);
            Assert.DoesNotContain("[", body);
            var firstToken = body.Split(' ', 2)[0];
            Assert.True(long.TryParse(firstToken, out var epoch) && epoch > 0);
            Assert.Contains(ch.GetWebLines(100)[0].Text, body);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void SnapshotLastSeq_TracksNewestId_ZeroWhenEmpty()
    {
        var ch = NewChannel();
        try
        {
            Assert.Equal(0, ServerLogEndpoints.SnapshotLastSeq(ch));
            ch.Msg("first", null);
            ch.Msg("second", null);
            var lines = ch.GetWebLines(10);
            Assert.Equal(lines[^1].Seq, ServerLogEndpoints.SnapshotLastSeq(ch));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ResolveLastId_PrefersQuery_OverHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?lastId=7");
        ctx.Request.Headers["Last-Event-ID"] = "3";
        Assert.Equal(7, ServerLogEndpoints.ResolveLastId(ctx.Request));
    }

    [Fact]
    public void ResolveLastId_FallsBackToHeader_WhenNoQuery()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Last-Event-ID"] = "4";
        Assert.Equal(4, ServerLogEndpoints.ResolveLastId(ctx.Request));
    }

    [Fact]
    public void ResolveLastId_Zero_WhenNeitherPresent()
    {
        var ctx = new DefaultHttpContext();
        Assert.Equal(0, ServerLogEndpoints.ResolveLastId(ctx.Request));
    }

    [Fact]
    public async Task StreamAsync_ReplaysOnlyAfterQueryLastId()
    {
        var ch = NewChannel();
        try
        {
            ch.Msg("first", null);
            ch.Msg("second", null);
            long first = ch.GetHistoryLines(10)[0].Seq;
            var ctx = new DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();
            ctx.Request.QueryString = new QueryString($"?lastId={first}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = ServerLogEndpoints.StreamAsync(ctx, ch, cts.Token);
            string Output() => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!Output().Contains("second") && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            cts.Cancel();
            await stream;
            var text = Output();
            Assert.DoesNotContain("first", text);
            Assert.Contains("second", text);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public async Task StreamAsync_SnapshotSeq_ResumesWithoutReplay()
    {
        // End-to-end pin for the landing-page duplicate: snapshot, take its
        // X-Last-Seq, stream from it — the snapshot lines must not replay.
        var ch = NewChannel();
        try
        {
            ch.Msg("first", null);
            ch.Msg("second", null);
            long resume = ServerLogEndpoints.SnapshotLastSeq(ch);
            Assert.True(resume > 0);
            var ctx = new DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();
            ctx.Request.QueryString = new QueryString($"?lastId={resume}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = ServerLogEndpoints.StreamAsync(ctx, ch, cts.Token);
            await Task.Delay(300);
            cts.Cancel();
            await stream;
            var text = Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());
            Assert.DoesNotContain("first", text);
            Assert.DoesNotContain("second", text);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public async Task StreamAsync_OpensWithComment_BeforeFirstFrame()
    {
        // The stall fix: a caught-up ?lastId= must still produce bytes
        // immediately. StartAsync alone moved nothing on the wire, so the
        // open sends an SSE comment (ignored by clients, changes no
        // lastEventId) to force headers out — no waiting on the first
        // replay frame or the 20s keepalive.
        var ch = NewChannel();
        try
        {
            ch.Msg("first", null);
            long resume = ServerLogEndpoints.SnapshotLastSeq(ch);
            var ctx = new DefaultHttpContext();
            ctx.Request.QueryString = new QueryString($"?lastId={resume}");
            var body = new RecordingBodyFeature();
            ctx.Features.Set<IHttpResponseBodyFeature>(body);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = ServerLogEndpoints.StreamAsync(ctx, ch, cts.Token);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!body.Started && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.True(body.Started, "Stream must flush headers even with nothing to replay.");
            Assert.Equal("text/event-stream", ctx.Response.ContentType);
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (body.Written == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            var opening = body.ReadText();
            Assert.StartsWith(":", opening);
            Assert.DoesNotContain("id:", opening);
            cts.Cancel();
            await stream;
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void SnapshotRoute_ExposesLastSeqHeader()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Contains("X-Last-Seq", src);
    }

    [Fact]
    public void Routes_Registered_WithServerLogPublicGate()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Contains("app.MapGet(\"/server-log\",", src);
        Assert.Contains("app.MapGet(\"/server-log/stream\",", src);
        Assert.Contains("!settings.ServerLogPublic", src);
    }

    [Fact]
    public void LandingTemplate_LoadsServerLogViewerAsModule()
    {
        // The viewer bundle uses bare ES-module imports (xterm chunks),
        // so a classic script tag throws and the log never renders.
        var html = SourceScan.Read("src", "Atheriz.Server", "web", "templates", "index.html");
        Assert.Contains("<script type=\"module\" src=\"/static/assets/serverlog.js\">", html);
    }

    [Fact]
    public void ParseLastEventId_ParsesOrZero()
    {
        Assert.Equal(12, ServerLogEndpoints.ParseLastEventId("12"));
        Assert.Equal(0, ServerLogEndpoints.ParseLastEventId(null));
        Assert.Equal(0, ServerLogEndpoints.ParseLastEventId("abc"));
        Assert.Equal(0, ServerLogEndpoints.ParseLastEventId("-3"));
    }

    [Fact]
    public async Task WriteFrame_SplitsEmbeddedNewlines()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await ServerLogEndpoints.WriteFrameAsync(ctx.Response, 7, 1720000000, "a\nb", CancellationToken.None);
        var text = Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());
        Assert.Contains("id: 7\n", text);
        Assert.Contains("data: 1720000000 a\ndata: 1720000000 b\n", text);
    }

    [Fact]
    public async Task StreamAsync_ReplaysOnlyAfterLastEventId()
    {
        var ch = NewChannel();
        try
        {
            ch.Msg("first", null);
            ch.Msg("second", null);
            long first = ch.GetHistoryLines(10)[0].Seq;
            var ctx = new DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();
            ctx.Request.Headers["Last-Event-ID"] = first.ToString();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = ServerLogEndpoints.StreamAsync(ctx, ch, cts.Token);
            string Output() => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!Output().Contains("second") && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            cts.Cancel();
            await stream;
            var text = Output();
            Assert.Equal("text/event-stream", ctx.Response.ContentType);
            Assert.DoesNotContain("first", text);
            Assert.Contains("second", text);
            Assert.Contains($"id: {first + 1}\n", text);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public async Task StreamAsync_DeliversLiveMessage()
    {
        var ch = NewChannel();
        try
        {
            var ctx = new DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = ServerLogEndpoints.StreamAsync(ctx, ch, cts.Token);
            string Output() => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());
            // StreamAsync subscribes synchronously past the (empty) replay,
            // so a posted message from here is always observed live.
            await Task.Delay(200);
            ch.Msg("live", null);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!Output().Contains("live") && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            cts.Cancel();
            await stream;
            Assert.Contains("live", Output());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Records when headers flush, separately from body bytes: the stall fix
    // pins header-flush-before-first-body-byte, which a plain MemoryStream
    // body cannot observe. (HttpResponse.StartAsync routes through
    // IHttpResponseBodyFeature.StartAsync — there is no separate
    // start-feature type.)
    private sealed class RecordingBodyFeature : IHttpResponseBodyFeature
    {
        private readonly Pipe _pipe = new();
        private long _written;
        public bool Started;
        public long Written => Volatile.Read(ref _written);
        public Stream Stream => Stream.Null;
        public PipeWriter Writer => new CountingWriter(_pipe.Writer, this);
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }
        public Task CompleteAsync() => Task.CompletedTask;
        public void DisableBuffering() { }
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public string ReadText()
        {
            var sb = new StringBuilder();
            while (_pipe.Reader.TryRead(out var result))
            {
                sb.Append(Encoding.UTF8.GetString(result.Buffer.ToArray()));
                _pipe.Reader.AdvanceTo(result.Buffer.End);
            }
            return sb.ToString();
        }

        // Counts in Advance: every PipeWriter write path (GetSpan/Write/
        // WriteAsync) funnels through it exactly once.
        private sealed class CountingWriter(PipeWriter inner, RecordingBodyFeature owner) : PipeWriter
        {
            public override void Advance(int bytes)
            {
                if (bytes > 0) Interlocked.Add(ref owner._written, bytes);
                inner.Advance(bytes);
            }

            public override Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);
            public override Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);
            public override void CancelPendingFlush() => inner.CancelPendingFlush();
            public override void Complete(Exception? exception = null) => inner.Complete(exception);

            public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
                => inner.FlushAsync(cancellationToken);
        }
    }
}
