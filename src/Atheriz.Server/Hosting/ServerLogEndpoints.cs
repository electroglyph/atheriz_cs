using System.Threading.Channels;
using Microsoft.AspNetCore.Http;

namespace Atheriz.Server.Hosting;

// Read-only server-channel log for the landing page: an epoch-prefixed
// text snapshot plus a live event stream. The viewer renders each epoch in
// its own timezone; in-game delivery still uses the server-formatted
// bytes. Anonymous by design (the page is public); the routes 404 unless
// ServerLogPublic is enabled.
public static class ServerLogEndpoints
{
    // Snapshot depth for "up to last 100 messages". GetHistory clamps to
    // ChannelHistoryLimit anyway, so this is a ceiling, not a promise.
    public const int MaxSnapshotLines = 100;

    public static string SnapshotBody(Atheriz.Core.Objects.Channel channel)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (_, timestamp, text) in channel.GetWebLines(MaxSnapshotLines))
        {
            sb.Append(timestamp);
            sb.Append(' ');
            sb.Append(text);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // Newest sequence id held in history, 0 when empty. The snapshot body
    // carries no ids (epoch + text only), so /server-log exposes this as
    // X-Last-Seq and the viewer passes it back as ?lastId= to open the
    // stream without replaying the lines it just rendered.
    public static long SnapshotLastSeq(Atheriz.Core.Objects.Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var last = channel.GetWebLines(1);
        return last.Count > 0 ? last[0].Seq : 0;
    }

    public static long ParseLastEventId(string? header)
        => long.TryParse(header, out var id) && id > 0 ? id : 0;

    // Initial EventSource requests carry no Last-Event-ID header (the
    // browser only sends it on reconnect), so the viewer hands back the
    // snapshot's X-Last-Seq as ?lastId=. Take the newest of the two: on a
    // reconnect the query id is frozen at snapshot time while the header
    // tracks the newest delivered frame — resuming from the stale query
    // id would replay lines the viewer already rendered.
    public static long ResolveLastId(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        long id = ParseLastEventId(request.Headers["Last-Event-ID"].ToString());
        string[] queryKeys = ["lastId", "after", "lastEventId"];
        foreach (var key in queryKeys)
            id = Math.Max(id, ParseLastEventId(request.Query[key].ToString()));
        return id;
    }

    // Headers must reach the client the instant the stream opens: with no
    // replay pending (a caught-up ?lastId=) the first body byte used to be
    // the 20s keepalive, and the headers sat unsent until then — leaving
    // EventSource stuck in CONNECTING with zero bytes, which some browsers
    // fail instead of riding out. StartAsync alone did not move bytes on
    // the wire here, so an initial SSE comment (protocol-legal, ignored by
    // clients, changes no lastEventId) forces the first flush as well.
    public static async Task SendHeadersAsync(HttpResponse response, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        await response.StartAsync(ct);
        await response.WriteAsync(": connected\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteFrameAsync(HttpResponse response, long seq, long timestamp, string text, CancellationToken ct)
    {
        await response.WriteAsync($"id: {seq}\n", ct);
        foreach (var line in text.Split('\n'))
            await response.WriteAsync($"data: {timestamp} {line}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteKeepaliveAsync(HttpResponse response, CancellationToken ct)
    {
        await response.WriteAsync(": ping\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    // Replay-then-live: entries after the client's resume point first
    // (newest of ?lastId= and Last-Event-ID — so reconnects miss nothing
    // inside history bounds and never replay rendered lines), then new
    // posts until the client disconnects. Subscribe-before-replay with
    // sequence dedupe closes the gap a post could otherwise slip through.
    // Keepalive comments every 20s keep idle connections open through proxies.
    // Headers flush before the first body byte (see SendHeadersAsync), and
    // the connection lifecycle is logged so a stuck viewer is diagnosable
    // from server.log instead of a silent client-side timeout.
    public static async Task StreamAsync(HttpContext ctx, Atheriz.Core.Objects.Channel channel, CancellationToken ct)
    {
        await SendHeadersAsync(ctx.Response, ct);
        long lastId = ResolveLastId(ctx.Request);
        string client = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        try { Atheriz.Core.AtherizLogger.LogInformation($"Server log stream opened for {client} (resume {lastId})."); }
        catch { }
        var queue = Channel.CreateUnbounded<(long Seq, long Timestamp, string Text)>();
        void Handler(long seq, long timestamp, string text) => queue.Writer.TryWrite((seq, timestamp, text));
        channel.MessagePosted += Handler;
        try
        {
            long delivered = lastId;
            foreach (var (seq, timestamp, text) in channel.LinesWebAfter(lastId))
            {
                ct.ThrowIfCancellationRequested();
                await WriteFrameAsync(ctx.Response, seq, timestamp, text, ct);
                delivered = seq;
            }
            while (!ct.IsCancellationRequested)
            {
                var read = queue.Reader.ReadAsync(ct).AsTask();
                var idle = Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (await Task.WhenAny(read, idle) == read)
                {
                    var (seq, timestamp, text) = await read;
                    if (seq <= delivered) continue;
                    delivered = seq;
                    await WriteFrameAsync(ctx.Response, seq, timestamp, text, ct);
                }
                else
                {
                    ct.ThrowIfCancellationRequested();
                    await WriteKeepaliveAsync(ctx.Response, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            try { Atheriz.Core.AtherizLogger.LogDebug($"Server log stream closed for {client}."); }
            catch { }
        }
        catch (Exception ex)
        {
            // A dead client mid-write must end the stream, not escape: the
            // headers already went out, so there is no error page to render.
            try { Atheriz.Core.AtherizLogger.LogWarning($"Server log stream for {client} failed: {ex.Message}"); }
            catch { }
        }
        finally { channel.MessagePosted -= Handler; }
    }
}
