using Atheriz.Core.Settings;

namespace Atheriz.Core.Plugins;

// One build-first entry point for every command that must run on fresh
// binaries: CLI reset/restart, the in-game reset verb, and the server side
// of the in-game restart. Engine first (a stale server runs an older
// Atheriz.Core than a fresh plugin, which the loader unifies away to zero
// game replacements), then every stale game project. Any failure aborts with
// Ok false and live code untouched — callers fail loudly instead of
// starting/wiping/reloading on stale or half-built binaries.
public static class BuildCoordinator
{
    /// <summary>
    /// Outcome of a build-first pass. <c>Ok</c> false means something failed
    /// to build: the caller must abort and leave live state untouched.
    /// </summary>
    public sealed record BuildAllResult(bool Ok, bool EngineBuilt, int GameBuilt, string Summary);

    /// <summary>
    /// Builds the engine server when stale, then every stale game project,
    /// then the webclient bundle when its sources are newer than dist/
    /// (rebuilt with npm and re-staged into the game's web folder).
    /// Pass explicit roots/projects in tests; the defaults discover from the
    /// environment exactly like the per-game scripts and the reload path do.
    /// </summary>
    public static async Task<BuildAllResult> EnsureAllBuiltAsync(
        AtherizSettings settings,
        string? engineRoot = null,
        IReadOnlyList<string>? gameProjects = null,
        Func<string, CancellationToken, Task<bool>>? engineBuild = null,
        Func<string, CancellationToken, Task<bool>>? gameBuild = null,
        Func<AtherizSettings, string?, CancellationToken, Task<WebclientBuilder.WebclientBuildResult>>? webclientBuild = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var engine = await EngineBuilder.EnsureEngineBuiltAsync(engineRoot, engineBuild, cancellationToken).ConfigureAwait(false);
        if (!engine.Ok) return new BuildAllResult(false, false, 0, $"Build failed: {engine.Message}");
        var projects = gameProjects ?? PluginBuilder.FindGameProjects(settings);
        var built = 0;
        foreach (var csproj in projects)
        {
            if (PluginReloader.IsExcludedAssembly(csproj)) continue;
            bool stale;
            try { stale = PluginBuilder.DllNeedsBuild(csproj, out _); }
            catch (Exception ex)
            {
                return new BuildAllResult(false, engine.WasBuilt, built,
                    $"Build failed: {Path.GetFileName(csproj)}: staleness check failed: {ex.Message}");
            }
            if (!stale) continue;
            bool ok;
            try { ok = await (gameBuild ?? ((csproj, ct) => PluginBuilder.BuildAsync(csproj, ct)))(csproj, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                return new BuildAllResult(false, engine.WasBuilt, built,
                    $"Build failed: {Path.GetFileName(csproj)}: build threw: {ex.Message}");
            }
            if (!ok)
            {
                return new BuildAllResult(false, engine.WasBuilt, built,
                    $"Build failed: {Path.GetFileName(csproj)}: build failed. Live code untouched.");
            }
            built++;
        }
        var summary = $"Build check done: engine {(engine.WasBuilt ? "rebuilt" : "up to date")}, {built} game project(s) built.";
        // The browser bundle is a build artifact too: a webclient-only
        // change otherwise sits unserved until someone rebuilds by hand.
        var webRoot = engineRoot;
        if (string.IsNullOrWhiteSpace(webRoot) && !EngineBuilder.TryResolveEngineRoot(out webRoot))
            webRoot = null;
        WebclientBuilder.WebclientBuildResult web;
        try
        {
            web = await (webclientBuild ?? DefaultWebclientBuildAsync)(settings, webRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new BuildAllResult(false, engine.WasBuilt, built,
                $"Build failed: webclient step threw: {ex.Message}");
        }
        if (!web.Ok)
        {
            return new BuildAllResult(false, engine.WasBuilt, built,
                $"Build failed: {web.Message} Live code untouched.");
        }
        return new BuildAllResult(true, engine.WasBuilt, built,
            $"{summary} Webclient: {web.Message.ToLowerInvariant()}.");
    }

    private static Task<WebclientBuilder.WebclientBuildResult> DefaultWebclientBuildAsync(
        AtherizSettings settings, string? webRoot, CancellationToken cancellationToken)
    {
        var gameWeb = WebclientBuilder.DeriveGameWebRoot(settings);
        if (string.IsNullOrWhiteSpace(webRoot) || string.IsNullOrWhiteSpace(gameWeb))
            return Task.FromResult(new WebclientBuilder.WebclientBuildResult(
                true, false, "webclient skipped: no checkout or game web folder"));
        return WebclientBuilder.EnsureWebclientDeployedAsync(webRoot, gameWeb, cancellationToken: cancellationToken);
    }
}
