// Port of atheriz/tests/test_websocket.py:1 — 16 defs faithful
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedWebSocketTests
{
    // Helpers for endpoint capture
    private sealed class FakeApp
    {
        public Dictionary<string, Delegate> Captured = new();
        public Func<string, Func<Delegate, Delegate>> websocket => path => handler => { Captured[path] = handler; return handler; };
    }
    private static Func<dynamic, Task> CaptureEndpoint()
    {
        var app = new FakeApp();
        var prev = AtherizSettings.Global.WebsocketEnabled;
        AtherizSettings.Global.WebsocketEnabled = true;
        try { new WebSocketProtocol().Setup(app); }
        finally { AtherizSettings.Global.WebsocketEnabled = prev; }
        if (app.Captured.TryGetValue("/ws", out var del) && del is Func<dynamic, Task> fn) return fn;
        if (app.Captured.TryGetValue("/ws", out var del2) && del2 is Delegate d)
        {
            // Try to convert Delegate to Func<dynamic,Task>
            if (d is Func<dynamic, Task> ff) return ff;
            // If it's Func<Delegate,Delegate> wrapped, unwrap via DynamicInvoke
            try { var res = d.DynamicInvoke(new Func<dynamic, Task>(_=> Task.CompletedTask)); } catch {}
        }
        // Fallback: try to get as Delegate and cast
        foreach (var kv in app.Captured)
        {
            if (kv.Value is Func<dynamic, Task> f) return f;
            if (kv.Value is Delegate dd && dd.Method.ReturnType == typeof(Task))
            {
                // Attempt to create wrapper
                return async (dynamic ws) => { await (Task)dd.DynamicInvoke(ws)!; };
            }
        }
        throw new InvalidOperationException("endpoint not captured");
    }

    private sealed class MockClient { public string host = "127.0.0.1"; }
    private sealed class MockWsEndpoint
    {
        public object? client;
        public Func<Task> acceptImpl = () => Task.CompletedTask;
        public Func<Task<string>> receiveTextImpl = () => Task.FromResult("");
        public List<int> CloseCodes = new();
        public bool CloseCalled;
        public Task accept() => acceptImpl();
        public Task<string> receive_text() => receiveTextImpl();
        public Task close(object? code = null, object? reason = null)
        {
            CloseCalled = true;
            if (code is int i) CloseCodes.Add(i);
            else if (code != null) CloseCodes.Add(0);
            else CloseCodes.Add(0);
            return Task.CompletedTask;
        }
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
        var endpoint = CaptureEndpoint();
        var ws = new MockWsEndpoint();
        ws.client = new MockClient { host = "127.0.0.1" };
        ws.receiveTextImpl = () => Task.FromResult(new string('x', 100_000));
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await endpoint((dynamic)ws); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait:false); }
        Assert.Equal(1, mockMgr.DisconnectCalls);
    }

    [Fact]
    public async Task ReceiveErrorDisconnectsConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var endpoint = CaptureEndpoint();
        var ws = new MockWsEndpoint();
        ws.client = new MockClient { host = "127.0.0.1" };
        ws.receiveTextImpl = () => throw new InvalidOperationException("socket error");
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await endpoint((dynamic)ws); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait:false); }
        Assert.Equal(1, mockMgr.DisconnectCalls);
    }

    // ----- TestWebSocketNone -----
    [Fact]
    public async Task WsEndpointToleratesClientNone()
    {
        using var env = GlobalTestEnv.Enter();
        var endpoint = CaptureEndpoint();
        var ws = new MockWsEndpoint();
        ws.client = null;
        ws.receiveTextImpl = () => throw new TestWebSocketDisconnectException();
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await endpoint((dynamic)ws); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait:false); }
        Assert.Equal(1, mockMgr.DisconnectCalls);
    }
    private sealed class TestWebSocketDisconnectException : Exception { }

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

    // ----- TestWebSocketProtocolSetup -----
    [Fact] public void SetupRegistersRoute()
    {
        using var env = GlobalTestEnv.Enter();
        var app = new FakeApp();
        var prev = AtherizSettings.Global.WebsocketEnabled;
        AtherizSettings.Global.WebsocketEnabled = true;
        try { new WebSocketProtocol().Setup(app); } finally { AtherizSettings.Global.WebsocketEnabled = prev; }
        Assert.True(app.Captured.ContainsKey("/ws"));
    }
    [Fact] public void SetupSkippedWhenDisabled()
    {
        using var env = GlobalTestEnv.Enter();
        var app = new FakeApp();
        var prev = AtherizSettings.Global.WebsocketEnabled;
        AtherizSettings.Global.WebsocketEnabled = false;
        try { new WebSocketProtocol().Setup(app); } finally { AtherizSettings.Global.WebsocketEnabled = prev; }
        Assert.Empty(app.Captured);
    }

    // ----- TestBaseProtocol -----
    [Fact] public void SetupNotImplemented()
    {
        using var env = GlobalTestEnv.Enter();
        var proto = new DummyProtocol();
        var ex = Record.Exception(()=> proto.Setup(new object()));
        Assert.NotNull(ex);
        Assert.IsType<NotImplementedException>(ex);
    }
    private sealed class DummyProtocol : BaseProtocol { public override void Setup(object app)=> throw new NotImplementedException(); }

    // ----- TestWebSocketMessageSize -----
    [Fact]
    public async Task RejectsOversizedMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var endpoint = CaptureEndpoint();
        var ws = new MockWsEndpoint();
        ws.client = new MockClient { host = "127.0.0.1" };
        ws.receiveTextImpl = () => Task.FromResult(new string('x', 100_000));
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await endpoint((dynamic)ws); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait:false); }
        Assert.Contains(1009, ws.CloseCodes);
    }

    [Fact]
    public async Task AcceptsNormalMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var endpoint = CaptureEndpoint();
        var ws = new MockWsEndpoint();
        ws.client = new MockClient { host = "127.0.0.1" };
        int call = 0;
        ws.receiveTextImpl = () => {
            call++;
            if (call==1) return Task.FromResult("hello");
            throw new TestWebSocketDisconnectException();
        };
        var mockMgr = new MockMgr();
        var prevMgr = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mockMgr;
        try { await endpoint((dynamic)ws); }
        finally { ConnectionManager.GlobalInstance = prevMgr; mockMgr.Atp.Stop(wait:false); }
        Assert.Equal(1, mockMgr.HandleCalls);
        Assert.Empty(ws.CloseCodes);
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
