using System.Reflection;
using System.Net.WebSockets;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Regression;

// Pins for telnet/persistence/shutdown behavior.
[Collection("Ported")]
public class TelnetPersistenceShutdownTests
{
    // The reader header documents the deliberate throw contract; no comment
    // still promises read-errors-as-EOF.
    [Fact]
    public void ReadCappedLines_Header_DocumentsThrowContract()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.Contains("Read errors throw IOException (deliberate)", src);
        Assert.DoesNotContain("treated as EOF (clean disconnect path)", src);
    }

    private sealed class RefusingMgr : ConnectionManager
    {
        public RefusingMgr()
            : base(pool: new Atheriz.Core.Concurrency.AsyncThreadPool(maxThreads: 2, queueLimit: 100),
                settings: new Atheriz.Core.Settings.AtherizSettings())
        { }
        public override bool RegisterConnection(string connId, BaseConnection connection) => false;
        public override string GenerateConnectionId() => "conn_refused";
    }

    private sealed class DisposalSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public bool Disposed { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; }
        public override void Dispose() { Disposed = true; }
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken c)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => CloseAsync(s, d, c);
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken c) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
    }

    private sealed class WsFeature : IHttpWebSocketFeature
    {
        private readonly WebSocket _ws;
        public WsFeature(WebSocket ws) => _ws = ws;
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(_ws);
    }

    // A refused socket is disposed, not left dangling.
    [Fact]
    public async Task RefusedSocket_DisposedNotDangled()
    {
        var sock = new DisposalSocket();
        var mgr = new RefusingMgr();
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            http.Features.Set<IHttpWebSocketFeature>(new WsFeature(sock));
            await WebSocketHandler.HandleAsync(http, new Atheriz.Core.Settings.AtherizSettings()).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { ConnectionManager.GlobalInstance = prev; mgr.Atp.Stop(wait: false); }
        Assert.True(sock.Disposed);
    }

    // Every transient name resolves to a real field on the engine family:
    // a rename that stops resolving fails here instead of silently
    // changing patch semantics.
    [Fact]
    public void TransientFields_AllResolveToRealFields()
    {
        var f = typeof(PluginReloader).GetField("_transientFields", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        var names = (HashSet<string>)f.GetValue(null)!;
        Assert.NotEmpty(names);
        var types = new[] { typeof(GameObject), typeof(Account), typeof(Channel), typeof(Node), typeof(Script) };
        foreach (var name in names)
        {
            bool found = types.Any(t =>
            {
                var cur = t;
                while (cur != null && cur != typeof(object))
                {
                    if (cur.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                        return true;
                    cur = cur.BaseType;
                }
                return false;
            });
            Assert.True(found, $"transient field '{name}' resolves to no engine field");
        }
    }

    // Set-based delete removes the rows in one round-trip.
    [Fact]
    public async Task DeleteObjects_RemovesRowsSetBased()
    {
        var opts = new DbContextOptionsBuilder<Atheriz.Core.Persistence.AtherizDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var ctx = new Atheriz.Core.Persistence.AtherizDbContext(opts);
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Objects.Add(new Atheriz.Core.Persistence.Entities.ObjectRow { Id = 9001, Data = "{}", Type = "object", Version = 1 });
        ctx.Objects.Add(new Atheriz.Core.Persistence.Entities.ObjectRow { Id = 9002, Data = "{}", Type = "object", Version = 1 });
        await ctx.SaveChangesAsync();
        ObjectRegistry.DeleteObjects(ctx, new List<int> { 9001, 9002 });
        // No-tracking reads: set-based delete bypasses the change tracker,
        // so tracked Finds would still return the deleted rows.
        Assert.Null(await ctx.Objects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == 9001));
        Assert.Null(await ctx.Objects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == 9002));
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        Assert.Contains("ExecuteDelete()", SourceScan.Region(src, "public static void DeleteObjects("));
    }

    // The shutdown client reads the body's status contract: auth failures
    // arrive as HTTP 401 with {status:"error"} JSON, never bare statuses —
    // so no dead HTTP-status branch sits above the JSON handling.
    [Fact]
    public void ShutdownClient_NoDeadStatusBranch()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.DoesNotContain("resp.StatusCode == 401", src);
        Assert.Contains("HTTP 401 with {status:\"error\"}", src);
    }

    // Token lookup stays scoped: configured dir plus the upward walk, no
    // listener-scan discovery and no /proc probe.
    [Fact]
    public void TokenLookup_ScopedNoProcProbe()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.DoesNotContain("/proc/", src);
        Assert.DoesNotContain("TryFindPidListeningOnPort", src);
        Assert.Contains("admin.token", src);
    }
}
