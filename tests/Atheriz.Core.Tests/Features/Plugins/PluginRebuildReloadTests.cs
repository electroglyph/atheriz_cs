using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// Reload must rebuild stale game projects first (new code picked up without
// a separate build step), abort before touching live code when the build
// fails, and report a no-op when nothing changed. The Slow test drives the
// real path: dotnet build, source edit, reload, new behavior live.
[Collection("Ported")]
public class PluginRebuildReloadTests
{
    private const string AlphaGeneration = "GENERATION_ALPHA_7F3A9C";
    private const string BetaGeneration = "GENERATION_BETA_4D2E81";

    // Locate the engine project without hardcoding the checkout path: walk
    // up from the test binaries (bin/Release/net10.0 → repo root), then
    // from the current directory. Fails closed with a clear message instead
    // of silently referencing a stale absolute path.
    private static string CoreCsprojPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d is not null)
            {
                var cand = Path.Combine(d.FullName, "src", "Atheriz.Core", "Atheriz.Core.csproj");
                if (File.Exists(cand)) return cand;
                d = d.Parent;
            }
        }
        throw new InvalidOperationException("Atheriz.Core.csproj not found above test binaries or CWD.");
    }

    private static string StageGameDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "save"));
        return dir;
    }

    private static AtherizSettings GameSettings(string dir) =>
        new() { SavePath = Path.Combine(dir, "save") };

    private static void WriteDotnetProject(string dir, string name, string generation)
    {
        var coreCsproj = CoreCsprojPath();
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{coreCsproj}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Probe.cs"),
            $$"""
            using Atheriz.Core.Objects;
            using Atheriz.Core.Plugins;
            [assembly: EntityReplacement(typeof(GameObject), typeof(MiniPlugin.ProbeWidget))]
            namespace MiniPlugin;
            public sealed class ProbeWidget : GameObject
            {
                public const string Generation = "{{generation}}";
            }
            """);
    }

    private static bool DllContainsGeneration(string dllPath, string generation)
    {
        var dll = File.ReadAllBytes(dllPath);
        var needle = System.Text.Encoding.Unicode.GetBytes(generation);
        return dll.AsSpan().IndexOf(needle) >= 0;
    }

    private static string? LiveProbeGeneration()
    {
        var probe = ObjectRegistry.FilterBy(o =>
            string.Equals(o.GetType().FullName, "MiniPlugin.ProbeWidget", StringComparison.Ordinal)).FirstOrDefault();
        var field = probe?.GetType().GetField("Generation", BindingFlags.Public | BindingFlags.Static);
        return field?.GetRawConstantValue() as string;
    }

    [Fact]
    public async Task WithBuild_BuildFailure_AbortsWithoutReloading()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = StageGameDir("atheriz_rebuild_fail_");
        try
        {
            File.WriteAllText(Path.Combine(dir, "failgame.csproj"), "<Project />");
            var settings = GameSettings(dir);
            int calls = 0;
            Task<bool> build(string csproj, CancellationToken ct) { calls++; return Task.FromResult(false); }
            var result = await PluginReloader.ReloadGameLogicWithBuildAsync(
                GlobalServices.GetAsyncTicker(), GlobalServices.GetAsyncThreadPool(), settings, build, CancellationToken.None);
            Assert.StartsWith("Reload aborted", result);
            Assert.Contains("Live code untouched", result);
            Assert.Equal(1, calls);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task WithBuild_StaleProject_BuildsThenLoadsAndPatches()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = StageGameDir("atheriz_rebuild_load_");
        try
        {
            // Stale project (no dll yet) for the build phase; the fake build
            // stages this test assembly as the project's OWN bin output
            // (bin/Release/net10.0/fakegame.dll), which DiscoverGameAssembly
            // then picks up exactly like a real `dotnet build` product.
            File.WriteAllText(Path.Combine(dir, "fakegame.csproj"), "<Project />");
            ObjectRegistry.AddObject(new GameObject());
            var settings = GameSettings(dir);
            Task<bool> build(string csproj, CancellationToken ct)
            {
                var bin = Path.Combine(dir, "bin", "Release", "net10.0");
                Directory.CreateDirectory(bin);
                // WriteAllBytes (not File.Copy): File.Copy preserves the
                // source mtime, so the staged dll would predate the csproj
                // and look stale forever. A real build writes fresh bytes
                // with a now mtime; mimic that.
                var dllBytes = File.ReadAllBytes(typeof(PluginRebuildReloadTests).Assembly.Location);
                File.WriteAllBytes(Path.Combine(bin, "fakegame.dll"), dllBytes);
                return Task.FromResult(true);
            }
            var result = await PluginReloader.ReloadGameLogicWithBuildAsync(
                GlobalServices.GetAsyncTicker(), GlobalServices.GetAsyncThreadPool(), settings, build, CancellationToken.None);
            Assert.Contains("Built 1 project(s)", result);
            Assert.Contains("Reloaded 1 modules", result);
            Assert.Contains("Errors: 0", result);
            // The exact-type engine instance converted to the plugin
            // replacement, compared by full name across the ALC boundary.
            Assert.Contains(ObjectRegistry.FilterBy(_ => true),
                o => o.GetType().FullName == typeof(GoodProbeReplacement).FullName);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task WithBuild_UpToDateSecondCall_NeedsNoBuildAndNoops()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = StageGameDir("atheriz_rebuild_noop_");
        try
        {
            File.WriteAllText(Path.Combine(dir, "noopgame.csproj"), "<Project />");
            var settings = GameSettings(dir);
            int calls = 0;
            Task<bool> build(string csproj, CancellationToken ct)
            {
                calls++;
                var dllBytes = File.ReadAllBytes(typeof(PluginRebuildReloadTests).Assembly.Location);
                var plugins = Path.Combine(dir, "save", "plugins");
                Directory.CreateDirectory(plugins);
                File.WriteAllBytes(Path.Combine(plugins, "noopgame.dll"), dllBytes);
                // Also stamp the project's own output, or the csproj would
                // stay stale and every reload would rebuild.
                var bin = Path.Combine(dir, "bin", "Release", "net10.0");
                Directory.CreateDirectory(bin);
                File.WriteAllBytes(Path.Combine(bin, "noopgame.dll"), dllBytes);
                return Task.FromResult(true);
            }
            var ticker = GlobalServices.GetAsyncTicker();
            var pool = GlobalServices.GetAsyncThreadPool();
            var first = await PluginReloader.ReloadGameLogicWithBuildAsync(ticker, pool, settings, build, CancellationToken.None);
            Assert.Contains("Reloaded", first);
            Assert.Equal(1, calls);
            Task<bool> mustNotBuild(string csproj, CancellationToken ct) =>
                throw new InvalidOperationException("no build should run when nothing changed");
            var second = await PluginReloader.ReloadGameLogicWithBuildAsync(ticker, pool, settings, mustNotBuild, CancellationToken.None);
            Assert.Equal("Reload not needed; already up to date.", second);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WithBuild_RealBuild_Modify_Reload_PicksUpChange()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = StageGameDir("atheriz_rebuild_real_");
        try
        {
            WriteDotnetProject(dir, "minigame", AlphaGeneration);
            var settings = GameSettings(dir);
            var ticker = GlobalServices.GetAsyncTicker();
            var pool = GlobalServices.GetAsyncThreadPool();
            ObjectRegistry.AddObject(new GameObject());

            var first = await PluginReloader.ReloadGameLogicWithBuildAsync(ticker, pool, settings);
            Assert.Contains("Reloaded", first);
            Assert.Contains("Errors: 0", first);
            Assert.Equal(AlphaGeneration, LiveProbeGeneration());

            // Edit the source, reload, and the new generation must be live.
            WriteDotnetProject(dir, "minigame", BetaGeneration);
            var second = await PluginReloader.ReloadGameLogicWithBuildAsync(ticker, pool, settings);
            Assert.Contains("Built 1 project(s)", second);
            Assert.Contains("Reloaded", second);
            Assert.Contains("Errors: 0", second);
            var dllPath = Path.Combine(dir, "bin", "Release", "net10.0", "minigame.dll");
            Assert.True(DllContainsGeneration(dllPath, BetaGeneration), "rebuilt dll must contain the new generation");
            Assert.Equal(BetaGeneration, LiveProbeGeneration());

            // Nothing changed since: a no-op, not another conversion.
            var third = await PluginReloader.ReloadGameLogicWithBuildAsync(ticker, pool, settings);
            Assert.Equal("Reload not needed; already up to date.", third);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReloadCommand_UsesBuildFirstReload()
    {
        // The in-game `reload` verb must drive the build-first path, or code
        // edits silently need a separate build step again. Source pin (same
        // style as HotReloadBlocksLoop): the command is fire-and-forget, so
        // there is no result string to assert on behaviorally.
        var root = FindRepoRoot();
        var src = File.ReadAllText(Path.Combine(root, "src", "Atheriz.Core", "Commands", "LoggedIn", "AdminCommands.cs"));
        Assert.Contains("ReloadGameLogicWithBuildAsync", src);
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d is not null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "src"))) return d.FullName;
                d = d.Parent;
            }
        }
        throw new InvalidOperationException("repo root with src/ not found");
    }
}
