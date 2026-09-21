using System.Reflection;
using System.Runtime.Loader;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// Reload-must-refresh-commands pins: replacing a plugin load has to evict
// the dead load's command instances, or reinstalls keep them forever.
public sealed class EvictProbeCommand : Command
{
    public EvictProbeCommand(string key) => Key = key;
    public override string Key { get; }
    public override IReadOnlyList<string> Aliases => [];
    public override string Desc => "Eviction probe.";
    public override string Category => "Test";
    public override void Run(IMessageTarget caller, object? args) { }
}

[Collection("Ported")]
public class PluginCommandRefreshTests
{
    private static string CopyTestAssembly()
    {
        var src = typeof(PluginCommandRefreshTests).Assembly.Location;
        var dst = Path.Combine(Path.GetTempPath(), "atheriz_cmdprobe_" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(src, dst);
        File.SetLastWriteTimeUtc(dst, DateTime.UtcNow);
        return dst;
    }

    // Instantiates EvictProbeCommand from separately-loaded copies of this
    // test assembly (stand-ins for consecutive plugin loads). Test-only
    // reflection; production stays reflection-free outside the loader.
    private static (AssemblyLoadContext alc, Command cmd) LoadProbeFromCopy(string copy, string key)
    {
        var alc = new AssemblyLoadContext("probe-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        var asm = alc.LoadFromAssemblyPath(copy);
        var t = asm.GetType(typeof(EvictProbeCommand).FullName!);
        Assert.NotNull(t);
        var cmd = (Command)Activator.CreateInstance(t, [key])!;
        return (alc, cmd);
    }

    [Fact]
    public void EvictStaleCommands_RemovesOldLoadKeepsOthers()
    {
        var copyOld = CopyTestAssembly();
        var copyNew = CopyTestAssembly();
        AssemblyLoadContext? alcOld = null;
        AssemblyLoadContext? alcNew = null;
        try
        {
            (alcOld, var oldCmd) = LoadProbeFromCopy(copyOld, "oldverb");
            var oldAsm = oldCmd.GetType().Assembly;
            (alcNew, var newCmd) = LoadProbeFromCopy(copyNew, "newverb");
            // Same-load-type instance from this (running) assembly: stands in
            // for engine-assembly commands, which must survive.
            var keeper = new EvictProbeCommand("keeper");
            var set = new CmdSet();
            set.Add(oldCmd);
            set.Add(newCmd);
            set.Add(keeper);
            var evicted = PluginReloader.EvictStaleCommands(oldAsm, set);
            Assert.Equal(1, evicted);
            Assert.Null(set.Get("oldverb"));
            Assert.Same(newCmd, set.Get("newverb"));
            Assert.Same(keeper, set.Get("keeper"));
        }
        finally
        {
            try { alcOld?.Unload(); } catch { }
            try { alcNew?.Unload(); } catch { }
            try { File.Delete(copyOld); } catch { }
            try { File.Delete(copyNew); } catch { }
        }
    }

    [Fact]
    public void EvictStaleCommands_NullAssembly_EvictsNothing()
    {
        var set = new CmdSet();
        var cmd = new EvictProbeCommand("keeper");
        set.Add(cmd);
        Assert.Equal(0, PluginReloader.EvictStaleCommands(null, set));
        Assert.Same(cmd, set.Get("keeper"));
    }

    [Fact]
    public void WorldAlreadyConverted_EngineWorld_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.AddObject(new GameObject());
        var map = new Dictionary<Type, Type> { [typeof(GameObject)] = typeof(GameObject) };
        Assert.False(PluginReloader.WorldAlreadyConverted(map));
        Assert.False(PluginReloader.WorldAlreadyConverted(new Dictionary<Type, Type>()));
    }

    [Fact]
    public void WorldAlreadyConverted_PatchedWorld_ReturnsTrue()
    {
        using var env = GlobalTestEnv.Enter();
        var copy = CopyTestAssembly();
        AssemblyLoadContext? alc = null;
        try
        {
            alc = new AssemblyLoadContext("convprobe-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            var asm = alc.LoadFromAssemblyPath(copy);
            var t = asm.GetType(typeof(GoodProbeReplacement).FullName!);
            Assert.NotNull(t);
            ObjectRegistry.AddObject((GameObject)Activator.CreateInstance(t)!);
            var map = new Dictionary<Type, Type> { [typeof(GameObject)] = typeof(GoodProbeReplacement) };
            Assert.True(PluginReloader.WorldAlreadyConverted(map));
        }
        finally
        {
            try { alc?.Unload(); } catch { }
            try { File.Delete(copy); } catch { }
        }
    }
}
