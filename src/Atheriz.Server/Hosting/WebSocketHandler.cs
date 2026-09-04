using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Server.Hosting;

public static class WebSocketHandler
{
    private static readonly Dictionary<string, double> _wsOversizeLast = new();
    private static readonly object _wsOversizeLock = new();

    public static async Task MapWebSocketAsync(HttpContext context, AtherizSettings settings) => await HandleAsync(context, settings);

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
            while (true)
            {
                string rawMessage;
                bool isClose = false;
                using (var ms = new MemoryStream())
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    do
                    {
                        // Use CancellationToken.None to avoid breaking on RequestAborted during normal WS lifetime;
                        // host shutdown is handled via finally/Disconnect. Old Program.cs:463 lambda used None.
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            isClose = true;
                            break;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (isClose) break;
                    rawMessage = Encoding.UTF8.GetString(ms.ToArray());
                }

                var byteCount = Encoding.UTF8.GetByteCount(rawMessage);
                if (byteCount > settings.WebsocketMaxMessageSize)
                {
                    bool shouldLog = ThrottleWindow.ShouldLog(_wsOversizeLast, _wsOversizeLock, clientHost, 5.0);
                    if (shouldLog)
                    {
                        var msg = $"[WebSocket] Message too large from {clientHost} ({byteCount} bytes > {settings.WebsocketMaxMessageSize} bytes)";
                        try { Atheriz.Core.AtherizLogger.LogWarning(msg); } catch { }
                        Console.Error.WriteLine(msg);
                    }
                    try { await webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None); } catch { }
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
            var mgr = ConnectionManager.GlobalInstance;
            mgr?.Disconnect(connection);
        }
    }
}
