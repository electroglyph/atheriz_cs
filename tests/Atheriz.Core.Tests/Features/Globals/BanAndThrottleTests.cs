using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Globals;

// IP ban, creation-cooldown, and throttle-window behavior.
[Collection("Ported")]
public class BanAndThrottleTests
{
    // --- Ban/cooldown check-then-remove race ---

    [Fact]
    public void IpBan_ExpiryRefresh_Race_DoesNotDeleteFreshBan_Stress()
    {
        // Ban expiry uses remove-if-equal: a BanIp refresh landing between
        // check and remove must not delete the fresh ban. Hammers the
        // interleave; a correct implementation never loses the refresh.
        ObjectRegistry.ClearAll();
        try
        {
            const string host = "10.9.9.9";
            int lost = 0;
            for (int i = 0; i < 2500; i++)
            {
                ObjectRegistry.BanIp(host, 999.0); // expired relative to t=1000
                var check = Task.Run(() => ObjectRegistry.IsIpBanned(host, 1000.0));
                var refresh = Task.Run(() => ObjectRegistry.BanIp(host, 1100.0));
                Task.WaitAll(check, refresh);
                if (!ObjectRegistry.IsIpBanned(host, 1000.0)) lost++;
                ObjectRegistry.UnbanIp(host);
            }
            Assert.Equal(0, lost);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void CreationCooldown_Refresh_Race_DoesNotDeleteFreshCooldown_Stress()
    {
        // Creation cooldown uses the same remove-if-equal shape as IP bans:
        // a refresh racing the expiry check must preserve the fresh cooldown.
        ObjectRegistry.ClearAll();
        try
        {
            const string host = "10.9.9.8";
            int lost = 0;
            for (int i = 0; i < 800; i++)
            {
                ObjectRegistry.ApplyCreationCooldown("create", host, 900.0, 50.0); // exp 950 < 1000
                var check = Task.Run(() => ObjectRegistry.CreationCooldownActive("create", host, 1000.0));
                var refresh = Task.Run(() => ObjectRegistry.ApplyCreationCooldown("create", host, 1000.0, 100.0));
                Task.WaitAll(check, refresh);
                if (!ObjectRegistry.CreationCooldownActive("create", host, 1000.0)) lost++;
                ObjectRegistry.ClearCreationCooldown(host);
            }
            Assert.Equal(0, lost);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- Throttle dict growth ---

    [Fact]
    public void ThrottleWindow_EvictsExpiredHosts()
    {
        // Per-host throttle state must not grow forever: entries whose window
        // has fully elapsed are evicted on later calls.
        var last = new Dictionary<string, double>();
        var lck = new object();
        Assert.True(ThrottleWindow.ShouldLog(last, lck, "h1", 5.0, now: 100.0));
        Assert.False(ThrottleWindow.ShouldLog(last, lck, "h1", 5.0, now: 102.0));
        Assert.True(ThrottleWindow.ShouldLog(last, lck, "h2", 5.0, now: 200.0));
        Assert.Single(last);
    }

    [Fact]
    public void ThrottleWindow_Now_IsPositiveMonotonic()
    {
        // Now() exposes the monotonic clock.
        Assert.True(ThrottleWindow.Now() > 0);
    }
}
