using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Batch E (§3.1) pins: Lazy<T> publication, deleted twin shims, extracted
// LockTable/HookRegistry, split ban/cooldown stores.
[Collection("Ported")]
public class Section3BatchEPinsTests
{
    [Fact]
    public void GetOrCreateSingleton_Gone()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        Assert.DoesNotContain("GetOrCreateSingleton", src);
        Assert.DoesNotContain("_singletonLock", src);
        Assert.Contains("Lazy<", src);
    }

    [Fact]
    public void LockTable_IsSingleHome()
    {
        var go = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        Assert.Contains("private readonly LockTable _lockTable = new();", go);
        Assert.DoesNotContain("Dictionary<string, List<LockEntry>> _locks", go);
        var door = SourceScan.Read("src", "Atheriz.Core", "Objects", "Door.cs");
        Assert.Contains("private readonly LockTable _lockTable = new();", door);
        Assert.DoesNotContain("Dictionary<string, List<LockEntry>> _locks", door);
        Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot(), "src", "Atheriz.Core", "Objects", "LockTable.cs")));
    }

    [Fact]
    public void HookRegistry_IsSingleHome()
    {
        var go = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        Assert.Contains("private readonly HookRegistry _hookRegistry = new();", go);
        Assert.DoesNotContain("Dictionary<string, HashSet<Delegate>> _hooks", go);
        Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot(), "src", "Atheriz.Core", "Objects", "HookRegistry.cs")));
    }

    [Fact]
    public void Locks_And_Hooks_StillBehave()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("bantable");
            o.AddLock("get", _ => false);
            Assert.False(o.Access(GameObject.Create("intruder"), "get"));
            Assert.True(o.Access(GameObject.Create("intruder"), "missing"));
            o.ClearLocksByName("get");
            Assert.True(o.Access(GameObject.Create("intruder"), "get"));
            bool fired = false;
            o.InstallHook("custom_hook", (Action)(() => fired = true));
            Assert.True(o.HasHook("custom_hook"));
            // Unmarked delegates install but never dispatch: the original
            // runs and the hook stays silent (Hookable partition contract).
            Assert.Equal(0, o.Hookable("custom_hook", () => 0));
            Assert.False(fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Bans_LiveInIpBanStore()
    {
        ObjectRegistry.ClearAll();
        try
        {
            ObjectRegistry.BanIp("10.0.0.1", 9999999999);
            Assert.True(IpBanStore.IsIpBanned("10.0.0.1"));
            IpBanStore.UnbanIp("10.0.0.1");
            Assert.False(ObjectRegistry.IsIpBanned("10.0.0.1"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Cooldowns_LiveInCreationCooldownStore()
    {
        ObjectRegistry.ClearAll();
        try
        {
            ObjectRegistry.ApplyCreationCooldown("account", "10.0.0.2", 1000, 60);
            Assert.True(CreationCooldownStore.CreationCooldownActive("10.0.0.2", 1010));
            CreationCooldownStore.ClearCreationCooldown("10.0.0.2");
            Assert.False(ObjectRegistry.CreationCooldownActive("10.0.0.2", 1010));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void BoundedDictionary_IsTopLevel()
    {
        Assert.NotNull(typeof(BoundedDictionary<,>));
        Assert.Null(typeof(ObjectRegistry).GetNestedType("BoundedDictionary`2"));
    }

    [Fact]
    public void SetAsyncTicker_IsLiveForTryGet()
    {
        var prior = GlobalServices.TryGetTicker();
        try
        {
            var t = GlobalServices.GetAsyncTicker();
            GlobalServices.SetAsyncTicker(t);
            Assert.Same(t, GlobalServices.TryGetTicker());
        }
        finally { GlobalServices.Reset(); }
    }

    [Fact]
    public void FaultedCreation_RetriesInsteadOfWedging()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.Reset();
        try
        {
            // Default settings carry an invalid SavePath outside a game
            // folder, so pinned creation fails like DoStartup's boot attempt
            // with a bad ambient path — and must not wedge the slot.
            var bad = new AtherizSettings();
            Assert.Throws<InvalidOperationException>(() => GlobalServices.GetNodeHandler(bad));
            // The fault stores nothing ...
            Assert.Null(GlobalServices.TryGetNodeHandler());
            // ... and the next ambient read retries instead of rethrowing.
            var h = GlobalServices.GetNodeHandler();
            Assert.NotNull(h);
            // First success wins: the pinned overload now sees the ambient one.
            Assert.Same(h, GlobalServices.GetNodeHandler(bad));
        }
        finally { GlobalServices.Reset(); }
    }
}
