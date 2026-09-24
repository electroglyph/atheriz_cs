using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core;
using System.Diagnostics;
using System.Reflection;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from AdminGuardHelperTests.cs
// One admin guard-result shape for the three /_internal endpoints; the auth
// gate is the shared Admin policy (one RequireAuthorization per endpoint).
// The Validate* one-line forwards are gone —
// the create-account site calls Validation directly.
[Collection("Ported")]
public class AdminGuardHelperTests
{
    [Fact]
    public void GuardHelpers_DefinedOnce_UsedThrice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.Equal(0, SourceScan.Count(src, "bool RequireAdmin("));
        Assert.Equal(1, SourceScan.Count(src, "static IResult AdminError("));
        Assert.Equal(1, SourceScan.Count(src, "static IResult AdminOk("));
        Assert.Equal(3, SourceScan.Count(src, ".RequireAuthorization(AdminAuthServices.Policy)"));
    }

    [Fact]
    public void CreateAccount_CallsValidationDirectly()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.Contains("Validation.ValidateAccountName(accountName, settings)", src);
        Assert.Contains("Validation.ValidateCharacterName(charName, settings)", src);
        Assert.Contains("Validation.ValidatePassword(password, settings)", src);
        // No local Validate* wrappers survive: each name occurs exactly once
        // (the direct call above).
        Assert.Equal(1, SourceScan.Count(src, "ValidateAccountName("));
        Assert.Equal(1, SourceScan.Count(src, "ValidateCharacterName("));
        Assert.Equal(1, SourceScan.Count(src, "ValidatePassword("));
    }
}

// Merged from CertCallbackGuardTests.cs
// The cert callback keeps only the live null-guard (RequestUri is nullable);
// the always-false `req is not HttpRequestMessage` pattern is gone.
// Loopback-only tolerance is unchanged.
[Collection("Ported")]
public class CertCallbackGuardTests
{
    [Fact]
    public void Callback_KeepsLiveGuard_DropsDeadPattern()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        var region = SourceScan.Region(src, "ServerCertificateCustomValidationCallback");
        Assert.DoesNotContain("req is not HttpRequestMessage", region);
        Assert.Contains("req.RequestUri is not Uri", region);
    }

    [Fact]
    public void Callback_LoopbackSet_Unchanged()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.Contains("u.Host == \"localhost\"", src);
        Assert.Contains("u.Host == \"127.0.0.1\"", src);
        Assert.Contains("u.Host == \"::1\"", src);
        Assert.Contains("RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch", src);
    }
}

// Merged from BuilderPolicyParityTests.cs
// Builder policy: the 1-arg and 2-arg overloads share one target-independent
// leaf, so both resolve identically for builders, non-builders, and quelled
// builders (quell suppresses the builder bit in both).
[Collection("Ported")]
public class BuilderPolicyParityTests
{
    [Fact]
    public void BuilderPolicy_BothOverloads_Agree()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var target = GameObject.Create("target");
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            var player = GameObject.Create("player", privilege: Privilege.Player);

            Assert.True(LockPolicies.TryResolve(LockPolicies.Builder, out var p1));
            Assert.True(LockPolicies.TryResolve(LockPolicies.Builder, target, out var p2));
            Assert.True(p1(builder));
            Assert.True(p2(builder));
            Assert.False(p1(player));
            Assert.False(p2(player));

            builder.Quelled = true;
            Assert.False(p1(builder));
            Assert.False(p2(builder));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void BuilderPolicy_Unknown_ReturnsFalse()
    {
        var target = GameObject.Create("target");
        Assert.False(LockPolicies.TryResolve("no-such-policy", out _));
        Assert.False(LockPolicies.TryResolve("no-such-policy", target, out _));
    }
}

// Merged from ReloaderGateGridTests.cs
// Dedicated reload gate + shared grid walk: a reload in progress makes a second
// reload skip (never block), and tick re-registration discovers grid nodes
// through the unified snapshot walk.
[Collection("Ported")]
public class ReloaderGateGridTests
{
    private static MethodInfo GateMethod(string name) =>
        typeof(PluginReloader).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public async Task ReloadAsync_GateHeldOnOtherFlow_SkipsWithoutBlocking()
    {
        using var env = GlobalTestEnv.Enter();
        var tryEnter = GateMethod("TryEnterGate");
        var exitGate = GateMethod("ExitGate");
        Assert.NotNull(tryEnter);
        Assert.NotNull(exitGate);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        bool holderTookGate = false;
        var holder = new Thread(() =>
        {
            try
            {
                holderTookGate = (bool)tryEnter.Invoke(null, null)!;
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                exitGate.Invoke(null, null);
            }
            catch { entered.Set(); }
        });
        // Suppress the ExecutionContext flow so the holder thread starts with a
        // fresh gate view instead of inheriting this flow's recursion count.
        var flow = ExecutionContext.SuppressFlow();
        try { holder.Start(); }
        finally { flow.Undo(); }
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(holderTookGate);
        try
        {
            using var cap = new CaptureAtherizLog();
            var ticker = GlobalServices.GetAsyncTicker();
            var pool = GlobalServices.GetAsyncThreadPool();
            var sw = Stopwatch.StartNew();
            Assert.False(await PluginReloader.ReloadAsync("/tmp/atheriz_gate_probe_xyz.dll", ticker, pool));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Contains("already in progress", cap.Read());
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void ReregisterTicks_DiscoversGridOnlyTickableNode()
    {
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var area = new NodeArea("gategrid");
            var grid = new NodeGrid("gategrid", 0);
            var node = new Node(new Coord("gategrid", 0, 0, 0)) { IsTickable = true, TickSeconds = 60 };
            // Grid-only membership: the unified walk must find the node even
            // though it is not published to the object registry.
            try { ObjectRegistry.RemoveObject(node); } catch { }
            grid.AddNode(node);
            area.AddGrid(grid);
            nh.AddArea(area);
            GlobalServices.SetNodeHandler(nh);
            Assert.Empty(ObjectRegistry.FilterBy(o => o.IsTickable));
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(1, ticker.Slots.Values.Sum(s => s.Coros.Count));
        }
        finally { ticker.Clear(); pool.Stop(); }
    }
}

// Merged from RecursiveDeleteTruncationTests.cs
// Recursive delete: the depth cap records capped children without visiting
// them (the membership test is load-bearing), so over-deep survivors are
// detached, not deleted.
[Collection("Ported")]
public class RecursiveDeleteTruncationTests
{
    [Fact]
    public void DeleteRecursive_DetachesOverDepthSurvivors()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var chain = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                var o = GameObject.Create($"c{i}", isContainer: true);
                ObjectRegistry.AddObject(o);
                chain.Add(o);
                if (i > 0) chain[i - 1].AddObject(o);
            }
            var root = chain[0];

            var result = root.Delete(null, recursive: true, maxDepth: 4);

            Assert.NotNull(result);
            Assert.True(root.IsDeleted);
            Assert.True(chain[3].IsDeleted);
            // Depth-capped: c4 and below survive, detached to nowhere.
            Assert.False(chain[4].IsDeleted);
            Assert.NotEmpty(ObjectRegistry.Get(chain[4].Id));
            Assert.IsType<LocationRef.NullLocation>(chain[4].Location);
            Assert.False(chain[7].IsDeleted);
        }
        finally
        {
            ObjectRegistry.ClearAll();
        }
    }
}
