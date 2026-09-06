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
    public void ServerLifecycle_ResetForTesting_ClearsStartupFlag()
    {
        // DoStartup records startup success only on success, and
        // ResetForTesting clears the flag, so /ready never reports ok for a
        // failed startup.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0 };
        try
        {
            ServerLifecycle.DoStartup(settings);
            Assert.True(ServerLifecycle.StartupSucceeded);
            ServerLifecycle.ResetForTesting();
            Assert.False(ServerLifecycle.StartupSucceeded);
        }
        finally { try { ServerLifecycle.DoShutdown(settings); } catch { } }
    }

    [Fact]
    public void ServerLifecycle_WorldAndShutdownLocks_AreOneLock()
    {
        // Python has a single _WORLD_LOCK (with _shutdown_lock as an alias);
        // the port keeps one shared lock so the documented ordering guarantee holds.
        var t = typeof(ServerLifecycle);
        var w = t.GetField("WorldLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var s = t.GetField("ShutdownLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        Assert.Same(w, s);
    }
}
