using System.Reflection;

namespace Atheriz.Core.Tests.Features.Hosting;

// The three per-game build scripts (build.sh/build.cmd/build.ps1, generated
// by GameTemplateGenerator) must enforce one engine-freshness rule: missing
// dll or any engine source newer than it means rebuild. These pins render
// the templates and require the same rule sentinels in all three, so a
// future edit to one script cannot silently diverge the others.
[Collection("Ported")]
public class TemplateScriptParityTests
{
    private static string Render(string method)
    {
        var t = typeof(Atheriz.Server.Cli.NewHandler).Assembly
            .GetType("Atheriz.Server.Infrastructure.GameTemplateGenerator");
        Assert.NotNull(t);
        var m = t!.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        var rendered = m!.Invoke(null, ["paritygame", ".."]);
        Assert.IsType<string>(rendered);
        return (string)rendered!;
    }

    private static (string Sh, string Cmd, string Ps1) All() =>
        (Render("BuildSh"), Render("BuildCmd"), Render("BuildPs1"));

    [Fact]
    public void BuildScripts_AllWatchSameRootFiles()
    {
        // global.json pins the SDK a rebuild would use; a changed pin with a
        // fresh dll would otherwise compile the plugin against a newer
        // engine than the running server. All three scripts watch the same
        // three root files.
        var (sh, cmd, ps1) = All();
        foreach (var script in new[] { sh, cmd, ps1 })
        {
            Assert.Contains("Directory.Build.props", script);
            Assert.Contains("Directory.Packages.props", script);
            Assert.Contains("global.json", script);
        }
    }

    [Fact]
    public void BuildScripts_AllResolveEngineTheSameWay()
    {
        // ATHERIZ_ROOT wins everywhere; otherwise every script searches
        // upward for the checkout before falling back to the baked path, so
        // a moved game folder keeps working on every platform.
        var (sh, cmd, ps1) = All();
        Assert.Contains("ATHERIZ_ROOT", sh);
        Assert.Contains("ATHERIZ_ROOT", cmd);
        Assert.Contains("ATHERIZ_ROOT", ps1);
        Assert.Contains("-lt 12", sh);
        Assert.Contains("-lt 12", ps1);
        Assert.Contains("lss 12", cmd);
        Assert.Contains("src/Atheriz.Server/Atheriz.Server.csproj", sh);
        Assert.Contains("src/Atheriz.Server/Atheriz.Server.csproj", ps1);
        Assert.Contains(@"src\Atheriz.Server\Atheriz.Server.csproj", cmd);
    }

    [Fact]
    public void BuildScripts_EngineScanScopedToSrc()
    {
        // Game sources must never read as engine staleness: a game edit
        // must not trigger an engine rebuild on any platform.
        var (sh, cmd, ps1) = All();
        Assert.Contains("find \"$ENGINE_ROOT/src\"", sh);
        Assert.Contains("'src/*'", ps1);
        Assert.Contains("Join-Path $env:ENGINE_ROOT 'src'", cmd);
        Assert.DoesNotContain("-LiteralPath $env:ENGINE_ROOT -Recurse", cmd);
    }

    [Fact]
    public void BuildScripts_ShareStaleContract()
    {
        var (sh, cmd, ps1) = All();
        foreach (var script in new[] { sh, cmd, ps1 })
        {
            Assert.Contains("build the server (when stale) AND the plugin", script);
            Assert.Contains("--no-engine", script);
            Assert.Contains("Engine server is stale - rebuilding...", script);
            Assert.Contains("Engine server is up to date - skipping.", script);
            Assert.Contains("paritygame.csproj", script);
        }
    }

    [Fact]
    public void BuildScripts_EachHasAFreshnessProbe()
    {
        var (sh, cmd, ps1) = All();
        Assert.Contains("engine_stale", sh);
        Assert.Contains(":ensure_engine", cmd);
        Assert.Contains("Test-EngineStale", ps1);
    }

    [Fact]
    public void BuildScripts_AllExcludeBuildOutput()
    {
        // Build outputs must never count as sources, or every build would
        // look stale immediately afterwards.
        var (sh, cmd, ps1) = All();
        Assert.Contains("bin", sh);
        Assert.Contains("obj", sh);
        Assert.Contains("bin", cmd);
        Assert.Contains("obj", cmd);
        Assert.Contains("bin", ps1);
        Assert.Contains("obj", ps1);
    }
}
