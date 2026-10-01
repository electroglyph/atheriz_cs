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

    public static long ParseLastEventId(string? header)
        => long.TryParse(header, out var id) && id > 0 ? id : 0;

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

    // Replay-then-live: entries after the client's Last-Event-ID first
    // (so reconnects miss nothing inside history bounds), then new posts
    // until the client disconnects. Subscribe-before-replay with sequence
    // dedupe closes the gap a post could otherwise slip through.
    // Keepalive comments every 20s keep idle connections open through proxies.
    public static async Task StreamAsync(HttpContext ctx, Atheriz.Core.Objects.Channel channel, CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        long lastId = ParseLastEventId(ctx.Request.Headers["Last-Event-ID"].ToString());
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
        catch (OperationCanceledException) { }
        finally { channel.MessagePosted -= Handler; }
    }
}
