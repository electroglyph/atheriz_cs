using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Plugins;

// restart/reset must rebuild the browser bundle "as needed": stale sources
// rebuild + redeploy, fresh trees never run npm, and a missing checkout or
// game web folder skips loudly instead of aborting. The npm runner is always
// a stub here; no real node runs are involved.
[Collection("Ported")]
public class WebclientBuilderTests
{
    private static string StageWebclientRoot(string prefix, bool withDist)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        var wc = Path.Combine(root, "webclient");
        Directory.CreateDirectory(Path.Combine(wc, "src"));
        File.WriteAllText(Path.Combine(wc, "package.json"), "{}");
        File.WriteAllText(Path.Combine(wc, "src", "main.ts"), "// ui");
        if (withDist)
        {
            var assets = Path.Combine(wc, "dist", "assets");
            Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, "webclient-abc123.js"), [1, 2, 3]);
            var page = Path.Combine(wc, "dist", "webclient");
            Directory.CreateDirectory(page);
            File.WriteAllText(Path.Combine(page, "index.html"), "<html></html>");
            File.WriteAllText(Path.Combine(wc, "dist", "index.html"), "<html></html>");
        }
        return root;
    }

    private static string StageGameWeb(string prefix)
    {
        var gameDir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(gameDir, "save"));
        Directory.CreateDirectory(Path.Combine(gameDir, "web"));
        return gameDir;
    }

    [Fact]
    public void DistIsStale_MissingBundle_IsStale()
    {
        var root = StageWebclientRoot("wcb_nodist_", withDist: false);
        try
        {
            Assert.True(WebclientBuilder.DistIsStale(Path.Combine(root, "webclient"), out var reason));
            Assert.Contains("no built webclient bundle", reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void DistIsStale_FreshTree_IsFresh()
    {
        var root = StageWebclientRoot("wcb_fresh_", withDist: true);
        try
        {
            Assert.False(WebclientBuilder.DistIsStale(Path.Combine(root, "webclient"), out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void DistIsStale_TouchedSource_IsStale()
    {
        var root = StageWebclientRoot("wcb_touch_", withDist: true);
        try
        {
            var wc = Path.Combine(root, "webclient");
            File.SetLastWriteTimeUtc(
                Path.Combine(wc, "src", "main.ts"),
                File.GetLastWriteTimeUtc(Path.Combine(wc, "dist", "assets", "webclient-abc123.js")) + TimeSpan.FromSeconds(5));
            Assert.True(WebclientBuilder.DistIsStale(wc, out var reason));
            Assert.Contains("main.ts", reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task EnsureWebclientDeployed_FreshTree_NeverRunsNpm()
    {
        var root = StageWebclientRoot("wcb_nonpm_", withDist: true);
        var gameDir = StageGameWeb("wcb_nonpm_game_");
        try
        {
            var calls = 0;
            var result = await WebclientBuilder.EnsureWebclientDeployedAsync(
                root, Path.Combine(gameDir, "web"),
                runNpm: (_, _, _) => { calls++; return Task.FromResult((true, "")); });
            Assert.True(result.Ok);
            Assert.False(result.WasBuilt);
            Assert.Equal(0, calls);
            Assert.Contains("up to date", result.Message);
        }
        finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(gameDir, true); } catch { } }
    }

    [Fact]
    public async Task EnsureWebclientDeployed_StaleTree_RebuildsAndStages()
    {
        var root = StageWebclientRoot("wcb_stage_", withDist: false);
        var gameDir = StageGameWeb("wcb_stage_game_");
        try
        {
            var calls = new List<string>();
            var result = await WebclientBuilder.EnsureWebclientDeployedAsync(
                root, Path.Combine(gameDir, "web"),
                runNpm: (dir, args, _) =>
                {
                    calls.Add(string.Join(" ", args));
                    if (args is ["--version"]) return Task.FromResult((true, "10.9.8"));
                    // Fake what `npm run build` would emit: a fresh bundle.
                    var wc = Path.Combine(root, "webclient");
                    var assets = Path.Combine(wc, "dist", "assets");
                    Directory.CreateDirectory(assets);
                    File.WriteAllBytes(Path.Combine(assets, "webclient-newbundle.js"), [9, 9]);
                    var page = Path.Combine(wc, "dist", "webclient");
                    Directory.CreateDirectory(page);
                    File.WriteAllText(Path.Combine(page, "index.html"), "<html>new</html>");
                    File.WriteAllText(Path.Combine(wc, "dist", "index.html"), "<html>new</html>");
                    return Task.FromResult((true, "built"));
                });
            Assert.True(result.Ok);
            Assert.True(result.WasBuilt);
            Assert.Contains(calls, c => c == "--version");
            Assert.Contains(calls, c => c == "run build");
            Assert.True(File.Exists(Path.Combine(gameDir, "web", "static", "assets", "webclient-newbundle.js")));
            Assert.True(File.Exists(Path.Combine(gameDir, "web", "static", "webclient", "index.html")));
        }
        finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(gameDir, true); } catch { } }
    }

    [Fact]
    public async Task EnsureWebclientDeployed_BuildFailure_ReturnsNotOk()
    {
        var root = StageWebclientRoot("wcb_fail_", withDist: false);
        var gameDir = StageGameWeb("wcb_fail_game_");
        try
        {
            var result = await WebclientBuilder.EnsureWebclientDeployedAsync(
                root, Path.Combine(gameDir, "web"),
                runNpm: (_, args, _) => Task.FromResult(args is ["--version"] ? (true, "10.9.8") : (false, "vite exploded")));
            Assert.False(result.Ok);
            Assert.Contains("vite exploded", result.Message);
        }
        finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(gameDir, true); } catch { } }
    }

    [Fact]
    public async Task EnsureWebclientDeployed_MissingCheckout_SkipsCleanly()
    {
        var gameDir = StageGameWeb("wcb_nocheckout_game_");
        try
        {
            var result = await WebclientBuilder.EnsureWebclientDeployedAsync(
                Path.Combine(Path.GetTempPath(), "wcb_nope_" + Guid.NewGuid().ToString("N")),
                Path.Combine(gameDir, "web"));
            Assert.True(result.Ok);
            Assert.False(result.WasBuilt);
            Assert.Contains("no webclient checkout", result.Message);
        }
        finally { try { Directory.Delete(gameDir, true); } catch { } }
    }

    [Fact]
    public async Task BuildCoordinator_WebclientFailure_AbortsBeforeStop()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), "wcb_coord_" + Guid.NewGuid().ToString("N"));
        var saveDir = Path.Combine(gameDir, "save");
        Directory.CreateDirectory(saveDir);
        try
        {
            var settings = new AtherizSettings { SavePath = saveDir, SecretPath = Path.Combine(gameDir, "secret") };
            var engineRoot = StageWebclientRoot("wcb_coord_eng_", withDist: true);
            try
            {
                var result = await BuildCoordinator.EnsureAllBuiltAsync(
                    settings,
                    engineRoot: engineRoot,
                    gameProjects: [],
                    engineBuild: (_, _) => Task.FromResult(true),
                    webclientBuild: (_, _, _) => Task.FromResult(
                        new WebclientBuilder.WebclientBuildResult(false, false, "Build failed: npm exploded")));
                Assert.False(result.Ok);
                Assert.Contains("npm exploded", result.Summary);
            }
            finally { try { Directory.Delete(engineRoot, true); } catch { } }
        }
        finally { try { Directory.Delete(gameDir, true); } catch { } }
    }

    [Fact]
    public async Task BuildCoordinator_NoWebclientCheckout_StaysOk()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), "wcb_coordok_" + Guid.NewGuid().ToString("N"));
        var saveDir = Path.Combine(gameDir, "save");
        Directory.CreateDirectory(saveDir);
        try
        {
            var settings = new AtherizSettings { SavePath = saveDir, SecretPath = Path.Combine(gameDir, "secret") };
            var result = await BuildCoordinator.EnsureAllBuiltAsync(
                settings,
                engineRoot: Path.Combine(Path.GetTempPath(), "wcb_nope_" + Guid.NewGuid().ToString("N")),
                gameProjects: [],
                engineBuild: (_, _) => Task.FromResult(true));
            Assert.True(result.Ok);
            Assert.Contains("webclient", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(gameDir, true); } catch { } }
    }
}
