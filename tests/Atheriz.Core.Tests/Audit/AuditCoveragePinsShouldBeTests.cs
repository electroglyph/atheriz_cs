using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Audit;

// Behavior pins for audit §3 production surface with zero behavioral tests
// (FsUtil, MenuPrompt, ConnectionScreen, Logger.Rotate, InitialSetup wiring,
// PluginLoader success, GameUtils iter helpers, Pathfind overloads,
// AtherizSettings props). All are expected GREEN: they document what the code
// does today so future fixes/refactors cannot silently change it.
[Collection("Ported")]
public class AuditCoveragePinsShouldBeTests
{
    [Fact]
    public void FsUtil_TryChmod_MissingFile_NoThrow()
    {
        // "Try*" contract: failure is swallowed, never an exception.
        FsUtil.TryChmod0600(Path.Combine(Path.GetTempPath(), "no_such_atheriz_xyz"));
        FsUtil.TryChmod0700(Path.Combine(Path.GetTempPath(), "no_such_atheriz_xyz"));
    }

    [Fact]
    public void FsUtil_TryChmod_ExistingFile_NoThrow()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            FsUtil.TryChmod0600(tmp);
            FsUtil.TryChmod0700(tmp);
            Assert.True(File.Exists(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public async Task MenuPrompt_NegativeTimeout_ReturnsNull()
    {
        // Negative timeout: CancellationTokenSource throws, the bare catch
        // converts it to null (documented; change deliberately if ever fixed).
        var session = new Session();
        var result = await MenuPrompt.PromptWithTimeoutAsync(session, "display", TimeSpan.FromSeconds(-1));
        Assert.Null(result);
    }

    [Fact]
    public async Task MenuPrompt_ShortTimeout_ReturnsNull()
    {
        // No input arrives: the timeout elapses and the prompt yields null.
        var session = new Session();
        var result = await MenuPrompt.PromptWithTimeoutAsync(session, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(result);
    }

    [Fact]
    public void ConnectionScreen_Online_IsStableAndNonNegative()
    {
        // 5 s TTL cache: back-to-back reads agree and never go negative.
        var (o1, k1) = ConnectionScreen.GetOnline();
        var (o2, k2) = ConnectionScreen.GetOnline();
        Assert.True(o1 >= 0 && k1 >= 0);
        Assert.Equal((o1, k1), (o2, k2));
        Assert.False(string.IsNullOrWhiteSpace(ConnectionScreen.Render()));
    }

    [Fact]
    public void Logger_Rotate_ShiftsGenerations()
    {
        // 5-file rotation: r.log -> r.log.1 -> r.log.2, oldest dropped.
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_logrot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "r.log"), "A");
            File.WriteAllText(Path.Combine(dir, "r.log.1"), "B");
            AtherizLogger.Rotate(Path.Combine(dir, "r.log"));
            Assert.Equal("A", File.ReadAllText(Path.Combine(dir, "r.log.1")));
            Assert.Equal("B", File.ReadAllText(Path.Combine(dir, "r.log.2")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void InitialSetup_Wires_DashboardButtonChannelAdmin()
    {
        // audit §3.2: alarm/dashboard/button/channel wiring unpinned.
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_initpin_" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(tmp, "save");
        var secret = Path.Combine(tmp, "secret");
        try
        {
            InitialSetup.DoSetup(save, "admin", "password123", secret);
            var dash = ObjectRegistry.FilterBy(o => o.Name == "A flashing dashboard");
            Assert.Single(dash);
            Assert.True(dash[0].IsItem);
            var button = ObjectRegistry.FilterBy(o => o.Name == "A big red button");
            Assert.Single(button);
            Assert.NotNull(button[0].ExternalCmdSet!.Get("push"));
            var chan = ObjectRegistry.FilterBy(o => o.IsChannel && o.Name == "Server");
            Assert.Single(chan);
            var admin = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name == "admin");
            Assert.Single(admin);
            var hero = ObjectRegistry.FilterBy(o => o.IsPc && o.Name == "admin");
            Assert.Single(hero);
            Assert.Contains(chan[0].Id, hero[0].ChannelsSnapshot);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
            GlobalTestEnv.Enter().Dispose();
        }
    }

    [Fact]
    public void PluginLoader_LoadUnload_Roundtrip()
    {
        // audit §3.1: only the not-found throw was covered; success + unload
        // behavior was unpinned.
        var src = typeof(AuditCoveragePinsShouldBeTests).Assembly.Location;
        var copy = Path.Combine(Path.GetTempPath(), "atheriz_plugin_" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(src, copy);
        try
        {
            using var loader = new PluginLoader();
            loader.Load(copy);
            Assert.True(loader.IsLoaded);
            loader.Unload();
            Assert.False(loader.IsLoaded);
            Assert.Empty(loader.Replacements);
        }
        finally { try { File.Delete(copy); } catch { } }
    }

    [Fact]
    public void GameUtils_IterHelpers()
    {
        Assert.False(GameUtils.IsIter("s"));
        Assert.False(GameUtils.IsIter(null));
        Assert.True(GameUtils.IsIter(new[] { 1, 2 }));
        Assert.Equal(new object?[] { 5 }, GameUtils.MakeIter(5));
        Assert.Equal(new object?[] { "s" }, GameUtils.MakeIter("s"));
        Assert.Equal("x", GameUtils.Detach("x"));
        GameUtils.EnsureThreadSafe(typeof(GameObject));
    }

    [Fact]
    public void Pathfind_Overloads_Agree_OnOpenGrid()
    {
        // audit §3.2: the door-aware overload + GetNeighbors unpinned.
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coordA = new Coord("limbo", 0, 0, 0);
            var coordB = new Coord("limbo", 2, 0, 0);
            var a = new Node(coordA);
            var b = new Node(coordB);
            if (ObjectRegistry.Get(a.Id).Count == 0) ObjectRegistry.AddObject(a);
            if (ObjectRegistry.Get(b.Id).Count == 0) ObjectRegistry.AddObject(b);
            a.AddLink(new NodeLink("east", coordB, new List<string> { "e" }));
            b.AddLink(new NodeLink("west", coordA, new List<string> { "w" }));
            nh.AddNode(a);
            nh.AddNode(b);
            var plain = Pathfind.FindPath(coordA, coordB, nh);
            var caller = GameObject.Create("walker");
            ObjectRegistry.AddObject(caller);
            var doorAware = Pathfind.FindPath(coordA, coordB, nh, caller);
            Assert.NotNull(plain);
            Assert.NotNull(doorAware);
            Assert.Equal(plain!, doorAware!);
            Assert.Contains(coordB, Pathfind.GetNeighbors(coordA, nh));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtherizSettings_SpotDefaults_Sane()
    {
        var s = new AtherizSettings();
        Assert.NotEmpty(s.NetworkProtocols);
        Assert.NotEmpty(s.AllSymbols);
        Assert.False(string.IsNullOrEmpty(s.NsClosedDoor));
    }
}
