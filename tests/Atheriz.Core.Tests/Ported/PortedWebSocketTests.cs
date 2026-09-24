// Port of atheriz/tests/test_websocket.py:1 — 16 defs faithful
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedWebSocketTests
{
    // Scripted System.Net.WebSockets.WebSocket: yields queued text messages
    // (fragmented to the receive buffer) then a clean close. Drives the real
    // /ws pump (WebSocketHandler) with no network.
    private class ScriptSocket : WebSocket
    {
        private byte[] _current = [];
        private int _offset;
        private readonly Queue<byte[]> _msgs;
        public WebSocketCloseStatus? Status;
        public ScriptSocket(IEnumerable<string> texts) => _msgs = new Queue<byte[]>(texts.Select(Encoding.UTF8.GetBytes));
        public override WebSocketCloseStatus? CloseStatus => Status;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken c) { Status = s; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => CloseAsync(s, d, c);
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken c)
        {
            while (_offset >= _current.Length)
            {
                if (_msgs.Count == 0)
                    return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
                _current = _msgs.Dequeue();
                _offset = 0;
            }
            int n = Math.Min(_current.Length - _offset, buffer.Count);
            Buffer.BlockCopy(_current, _offset, buffer.Array!, buffer.Offset, n);
            _offset += n;
            return Task.FromResult(new WebSocketReceiveResult(n, WebSocketMessageType.Text, _offset >= _current.Length));
        }
    }

    private sealed class ScriptFeature : IHttpWebSocketFeature
    {
        private readonly WebSocket _ws;
        public ScriptFeature(WebSocket ws) => _ws = ws;
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(_ws);
    }

    private static DefaultHttpContext WsContext(ScriptSocket sock, string host = "127.0.0.1")
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(host);
        http.Features.Set<IHttpWebSocketFeature>(new ScriptFeature(sock));
        return http;
    }

    private sealed class FakeWs : WebSocket
    {
        public List<string> Sent = new();
        public bool Closed;
        public string? CloseReason;
        private WebSocketCloseStatus? _closeStatus;
        public Func<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken, Task>? SendHandler;
        public override WebSocketCloseStatus? CloseStatus => _closeStatus;
        public override string? CloseStatusDescription => CloseReason;
        public override WebSocketState State => Closed ? WebSocketState.Closed : WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort(){}
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken){ Closed=true; _closeStatus=closeStatus; CloseReason=statusDescription; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseAsync(closeStatus,statusDescription,cancellationToken);
        public override void Dispose(){}
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if(SendHandler!=null) return SendHandler(buffer, messageType, endOfMessage, cancellationToken);
            var s = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            Sent.Add(s);
            return Task.CompletedTask;
        }
    }

    // For mocking ConnectionManager
    private sealed class MockMgr : ConnectionManager
    {
        public int DisconnectCalls;
        public int HandleCalls;
        public int RegisterCalls;
        public bool RegisterReturn = true;
        public MockMgr() : base(pool: new Atheriz.Core.Concurrency.AsyncThreadPool(maxThreads:2, queueLimit:100), settings: new AtherizSettings()) { }
        public override bool RegisterConnection(string connId, BaseConnection connection) { RegisterCalls++; return RegisterReturn; }
        public override void Disconnect(BaseConnection connection) { DisconnectCalls++; }
        public override void HandleCommand(BaseConnection connection, string rawMessage) { HandleCalls++; }
        public override string GenerateConnectionId() => "conn_mock";
    }

    // ----- TestWebSocketDisconnect -----
    [Fact]
    public async Task OversizedMessageDisconnectsConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var sock = new ScriptSocket([new string('x', 100_000)]);
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await WebSocketHandler.HandleAsync(WsContext(sock), new AtherizSettings { WebsocketMaxMessageSize = 100 }).WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait: false); }
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, sock.Status);
        Assert.Equal(0, mockMgr.HandleCalls);
        Assert.Equal(1, mockMgr.DisconnectCalls);
    }

    [Fact]
    public async Task ReceiveErrorDisconnectsConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var sock = new ErrorSocket();
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await WebSocketHandler.HandleAsync(WsContext(sock), new AtherizSettings()).WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait: false); }
        Assert.Equal(1, mockMgr.DisconnectCalls);
    }

    private sealed class ErrorSocket : ScriptSocket
    {
        public ErrorSocket() : base([]) { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken c) =>
            Task.FromException<WebSocketReceiveResult>(new InvalidOperationException("socket error"));
    }

    // ----- TestWebSocketNone -----
    [Fact]
    public async Task WsEndpointToleratesClientNone()
    {
        using var env = GlobalTestEnv.Enter();
        var sock = new ScriptSocket([]);
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpWebSocketFeature>(new ScriptFeature(sock));
        try { await WebSocketHandler.HandleAsync(http, new AtherizSettings()).WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait: false); }
        Assert.Equal(1, mockMgr.DisconnectCalls);
    }

    // ----- TestWebSocketConnection -----
    [Fact] public void InitStoresWebsocket()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, clientHost:"127.0.0.1");
        Assert.Same(ws, conn.WebSocket);
    }
    [Fact] public void InitStoresClientHost()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, clientHost:"10.0.0.1");
        Assert.Equal("10.0.0.1", conn.ClientHost);
    }
    [Fact] public void InitHandlesNoClient()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, clientHost:"?");
        Assert.Equal("?", conn.ClientHost);
    }
    [Fact] public void SessionId()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, sessionId:"abc", clientHost:"?");
        Assert.Equal("abc", conn.SessionId);
    }

    // ----- TestWebSocketConnectionSendCommand -----
    [Fact] public void SerializesData()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, sessionId:"x", clientHost:"?");
        conn.SendCommand("text", new List<object?>{"hello"}, new Dictionary<string,object?>{["k"]="v"});
        Thread.Sleep(300);
        Assert.Single(ws.Sent);
        var parsed = JsonDocument.Parse(ws.Sent[0]).RootElement;
        Assert.Equal("text", parsed[0].GetString());
        Assert.Contains("hello", parsed[1].EnumerateArray().First().GetString());
        Assert.Equal("v", parsed[2].GetProperty("k").GetString());
    }
    [Fact] public void SerializeNoArgs()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, sessionId:"x", clientHost:"?");
        conn.SendCommand("ping", new List<object?>{}, new Dictionary<string,object?>{});
        Thread.Sleep(300);
        Assert.Single(ws.Sent);
        var parsed = JsonDocument.Parse(ws.Sent[0]).RootElement;
        Assert.Equal("ping", parsed[0].GetString());
        Assert.Empty(parsed[1].EnumerateArray());
        Assert.Empty(parsed[2].EnumerateObject());
    }


    // ----- TestWebSocketMessageSize -----
    [Fact]
    public async Task RejectsOversizedMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var sock = new ScriptSocket([new string('x', 100_000)]);
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await WebSocketHandler.HandleAsync(WsContext(sock), new AtherizSettings { WebsocketMaxMessageSize = 100 }).WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait: false); }
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, sock.Status);
    }

    [Fact]
    public async Task AcceptsNormalMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var sock = new ScriptSocket(["hello"]);
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await WebSocketHandler.HandleAsync(WsContext(sock), new AtherizSettings()).WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait: false); }
        Assert.Equal(1, mockMgr.HandleCalls);
        Assert.Null(sock.Status);
    }

    // ----- TestWebSocketSendSerialization -----
    [Fact] public void WebSocketSendsHavePerSocketLock()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        var conn = new WebSocketConnection(ws, clientHost:"1.1.1.1");
        var f = typeof(WebSocketConnection).GetField("_sendLock", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        Assert.NotNull(f);
        var l = f!.GetValue(conn);
        Assert.NotNull(l);
        Assert.IsType<SemaphoreSlim>(l);
    }
    [Fact] public void ConcurrentSendTextIsSerialized()
    {
        using var env = GlobalTestEnv.Enter();
        var ws = new FakeWs();
        int active=0, maxActive=0;
        object lk=new();
        ws.SendHandler = async (buf, type, end, ct)=>{
            lock(lk){ active++; maxActive=Math.Max(maxActive, active); }
            await Task.Delay(40);
            lock(lk){ active--; }
            var s = Encoding.UTF8.GetString(buf.Array!, buf.Offset, buf.Count);
            lock(ws.Sent) ws.Sent.Add(s);
        };
        var conn = new WebSocketConnection(ws, clientHost:"1.1.1.1");
        var threads = Enumerable.Range(0,5).Select(_=> new Thread(()=> conn.SendCommand("text", new List<object?>{"hi"}))).ToList();
        foreach(var t in threads) t.Start();
        foreach(var t in threads) t.Join();
        Thread.Sleep(500);
        Assert.True(maxActive<=1, $"concurrent send_text not serialized: max_active={maxActive}");
    }
}
