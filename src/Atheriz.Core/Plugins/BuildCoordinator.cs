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
    /// Builds the engine server when stale, then every stale game project.
    /// Pass explicit roots/projects in tests; the defaults discover from the
    /// environment exactly like the per-game scripts and the reload path do.
    /// </summary>
    public static async Task<BuildAllResult> EnsureAllBuiltAsync(
        AtherizSettings settings,
        string? engineRoot = null,
        IReadOnlyList<string>? gameProjects = null,
        Func<string, CancellationToken, Task<bool>>? engineBuild = null,
        Func<string, CancellationToken, Task<bool>>? gameBuild = null,
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
        return new BuildAllResult(true, engine.WasBuilt, built, summary);
    }
}
