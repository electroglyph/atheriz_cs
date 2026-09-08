using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Server.Hosting;

public static class WebSocketHandler
{
    private static readonly Dictionary<string, double> _wsOversizeLast = new();
    private static readonly object _wsOversizeLock = new();

    public static async Task HandleAsync(HttpContext context, AtherizSettings settings)
    {
        string clientHost = "?";
        try { clientHost = context.Connection?.RemoteIpAddress?.ToString() ?? "?"; } catch { }

        if (ObjectRegistry.IsIpBanned(clientHost))
        {
            Console.Error.WriteLine($"Host {clientHost} in temp ban list has tried to connect.");
            try { context.Response.StatusCode = 403; } catch { }
            return;
        }

        bool isWsRequest = false;
        try { isWsRequest = context.WebSockets.IsWebSocketRequest; } catch { }
        if (!isWsRequest) { try { context.Response.StatusCode = 400; } catch { } return; }

        System.Net.WebSockets.WebSocket webSocket;
        try { webSocket = await context.WebSockets.AcceptWebSocketAsync(); } catch { return; }

        var connId = ConnectionManager.GlobalInstance?.GenerateConnectionId() ?? $"conn_{Guid.NewGuid()}";
        var connection = new WebSocketConnection(webSocket, sessionId: connId, settings: settings, clientHost: clientHost);
        var manager = ConnectionManager.GlobalInstance ?? new ConnectionManager(settings: settings);
        if (!manager.RegisterConnection(connId, connection)) return;

        try
        {
            var buffer = new byte[8192];
            // socket ops observe host shutdown — graceful stop
            // cancels open sockets (whose OperationCanceledException flows
            // to finally/Disconnect below) instead of dangling until the
            // client closes. RequestAborted is deliberately NOT used: it
            // fires during normal WS lifetime behind some proxies.
            var lifetime = context.RequestServices?.GetService(typeof(Microsoft.Extensions.Hosting.IHostApplicationLifetime)) as Microsoft.Extensions.Hosting.IHostApplicationLifetime;
            var shutdownToken = lifetime?.ApplicationStopping ?? default(System.Threading.CancellationToken);
            // Streaming size gate: fragments are measured as they
            // arrive and the message is abandoned as soon as it exceeds the
            // limit — never accumulate an unbounded MemoryStream.
            // this gate is the single enforcement point (the old
            // post-hoc byte-count re-check was dead: assembled bytes can
            // never exceed what the streaming gate already measured).
            var maxMessageSize = settings.WebsocketMaxMessageSize;
            while (true)
            {
                string rawMessage;
                bool isClose = false;
                bool tooBig = false;
                using (var ms = new MemoryStream())
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    do
                    {
                        // observe host shutdown (see above).
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), shutdownToken);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            isClose = true;
                            break;
                        }
                        if (ms.Length + result.Count > maxMessageSize) { tooBig = true; break; }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (isClose) break;
                    if (!tooBig) rawMessage = Encoding.UTF8.GetString(ms.ToArray());
                    else rawMessage = "";
                }
                if (isClose) break;
                if (tooBig)
                {
                    bool shouldLog = ThrottleWindow.ShouldLog(_wsOversizeLast, _wsOversizeLock, clientHost, 5.0);
                    if (shouldLog)
                    {
                        var msg = $"[WebSocket] Message too large from {clientHost} (over {maxMessageSize} bytes)";
                        try { Atheriz.Core.AtherizLogger.LogWarning(msg); } catch { }
                        Console.Error.WriteLine(msg);
                    }
                    try { await webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.MessageTooBig, "Message too large", shutdownToken); } catch { }
                    break;
                }

                manager.HandleCommand(connection, rawMessage);
            }
        }
        catch (System.Net.WebSockets.WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            var msg = $"[WebSocket] Connection error: {e}";
            try { Atheriz.Core.AtherizLogger.LogWarning(msg); } catch { }
            Console.Error.WriteLine(msg);
        }
        finally
        {
            // disconnect on the REGISTERING manager instance, not a
            // fresh GlobalInstance read (which may have been swapped since).
            manager.Disconnect(connection);
        }
    }
}
