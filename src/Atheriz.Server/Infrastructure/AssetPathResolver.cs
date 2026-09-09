namespace Atheriz.Server.Infrastructure;

/// <summary>Resolves wwwroot / templates paths, deduplicating candidate arrays.</summary>
public static class AssetPathResolver
{
    public static string? ResolveCandidates(IEnumerable<string?> candidates)
    {
        foreach (var c in candidates.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal))
        {
            if (Directory.Exists(c!))
                return c;
        }
        return null;
    }

    private static string? ResolveEngineWwwRoot()
    {
        try
        {
            var asmDir = Path.GetDirectoryName(typeof(PidFile).Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                var cand = Path.Combine(asmDir, "wwwroot");
                if (Directory.Exists(cand)) return cand;
                var cand2 = Path.GetFullPath(Path.Combine(asmDir, "..", "wwwroot"));
                if (Directory.Exists(cand2)) return cand2;
            }
        }
        catch { }
        var baseWww = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(baseWww)) return baseWww;
        return null;
    }

    private static string? ResolveEngineTemplates()
    {
        try
        {
            var asmDir = Path.GetDirectoryName(typeof(PidFile).Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                var cand = Path.Combine(asmDir, "web", "templates");
                if (Directory.Exists(cand)) return cand;
                var cand2 = Path.GetFullPath(Path.Combine(asmDir, "..", "web", "templates"));
                if (Directory.Exists(cand2)) return cand2;
            }
        }
        catch { }
        return null;
    }

    // Single ordered resolution table: each row is (base selector, sub-path).
    // Bases resolve lazily per call (CWD can move); rows keep game-before-install order
    // CWD (game) > contentRoot (install) > engine > appBaseDir: the server
    // runs with CWD set to the game folder, so per-game web customizations
    // win over shipped install assets. Dups collapse in ResolveCandidates
    // via Distinct. Sub-path "" means the base itself.
    private static IEnumerable<string?> ResolveTable(string contentRoot, string appBaseDir, string? engineDir, string subA, string subB)
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), subA);
        yield return Path.Combine(Directory.GetCurrentDirectory(), subB);
        yield return Path.Combine(contentRoot, subA);
        yield return Path.Combine(contentRoot, subB);
        if (engineDir is not null) yield return engineDir;
        yield return Path.Combine(appBaseDir, subA);
        yield return Path.Combine(appBaseDir, subB);
    }

    public static string? ResolveWwwRoot(string contentRoot, string appBaseDir)
    {
        var engineWwwroot = ResolveEngineWwwRoot();
        // No trailing bare-"wwwroot" fallback: it duplicated the CWD row in a
        // spelling Distinct cannot collapse (relative vs absolute).
        return ResolveCandidates(ResolveTable(contentRoot, appBaseDir, engineWwwroot, "wwwroot", Path.Combine("web", "static")));
    }

    public static string? ResolveTemplates(string contentRoot, string appBaseDir)
    {
        var engineTemplates = ResolveEngineTemplates();
        var result = ResolveCandidates(ResolveTable(contentRoot, appBaseDir, engineTemplates, Path.Combine("web", "templates"), "templates"));
        if (result is null && engineTemplates is not null && Directory.Exists(engineTemplates))
            return engineTemplates;
        return result;
    }
}
