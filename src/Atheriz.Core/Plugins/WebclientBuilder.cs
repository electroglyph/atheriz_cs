using System.Diagnostics;
using System.Text;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Plugins;

// Webclient build-first step for reset/restart (CLI and in-game verbs all
// funnel through BuildCoordinator). The C# builds never touched the browser
// bundle, so a webclient-only change sat unserved until someone rebuilt by
// hand: this step rebuilds (vite) and re-stages the game static dir whenever
// the sources are newer than the last build. "As needed" is a pure mtime
// rule, mirroring EngineBuilder/DllNeedsBuild: no npm run when fresh.
// A missing toolchain or webclient checkout skips with a loud summary note
// instead of aborting — the server stays restartable without node — but an
// attempted build that fails aborts the caller like any other build step.
public static class WebclientBuilder
{
    public sealed record WebclientBuildResult(bool Ok, bool WasBuilt, string Message);

    internal static readonly TimeSpan NpmVersionTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan NpmInstallTimeout = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan NpmBuildTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runner seam: (workingDirectory, arguments, token) -> (ok, output).
    /// The default shells out to npm; tests inject a stub that fakes dist/.
    /// </summary>
    public delegate Task<(bool Ok, string Output)> NpmRunner(
        string workingDirectory, IReadOnlyList<string> args, CancellationToken cancellationToken);

    /// <summary>
    /// True when the webclient checkout exists at engineRoot/webclient.
    /// </summary>
    public static bool TryResolveWebclientRoot(string engineRoot, out string? webclientRoot)
    {
        webclientRoot = null;
        try
        {
            var candidate = Path.Combine(engineRoot, "webclient");
            if (File.Exists(Path.Combine(candidate, "package.json"))
                && Directory.Exists(Path.Combine(candidate, "src")))
            {
                webclientRoot = candidate;
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// The game web folder derived from settings: the <c>web</c> sibling of
    /// the save leaf's parent (i.e. <c>&lt;game&gt;/web</c>). Null when the
    /// save path has no parent to hang it off.
    /// </summary>
    public static string? DeriveGameWebRoot(AtherizSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var saveFull = Path.GetFullPath(settings.SavePath);
            var gameDir = Path.GetDirectoryName(saveFull);
            if (string.IsNullOrEmpty(gameDir)) return null;
            return Path.Combine(gameDir, "web");
        }
        catch { return null; }
    }

    /// <summary>
    /// True when dist/ is missing its bundles or any watched source is newer
    /// than the newest dist output. Watched: src/**, public/**, configs and
    /// entry pages; node_modules and dist itself are never inputs.
    /// </summary>
    public static bool DistIsStale(string webclientRoot, out string reason)
    {
        reason = "";
        try
        {
            var dist = Path.Combine(webclientRoot, "dist");
            var assets = Path.Combine(dist, "assets");
            if (!Directory.Exists(assets)
                || Directory.GetFiles(assets, "webclient-*.js").Length == 0)
            {
                reason = "no built webclient bundle";
                return true;
            }
            var distNewest = Directory.GetFiles(dist, "*", SearchOption.AllDirectories)
                .Select(f => File.GetLastWriteTimeUtc(f))
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            string? newer = null;
            void Scan(string dir)
            {
                if (newer is not null || !Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(webclientRoot, f);
                    if (rel.StartsWith("node_modules", StringComparison.Ordinal)
                        || rel.StartsWith("dist", StringComparison.Ordinal)) continue;
                    if (File.GetLastWriteTimeUtc(f) > distNewest)
                    {
                        newer = rel;
                        return;
                    }
                }
            }
            Scan(Path.Combine(webclientRoot, "src"));
            Scan(Path.Combine(webclientRoot, "public"));
            foreach (var name in new[] { "package.json", "package-lock.json", "vite.config.ts", "tsconfig.json", "tsconfig.tests.json", "index.html" })
            {
                if (newer is not null) break;
                var f = Path.Combine(webclientRoot, name);
                if (File.Exists(f) && File.GetLastWriteTimeUtc(f) > distNewest) newer = name;
            }
            var entry = Path.Combine(webclientRoot, "webclient", "index.html");
            if (newer is null && File.Exists(entry) && File.GetLastWriteTimeUtc(entry) > distNewest)
                newer = Path.Combine("webclient", "index.html");
            if (newer is not null)
            {
                reason = $"{newer} is newer than the built bundle";
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Rebuilds the bundle when stale; no staging. Used by the explicit
    /// <c>deploy game</c> verb (which stages itself) and by
    /// <see cref="EnsureWebclientDeployedAsync"/>.
    /// </summary>
    public static async Task<WebclientBuildResult> EnsureBuiltAsync(
        string webclientRoot,
        NpmRunner? runNpm = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webclientRoot) || !File.Exists(Path.Combine(webclientRoot, "package.json")))
            return new WebclientBuildResult(true, false, "webclient skipped: no webclient checkout");
        bool stale;
        string reason;
        try { stale = DistIsStale(webclientRoot, out reason); }
        catch (Exception ex) { return new WebclientBuildResult(false, false, $"Build failed: webclient staleness check failed: {ex.Message}"); }
        if (!stale)
            return new WebclientBuildResult(true, false, "webclient up to date");
        var runner = runNpm ?? RunNpmAsync;
        Console.Error.WriteLine($"Webclient bundle stale ({reason}); rebuilding...");
        using var versionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        versionCts.CancelAfter(NpmVersionTimeout);
        var (versionOk, versionOut) = await runner(webclientRoot, ["--version"], versionCts.Token).ConfigureAwait(false);
        if (!versionOk)
            return new WebclientBuildResult(true, false,
                $"webclient skipped: npm not usable ({FirstLine(versionOut)}); serving existing bundle");
        if (InstallNeeded(webclientRoot))
        {
            Console.Error.WriteLine("Installing webclient dependencies...");
            using var installCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            installCts.CancelAfter(NpmInstallTimeout);
            var (installOk, installOut) = await runner(webclientRoot, ["install"], installCts.Token).ConfigureAwait(false);
            if (!installOk)
                return new WebclientBuildResult(false, false,
                    $"Build failed: npm install failed: {LastLines(installOut, 15)}");
        }
        using var buildCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        buildCts.CancelAfter(NpmBuildTimeout);
        var (buildOk, buildOut) = await runner(webclientRoot, ["run", "build"], buildCts.Token).ConfigureAwait(false);
        if (!buildOk)
            return new WebclientBuildResult(false, false,
                $"Build failed: webclient build failed: {LastLines(buildOut, 15)}");
        return new WebclientBuildResult(true, true, "webclient rebuilt");
    }

    /// <summary>
    /// Rebuilds and re-stages the webclient when stale; no-op when fresh.
    /// Never throws: every failure mode returns <c>Ok</c> false (attempted
    /// build failed) or a skip note (nothing to do / no toolchain).
    /// </summary>
    public static async Task<WebclientBuildResult> EnsureWebclientDeployedAsync(
        string engineRoot,
        string? gameWebRoot,
        NpmRunner? runNpm = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveWebclientRoot(engineRoot, out var webclientRoot) || webclientRoot is null)
            return new WebclientBuildResult(true, false, "webclient skipped: no webclient checkout");
        if (string.IsNullOrWhiteSpace(gameWebRoot) || !Directory.Exists(gameWebRoot))
            return new WebclientBuildResult(true, false, "webclient skipped: no game web folder");
        var built = await EnsureBuiltAsync(webclientRoot, runNpm, cancellationToken).ConfigureAwait(false);
        if (!built.Ok || !built.WasBuilt)
            return built;
        try
        {
            var staticRoot = Path.Combine(gameWebRoot, "static");
            Directory.CreateDirectory(staticRoot);
            StageBundles(
                Path.Combine(webclientRoot, "dist"),
                Path.Combine(webclientRoot, "fonts"),
                staticRoot);
        }
        catch (Exception ex)
        {
            return new WebclientBuildResult(false, true,
                $"Build failed: webclient staging failed: {ex.Message}");
        }
        return new WebclientBuildResult(true, true, "webclient rebuilt");
    }

    private static bool InstallNeeded(string webclientRoot)
    {
        try
        {
            var modules = Path.Combine(webclientRoot, "node_modules");
            if (!Directory.Exists(modules)) return true;
            var modulesTime = Directory.GetLastWriteTimeUtc(modules);
            foreach (var name in new[] { "package.json", "package-lock.json" })
            {
                var f = Path.Combine(webclientRoot, name);
                if (File.Exists(f) && File.GetLastWriteTimeUtc(f) > modulesTime) return true;
            }
        }
        catch { return true; }
        return false;
    }

    // Staging mirrors Atheriz.Server.Deployment.WebDeployer.StageDist minus
    // the landing-template sync (an explicit-deploy concern: restart must
    // never overwrite a game's customized landing page). Keep the file sets
    // identical — a bundle the deploy verb would not stage is a bundle the
    // game cannot serve.
    public static void StageBundles(string distRoot, string? fontsDir, string staticRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(staticRoot);
        CopyTree(Path.Combine(distRoot, "assets"), Path.Combine(staticRoot, "assets"));
        if (fontsDir is not null && Directory.Exists(fontsDir))
            CopyTree(fontsDir, Path.Combine(staticRoot, "fonts"));
        CopyFile(
            Path.Combine(distRoot, "webclient", "index.html"),
            Path.Combine(staticRoot, "webclient", "index.html"));
        CopyFile(
            Path.Combine(distRoot, "index.html"),
            Path.Combine(staticRoot, "atheriz_draw", "index.html"));
        var serverlog = Directory.GetFiles(Path.Combine(distRoot, "assets"), "serverlog-*.js").Order().ToArray();
        if (serverlog.Length > 0)
            CopyFile(serverlog[0], Path.Combine(staticRoot, "assets", "serverlog.js"));
        var chafaSrc = Path.Combine(distRoot, "chafa.wasm");
        if (File.Exists(chafaSrc))
        {
            CopyFile(chafaSrc, Path.Combine(staticRoot, "chafa.wasm"));
        }
        else if (!File.Exists(Path.Combine(staticRoot, "chafa.wasm")))
        {
            var candidates = Directory.GetFiles(Path.Combine(distRoot, "assets"), "chafa*.wasm").Order().ToArray();
            if (candidates.Length > 0)
                CopyFile(candidates[0], Path.Combine(staticRoot, "chafa.wasm"));
        }
        var gfontsSrc = Path.Combine(distRoot, "gfonts");
        if (Directory.Exists(gfontsSrc))
            CopyTree(gfontsSrc, Path.Combine(staticRoot, "gfonts"));
    }

    private static void CopyTree(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new FileNotFoundException($"Missing build directory: {source}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void CopyFile(string source, string destination)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException($"Missing build file: {source}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static string FirstLine(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return "no output";
    }

    private static string LastLines(string output, int count)
    {
        var lines = output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        return string.Join('\n', lines.Skip(Math.Max(0, lines.Length - count)));
    }

    // Default runner: npm from PATH (npm.cmd on Windows). Mirrors
    // PluginBuilder.BuildAsync: ArgumentList quoting, UTF-8 capture, output
    // kept for the failure summary only.
    internal static async Task<(bool Ok, string Output)> RunNpmAsync(
        string workingDirectory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        foreach (var exe in OperatingSystem.IsWindows() ? new[] { "npm.cmd", "npm" } : new[] { "npm" })
        {
            var startInfo = new ProcessStartInfo(exe)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) startInfo.ArgumentList.Add(a);
            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            try
            {
                if (!process.Start()) continue;
            }
            catch
            {
                continue;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (false, output + "\ntimed out");
            }
            return (process.ExitCode == 0, output.ToString());
        }
        return (false, "npm not found on PATH");
    }
}
