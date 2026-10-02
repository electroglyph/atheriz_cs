namespace Atheriz.Server.Deployment;

// C# port of webclient/deploy.py (deleted): stages the built webclient
// (dist/) into a game web/static dir and syncs the engine landing
// template into the game web dir. Staging never builds — vite stays in
// build.sh, so this is deploy.py's --no-build path made the only path.
// All roots are explicit parameters (never CWD-sniffed) so tests drive
// the real logic against temp dirs; the CLI verb resolves them.
public static class WebDeployer
{
    // Game deploy entry point. Returns a process exit code; expected
    // failures (missing dist/template) print to err and return 1, so the
    // CLI contract is exit 0/nonzero plus stderr, no stack traces.
    public static int DeployGame(
        string distRoot, string? fontsDir, string engineTemplateFile,
        string gameWebRoot, bool clean, TextWriter log, TextWriter err)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineTemplateFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameWebRoot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(err);
        if (!Directory.Exists(distRoot))
        {
            err.WriteLine($"Build output not found at {distRoot}; run `npm run build` first in webclient/");
            return 1;
        }
        var staticRoot = Path.Combine(gameWebRoot, "static");
        Directory.CreateDirectory(staticRoot);
        try
        {
            if (clean)
                CleanGeneratedOutput(staticRoot);
            StageDist(distRoot, fontsDir, staticRoot, log);
        }
        catch (Exception ex)
        {
            err.WriteLine($"error: {ex.Message}");
            return 1;
        }
        if (!File.Exists(engineTemplateFile))
        {
            err.WriteLine($"Engine landing template not found at {engineTemplateFile}");
            return 1;
        }
        SyncLandingTemplate(engineTemplateFile, gameWebRoot, log);
        return 0;
    }

    // Remove only paths owned by the build, never arbitrary game assets.
    // The game deploy preserves a legacy webclient/ tree and refreshes
    // just its entry page (deploy.py's dead package target used to wipe
    // the whole tree; nothing invokes that target anymore).
    public static void CleanGeneratedOutput(string staticRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staticRoot);
        foreach (var rel in new[] { "assets", "atheriz_draw", "chafa.wasm", "gfonts" })
            RemovePath(Path.Combine(staticRoot, rel));
        RemovePath(Path.Combine(staticRoot, "webclient", "index.html"));
    }

    // Stage one built dist/ over the game static dir. Files absent from
    // this build (e.g. gfonts when public/gfonts is missing) leave any
    // previously staged copy in place.
    public static void StageDist(string distRoot, string? fontsDir, string staticRoot, TextWriter log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(staticRoot);
        ArgumentNullException.ThrowIfNull(log);
        CopyTree(Path.Combine(distRoot, "assets"), Path.Combine(staticRoot, "assets"));
        if (fontsDir is not null && Directory.Exists(fontsDir))
            CopyTree(fontsDir, Path.Combine(staticRoot, "fonts"));
        CopyFile(
            Path.Combine(distRoot, "webclient", "index.html"),
            Path.Combine(staticRoot, "webclient", "index.html"));
        CopyFile(
            Path.Combine(distRoot, "index.html"),
            Path.Combine(staticRoot, "atheriz_draw", "index.html"));
        // Stable server-log viewer name for the landing page (hashed names
        // change per build). It lives next to its hashed chunk neighbors
        // so their relative imports keep resolving.
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

        log.WriteLine($"Deployed frontend artifacts to {staticRoot}");
        log.WriteLine($"  webclient: {Path.Combine(staticRoot, "webclient", "index.html")}");
        log.WriteLine($"  draw:     {Path.Combine(staticRoot, "atheriz_draw", "index.html")}");
        log.WriteLine($"  serverlog:{Path.Combine(staticRoot, "assets", "serverlog.js")}");
    }

    // Games keep their own web/templates/index.html copy (seeded at `new`
    // time and winning over the shipped one at runtime), so without this
    // step a redeploy updates the JS bundles but never the landing page
    // itself. Deploy is an explicit operator action, so overwrite.
    public static void SyncLandingTemplate(string engineTemplateFile, string gameWebRoot, TextWriter log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineTemplateFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameWebRoot);
        ArgumentNullException.ThrowIfNull(log);
        var dest = Path.Combine(Path.GetFullPath(gameWebRoot), "templates", "index.html");
        CopyFile(engineTemplateFile, dest);
        log.WriteLine($"  landing:  {dest}");
    }

    // Engine checkout root for resolving webclient/dist and the landing
    // template: ATHERIZ_ROOT wins, else walk up from the game folder (and
    // the install dir) for the server project. Null when unresolvable —
    // the caller reports it, never a bare guess.
    public static string? FindEngineRoot()
    {
        var env = Environment.GetEnvironmentVariable("ATHERIZ_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Atheriz.Server", "Atheriz.Server.csproj")))
                    return dir.FullName;
            }
        }
        return null;
    }

    private static void RemovePath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        else if (File.Exists(path))
            File.Delete(path);
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
}
