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

// Server lifecycle readiness and lock-sharing behavior.
[Collection("Ported")]
public class ServerLifecycleTests
{
    // --- ServerLifecycle readiness ---

    [Fact]
    public void ServerLifecycle_Reset_ClearsStartupFlag()
    {
        // DoStartup records startup success only on success, and
        // Reset clears the flag, so /ready never reports ok for a
        // failed startup.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0 };
        try
        {
            ServerLifecycle.DoStartup(settings);
            Assert.True(ServerLifecycle.StartupSucceeded);
            ServerLifecycle.Reset();
            Assert.False(ServerLifecycle.StartupSucceeded);
        }
        finally { try { ServerLifecycle.DoShutdown(settings); } catch { } }
    }

    [Fact]
    public void ServerLifecycle_WorldAndShutdownLocks_AreOneLock()
    {
        // Python has a single _WORLD_LOCK (with _shutdown_lock as an alias);
        // the port holds StartStop.WorldLock directly — no private alias pair
        // survives on ServerLifecycle, so the ordering guarantee has one name.
        var t = typeof(ServerLifecycle);
        Assert.Null(t.GetField("WorldLock", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(t.GetField("ShutdownLock", BindingFlags.NonPublic | BindingFlags.Static));
    }
}
