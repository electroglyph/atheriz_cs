using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Hosting;
using Atheriz.Server.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Core.Tests.Features.Hosting;

// Kestrel fail-fast/dual-stack/limits, admin-token cache,
// orphan sweep + total-cap admission gate.
[Collection("Ported")]
public class HostingRegressionTests
{
    [Fact]
    public void Kestrel_UnparseableInterface_Throws()
    {
        // a bad interface must never silently serve on loopback/Any.
        var settings = new AtherizSettings { WebserverInterface = "not an ip" };
        Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), settings));
    }

    [Fact]
    public void Kestrel_Limits_Configured()
    {
        // global guardrails behind the per-route caps.
        var settings = new AtherizSettings();
        var opts = new KestrelServerOptions();
        KestrelConfig.ConfigureKestrel(opts, settings);
        Assert.Equal(4 * 1024 * 1024, opts.Limits.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.Limits.RequestHeadersTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), opts.Limits.KeepAliveTimeout);
    }

    [Fact]
    public void AdminToken_Cache_RotationExact()
    {
        // mtime-gated cache must track rotation and deletion exactly.
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
        // sockets that never attached account/puppet age out.
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
    public void SweepOrphanedConnections_KeepsPuppetedSession()
    {
        // a session that attached a puppet is live, not orphaned, even
        // when the sweep runs right after login.
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new TestConn("sweep-kept", "10.9.9.9");
            conn.Session.Puppet = PortedHelpers.MakeCaller("sweep-kept-puppet");
            Assert.True(mgr.RegisterConnection("sweep-kept", conn));
            Assert.Equal(0, mgr.SweepOrphanedConnections(TimeSpan.FromMilliseconds(-1)));
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = prev;
        }
    }

    [Fact]
    public void TotalCap_RefusesOverLimit()
    {
        // admission gate above the per-IP limit.
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
