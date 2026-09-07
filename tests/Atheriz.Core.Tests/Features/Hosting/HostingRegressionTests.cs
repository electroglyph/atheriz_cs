using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Hosting;
using Atheriz.Server.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Core.Tests.Features.Hosting;

// P1-16 Batch B: Kestrel fail-fast/dual-stack/limits, admin-token cache,
// orphan sweep + total-cap admission gate.
[Collection("Ported")]
public class HostingRegressionTests
{
    private static IConfiguration KestrelConfigFor(Dictionary<string, string?> pairs)
        => new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

    [Fact]
    public void Kestrel_UnparseableInterface_Throws()
    {
        // P1-16: a bad interface must never silently serve on loopback/Any.
        var config = KestrelConfigFor(new() { ["Atheriz:WebserverInterface"] = "not an ip" });
        Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
    }

    [Fact]
    public void Kestrel_Limits_Configured()
    {
        // P1-16: global guardrails behind the per-route caps.
        var config = KestrelConfigFor(new());
        var opts = new KestrelServerOptions();
        KestrelConfig.ConfigureKestrel(opts, config);
        Assert.Equal(4 * 1024 * 1024, opts.Limits.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.Limits.RequestHeadersTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), opts.Limits.KeepAliveTimeout);
    }

    [Fact]
    public void AdminToken_Cache_RotationExact()
    {
        // P1-16: mtime-gated cache must track rotation and deletion exactly.
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_tokcache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "admin.token");
            File.WriteAllText(file, "tok1");
            Assert.Null(AdminToken.CheckAdmin(dir, "127.0.0.1", "tok1", "x"));
            File.WriteAllText(file, "tok2");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(2));
            Assert.Null(AdminToken.CheckAdmin(dir, "127.0.0.1", "tok2", "x"));
            Assert.Equal("Invalid token.", AdminToken.CheckAdmin(dir, "127.0.0.1", "tok1", "x"));
            File.Delete(file);
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(dir, "127.0.0.1", "tok2", "x"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SweepOrphanedConnections_DropsStalePreLogin()
    {
        // P1-16: sockets that never attached account/puppet age out.
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var mgr = new ConnectionManager(pool: pool, settings: new AtherizSettings());
        try
        {
            var conn = new FakeConnection("sweep-me") { ClientHost = "10.9.9.9" };
            Assert.True(mgr.RegisterConnection("sweep-me", conn));
            Assert.Equal(1, mgr.SweepOrphanedConnections(TimeSpan.FromMilliseconds(-1)));
            Assert.Equal(0, mgr.ConnectionCount);
        }
        finally { pool.Stop(wait: false); }
    }

    [Fact]
    public void TotalCap_RefusesOverLimit()
    {
        // P1-16: admission gate above the per-IP limit.
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var settings = new AtherizSettings { MaxTotalConnections = 1 };
        var mgr = new ConnectionManager(pool: pool, settings: settings);
        try
        {
            var c1 = new FakeConnection("cap-1") { ClientHost = "10.9.9.1" };
            var c2 = new FakeConnection("cap-2") { ClientHost = "10.9.9.2" };
            Assert.True(mgr.RegisterConnection("cap-1", c1));
            Assert.False(mgr.RegisterConnection("cap-2", c2));
            Assert.Equal(1, mgr.ConnectionCount);
        }
        finally { pool.Stop(wait: false); }
    }
}
