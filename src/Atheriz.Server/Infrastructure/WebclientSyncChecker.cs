using System.Security.Cryptography;

namespace Atheriz.Server.Infrastructure;

public static class WebclientSyncChecker
{
    // Mirrors atheriz/atheriz.py:214 _file_hash + 205 _collect_files
    // excludeDirNames is opt-in (default none = verbatim Python rglob parity):
    // skips any file under a directory with a matching name (e.g. ".git").
    private static Dictionary<string, string> CollectFiles(string root, IReadOnlyCollection<string>? excludeDirNames = null)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return dict;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f);
            // Normalize to forward slashes like Python Path.relative_to
            rel = rel.Replace(Path.DirectorySeparatorChar, '/');
            if (excludeDirNames is not null && excludeDirNames.Count > 0)
            {
                var dir = Path.GetDirectoryName(rel)?.Replace(Path.DirectorySeparatorChar, '/');
                if (!string.IsNullOrEmpty(dir) && dir.Split('/').Any(seg => excludeDirNames.Contains(seg))) continue;
            }
            dict[rel] = f;
        }
        return dict;
    }

    private static string FileHash(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Mirrors atheriz/atheriz.py:224 check_webclient_sync(game, engine_web=None)
    // settings is optional (defaults to Global = old behavior); excludeDirNames
    // is opt-in (default none = verbatim parity, see CollectFiles).
    public static Dictionary<string, Dictionary<string, List<string>>>? CheckSync(string gameCwd, string? engineWebOverride = null)
    {
        // Derive contentRoot from game parent if not supplied – for backward compat
        string contentRoot = Path.GetDirectoryName(Path.GetFullPath(gameCwd)) ?? Path.GetFullPath(gameCwd);
        return CheckSync(gameCwd, contentRoot, engineWebOverride, null, null);
    }
    public static Dictionary<string, Dictionary<string, List<string>>>? CheckSync(string gameCwd, string contentRoot, string? engineWebOverride = null, Atheriz.Core.Settings.AtherizSettings? settings = null, IReadOnlyCollection<string>? excludeDirNames = null)
    {
        // Respect WEBCLIENT_SYNC_CHECK — mirrors `if not getattr(settings, "WEBCLIENT_SYNC_CHECK", True): return None`
        // We read via AtherizSettings.Default default true, but caller should gate; here we just check env var fallback.
        // For faithful, we check both env and settings.Global.
        try
        {
            var gs = settings ?? Atheriz.Core.Settings.AtherizSettings.Global;
            if (!gs.WebclientSyncCheck) return null;
        }
        catch { }

        var gameWeb = Path.Combine(gameCwd, "web");
        if (!Directory.Exists(gameWeb)) return null;

        string? engineWeb = engineWebOverride ?? ResolveEngineWeb(contentRoot);
        // The C# layout ships the engine webclient under wwwroot/webclient,
        // not web/: ResolveEngineWeb then names a nonexistent web/ dir. Report
        // clean only when no wwwroot baseline exists either — otherwise compare
        // the static area against it below instead of returning clean.
        bool hasWebBaseline = engineWeb is not null && Directory.Exists(engineWeb);
        if (!hasWebBaseline && !WwwWebclientExists(contentRoot)) return null;
        engineWeb ??= contentRoot;

        var summary = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var area in new[] { "templates", "static" })
        {
            // Without a web/ baseline there is nothing to judge game templates
            // against (the wwwroot layout ships no templates tree) — compare
            // static only instead of reporting game files as extra.
            if (!hasWebBaseline && area == "templates")
            {
                summary[area] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
                {
                    ["missing"] = new(),
                    ["different"] = new(),
                    ["extra"] = new(),
                };
                continue;
            }
            // Try engineWeb/area/webclient; fallback for C# static at wwwroot/webclient
            var engineAreaRoot = Path.Combine(engineWeb, area, "webclient");
            var gameAreaRoot = Path.Combine(gameWeb, area, "webclient");
            var engineFiles = CollectFiles(engineAreaRoot, excludeDirNames);
            var gameFiles = CollectFiles(gameAreaRoot, excludeDirNames);

            // C# fallback: engine static may live at wwwroot/webclient instead of web/static/webclient
            if (area == "static" && engineFiles.Count == 0)
            {
                var altEngine = Path.Combine(contentRoot, "wwwroot", "webclient");
                if (Directory.Exists(altEngine))
                    engineFiles = CollectFiles(altEngine, excludeDirNames);
                else
                {
                    var baseAlt = Path.Combine(AppContext.BaseDirectory, "wwwroot", "webclient");
                    if (Directory.Exists(baseAlt))
                        engineFiles = CollectFiles(baseAlt, excludeDirNames);
                }
            }

            // Python special case: only compare index.html for static
            if (area == "static" && engineFiles.ContainsKey("index.html"))
            {
                var eIdx = engineFiles["index.html"];
                engineFiles = new Dictionary<string, string>(StringComparer.Ordinal) { ["index.html"] = eIdx };
                if (gameFiles.ContainsKey("index.html"))
                    gameFiles = new Dictionary<string, string>(StringComparer.Ordinal) { ["index.html"] = gameFiles["index.html"] };
                else
                    gameFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var common = new HashSet<string>(engineFiles.Keys, StringComparer.Ordinal);
            common.IntersectWith(gameFiles.Keys);

            var missing = engineFiles.Keys.Except(gameFiles.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var different = common.Where(r => FileHash(engineFiles[r]) != FileHash(gameFiles[r])).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var extra = gameFiles.Keys.Except(engineFiles.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            summary[area] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["missing"] = missing,
                ["different"] = different,
                ["extra"] = extra,
            };
        }

        // Draw app entry: compare only index.html (it embeds the hashed asset
        // names, so any rebuild changes it). Skipped entirely when the engine
        // ships no draw entry — nothing to judge the game copy against.
        var engineDrawIndex = FindEngineDrawIndex(engineWeb, contentRoot);
        if (engineDrawIndex is not null)
        {
            var gameDrawIndex = Path.Combine(gameWeb, "static", "atheriz_draw", "index.html");
            var drawMissing = new List<string>();
            var drawDifferent = new List<string>();
            if (!File.Exists(gameDrawIndex))
                drawMissing.Add("index.html");
            else if (FileHash(engineDrawIndex) != FileHash(gameDrawIndex))
                drawDifferent.Add("index.html");
            summary["atheriz_draw"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["missing"] = drawMissing,
                ["different"] = drawDifferent,
                ["extra"] = new(),
            };
        }

        if (summary.Values.All(v => v["missing"].Count == 0 && v["different"].Count == 0 && v["extra"].Count == 0))
            return null;
        return summary;
    }

    private static string? FindEngineDrawIndex(string engineWeb, string contentRoot)
    {
        // Shipped/project copies first: in the DLL-run layout engineWeb
        // resolves to the game's own web dir (self-compare, always clean),
        // so the layout-derived candidate goes last. The up-three-levels
        // probe mirrors the deploy.py heuristic in FormatWarning (bin /
        // Debug|Release / net10.0 -> project dir).
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "atheriz_draw", "index.html")),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "atheriz_draw", "index.html"),
            Path.Combine(contentRoot, "wwwroot", "atheriz_draw", "index.html"),
            Path.Combine(engineWeb, "static", "atheriz_draw", "index.html"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool WwwWebclientExists(string contentRoot)
    {
        // Same two probes as the static-area fallback in CheckSync: the engine
        // webclient may live at wwwroot/webclient instead of web/.
        return Directory.Exists(Path.Combine(contentRoot, "wwwroot", "webclient"))
            || Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "webclient"));
    }

    private static string? ResolveEngineWeb(string contentRoot)
    {
        var candidates = new[]
        {
            Path.Combine(contentRoot, "web"),
            Path.Combine(AppContext.BaseDirectory, "web"),
            Path.Combine(AppContext.BaseDirectory, "..", "web"),
            Path.Combine(Directory.GetCurrentDirectory(), "web"),
        };
        var resolved = AssetPathResolver.ResolveCandidates(candidates.Select(Path.GetFullPath));
        if (resolved is not null) return resolved;
        if (Directory.Exists(Path.Combine(contentRoot, "wwwroot")))
            return Path.Combine(contentRoot, "web");
        return null;
    }

    // Mirrors atheriz/atheriz.py:261 format_webclient_sync_warning(summary, game, os_name="posix", engine_web=None)
    public static string FormatWarning(Dictionary<string, Dictionary<string, List<string>>> summary, string gameCwd, string? osName = null, string? engineWebOverride = null)
    {
        string contentRoot = Path.GetDirectoryName(Path.GetFullPath(gameCwd)) ?? Path.GetFullPath(gameCwd);
        return FormatWarning(summary, gameCwd, contentRoot, engineWebOverride, osName);
    }
    public static string FormatWarning(Dictionary<string, Dictionary<string, List<string>>> summary, string gameCwd, string contentRoot, string? engineWebOverride = null, string? osName = null)
    {
        osName ??= OperatingSystem.IsWindows() ? "nt" : "posix";
        string? engineWeb = engineWebOverride ?? ResolveEngineWeb(contentRoot) ?? Path.Combine(contentRoot, "web");
        var lines = new List<string> { "WARNING: Game webclient is out of sync with the server's!" };
        foreach (var area in new[] { "templates", "static", "atheriz_draw" })
        {
            if (!summary.TryGetValue(area, out var d)) continue;
            var missing = d.TryGetValue("missing", out var m) ? m : [];
            var different = d.TryGetValue("different", out var diff) ? diff : [];
            var extra = d.TryGetValue("extra", out var e) ? e : [];
            if (missing.Count == 0 && different.Count == 0 && extra.Count == 0) continue;
            List<string> parts = [];
            if (different.Count > 0) parts.Add($"{different.Count} modified");
            if (missing.Count > 0) parts.Add($"{missing.Count} missing");
            if (extra.Count > 0) parts.Add($"{extra.Count} extra");
            var displayPath = area == "atheriz_draw" ? "web/static/atheriz_draw" : $"web/{area}/webclient";
            lines.Add($"  {displayPath}: {string.Join(", ", parts)}");
            var names = different.Concat(missing).Concat(extra).Take(3).ToList();
            lines.Add("    e.g. " + string.Join(", ", names));
        }
        var compiledWebclient = File.Exists(Path.Combine(engineWeb, "static", "webclient", "index.html"))
            || File.Exists(Path.Combine(contentRoot, "wwwroot", "webclient", "index.html"))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "webclient", "index.html"))
            || FindEngineDrawIndex(engineWeb, contentRoot) is not null;
        if (compiledWebclient)
        {
            // Try locate deploy.py for message — mirrors Python's deploy_py = Path(__file__).resolve().parent.parent / "webclient" / "deploy.py"
            var possibleDeploy = Path.GetFullPath(Path.Combine(engineWeb, "..", "..", "webclient", "deploy.py"));
            // In C# repo, webclient is at /home/anon/atheriz/webclient, not under Server
            // We hint generic command
            lines.Add("  Deploy the compiled webclient into the game:");
            // Try find deploy.py relative to engineWeb
            var deployPy = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "webclient", "deploy.py"));
            if (File.Exists(deployPy))
                lines.Add($"    python \"{deployPy}\" game --web-root \"{Path.Combine(gameCwd, "web")}\"");
            else if (File.Exists(possibleDeploy))
                lines.Add($"    python \"{possibleDeploy}\" game --web-root \"{Path.Combine(gameCwd, "web")}\"");
            else
            {
                lines.Add("    From the atheriz source checkout:");
                lines.Add($"    python webclient/deploy.py game --web-root \"{Path.Combine(gameCwd, "web")}\"");
            }
            return string.Join("\n", lines);
        }
        lines.Add("  Copy the server's webclient over the game's:");
        string rel;
        try
        {
            rel = Path.GetRelativePath(Path.GetFullPath(gameCwd), Path.GetFullPath(engineWeb));
            if (osName == "nt") rel = rel.Replace("/", "\\");
            else rel = rel.Replace("\\", "/");
        }
        catch
        {
            rel = engineWeb;
            if (osName == "nt") rel = rel.Replace("/", "\\");
            else rel = rel.Replace("\\", "/");
        }
        if (osName == "nt")
        {
            lines.Add($""""    xcopy "{rel}\templates\webclient" "web\templates\webclient\" /E /Y /I"""");
            lines.Add($""""    xcopy "{rel}\static\webclient" "web\static\webclient\" /E /Y /I"""");
        }
        else
        {
            lines.Add($"    cp -r \"{rel}/templates/webclient\" \"web/templates/\"");
            lines.Add($"    cp -r \"{rel}/static/webclient\" \"web/static/\"");
        }
        return string.Join("\n", lines);
    }
}
