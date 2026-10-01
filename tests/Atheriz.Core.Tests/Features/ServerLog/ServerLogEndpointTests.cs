using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;

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
}
