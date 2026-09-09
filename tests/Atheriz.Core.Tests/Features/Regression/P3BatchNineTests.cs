using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests.Features.Regression;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for the P3 batch 9 (N-19, N-21, N-22, N-23, S-17, S-18).
[Collection("Ported")]
public class P3BatchNineTests
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

    private sealed class FakeApp : IWebSocketApp
    {
        public Dictionary<string, Delegate> Captured = new();
        public void WebSocket(string path, Func<IWebSocketPeer, Task> endpoint) => Captured[path] = endpoint;
    }

    private sealed class MockClient : IWebSocketClientInfo
    {
        string? IWebSocketClientInfo.Host => "127.0.0.1";
    }

    private sealed class MockPeer : IWebSocketPeer
    {
        public object? client;
        public List<(int Code, string? Reason)> Closes = new();
        object? IWebSocketPeer.Client => client;
        Task IWebSocketPeer.AcceptAsync() => Task.CompletedTask;
        Task<string> IWebSocketPeer.ReceiveTextAsync() => Task.FromResult("never");
        Task IWebSocketPeer.CloseAsync(int code, string? reason)
        {
            Closes.Add((code, reason));
            return Task.CompletedTask;
        }
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

    private static Func<IWebSocketPeer, Task> CaptureEndpoint()
    {
        var app = new FakeApp();
        var prev = Atheriz.Core.Settings.AtherizSettings.Global.WebsocketEnabled;
        Atheriz.Core.Settings.AtherizSettings.Global.WebsocketEnabled = true;
        try { new WebSocketProtocol().Setup(app); }
        finally { Atheriz.Core.Settings.AtherizSettings.Global.WebsocketEnabled = prev; }
        return (Func<IWebSocketPeer, Task>)app.Captured["/ws"];
    }

    // A refused peer is closed, not left dangling on a log-only Close.
    [Fact]
    public async Task RefusedPeer_ClosedNotDangled()
    {
        var endpoint = CaptureEndpoint();
        var peer = new MockPeer { client = new MockClient() };
        var mgr = new RefusingMgr();
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try { await endpoint(peer); }
        finally { ConnectionManager.GlobalInstance = prev; mgr.Atp.Stop(wait: false); }
        Assert.Single(peer.Closes);
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

    // The shutdown client reads the body's status contract: no dead
    // HTTP-status branch above the JSON handling.
    [Fact]
    public void ShutdownClient_NoDeadStatusBranch()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        var region = SourceScan.Region(src, "internal static async Task<ShutdownRequestResult> TryRequestShutdownAsync(");
        Assert.DoesNotContain("resp.StatusCode == 401", region);
        Assert.Contains("HTTP 200 + {status:\"error\"}", region);
    }

    // No empty existence check litters the token lookup.
    [Fact]
    public void TokenLookup_NoEmptyExistenceCheck()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.DoesNotContain("Directory.Exists(link)", src);
        Assert.Contains("LinkTarget", src);
    }
}
