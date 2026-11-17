using Atheriz.Server.Deployment;

namespace Atheriz.Core.Tests.Features.Hosting;

// `deploy game` (which the per-game build.sh scripts delegate to) must
// build the bundle when stale, not just stage a stale dist/. The npm
// runner is always a stub here; staging hits real temp dirs.
[Collection("Ported")]
public class WebclientDeployBuildTests
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
            File.WriteAllBytes(Path.Combine(assets, "webclient-old.js"), [1]);
            var page = Path.Combine(wc, "dist", "webclient");
            Directory.CreateDirectory(page);
            File.WriteAllText(Path.Combine(page, "index.html"), "<html></html>");
            File.WriteAllText(Path.Combine(wc, "dist", "index.html"), "<html></html>");
        }
        return root;
    }

    private static WebclientBuilderNpmStub RebuildingStub(string webclientRoot) => new(webclientRoot);

    private sealed class WebclientBuilderNpmStub(string webclientRoot)
    {
        public readonly List<string> Calls = new();
        public bool FailBuild;
        public Task<(bool Ok, string Output)> Run(string dir, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(string.Join(" ", args));
            if (args is ["--version"]) return Task.FromResult((true, "10.9.8"));
            if (FailBuild) return Task.FromResult((false, "vite exploded"));
            var assets = Path.Combine(webclientRoot, "dist", "assets");
            Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, "webclient-fresh.js"), [9]);
            var page = Path.Combine(webclientRoot, "dist", "webclient");
            Directory.CreateDirectory(page);
            File.WriteAllText(Path.Combine(page, "index.html"), "<html>new</html>");
            File.WriteAllText(Path.Combine(webclientRoot, "dist", "index.html"), "<html>new</html>");
            return Task.FromResult((true, "built"));
        }
    }

    [Fact]
    public async Task DeployGame_StaleDist_RebuildsBeforeStaging()
    {
        var root = StageWebclientRoot(prefix: "wdep_build_", withDist: false);
        var gameWeb = Path.Combine(Path.GetTempPath(), "wdep_game_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(gameWeb, "web"));
        var template = Path.GetTempFileName();
        try
        {
            var stub = RebuildingStub(Path.Combine(root, "webclient"));
            var log = new StringWriter();
            var err = new StringWriter();
            var rc = await WebDeployer.DeployGameAsync(
                Path.Combine(root, "webclient", "dist"), fontsDir: null, template,
                Path.Combine(gameWeb, "web"), clean: true, log, err,
                runNpm: stub.Run);
            Assert.Equal(0, rc);
            Assert.Contains(stub.Calls, c => c == "run build");
            Assert.True(File.Exists(Path.Combine(gameWeb, "web", "static", "assets", "webclient-fresh.js")));
            Assert.Contains("rebuilt", log.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(gameWeb, true); } catch { } try { File.Delete(template); } catch { } }
    }

    [Fact]
    public async Task DeployGame_BuildFailure_AbortsWithoutStaging()
    {
        var root = StageWebclientRoot(prefix: "wdep_fail_", withDist: false);
        var gameWeb = Path.Combine(Path.GetTempPath(), "wdep_failgame_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(gameWeb, "web"));
        var template = Path.GetTempFileName();
        try
        {
            var stub = RebuildingStub(Path.Combine(root, "webclient"));
            stub.FailBuild = true;
            var log = new StringWriter();
            var err = new StringWriter();
            var rc = await WebDeployer.DeployGameAsync(
                Path.Combine(root, "webclient", "dist"), fontsDir: null, template,
                Path.Combine(gameWeb, "web"), clean: true, log, err,
                runNpm: stub.Run);
            Assert.Equal(1, rc);
            Assert.Contains("vite exploded", err.ToString());
            Assert.False(Directory.Exists(Path.Combine(gameWeb, "web", "static", "assets")));
        }
        finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(gameWeb, true); } catch { } try { File.Delete(template); } catch { } }
    }

    [Fact]
    public async Task DeployGame_NoWebclientCheckout_KeepsStageOnlyBehavior()
    {
        // A bare dist dir (test doubles, foreign layouts) is staged as-is;
        // missing dist still errors exactly like before.
        var tmp = Path.Combine(Path.GetTempPath(), "wdep_bare_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var template = Path.GetTempFileName();
        try
        {
            var log = new StringWriter();
            var err = new StringWriter();
            var rc = await WebDeployer.DeployGameAsync(
                Path.Combine(tmp, "nothing"), fontsDir: null, template,
                Path.Combine(tmp, "web"), clean: false, log, err);
            Assert.Equal(1, rc);
            Assert.Contains("Build output not found", err.ToString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } try { File.Delete(template); } catch { } }
    }

}
