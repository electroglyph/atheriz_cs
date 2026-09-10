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

    // Shared assembly-dir probe for the engine resolvers below: verified
    // shape-identical except sub-path (wwwroot vs web/templates) and the
    // appBase fallback (wwwroot only — Templates has none), so the helper
    // takes (subPath, fallbackBase?).
    private static string? ResolveEngineDir(string subPath, string? fallbackBase)
    {
        try
        {
            var asmDir = Path.GetDirectoryName(typeof(PidFile).Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                var cand = Path.Combine(asmDir, subPath);
                if (Directory.Exists(cand)) return cand;
                var cand2 = Path.GetFullPath(Path.Combine(asmDir, "..", subPath));
                if (Directory.Exists(cand2)) return cand2;
            }
        }
        catch { }
        if (fallbackBase is not null)
        {
            var fb = Path.Combine(fallbackBase, subPath);
            if (Directory.Exists(fb)) return fb;
        }
        return null;
    }

    private static string? ResolveEngineWwwRoot()
        => ResolveEngineDir("wwwroot", AppContext.BaseDirectory);

    private static string? ResolveEngineTemplates()
        => ResolveEngineDir(Path.Combine("web", "templates"), null);

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
        // No post-check re-probe of the engine dir here: row 5 of the table
        // above already yields it, so a null result means it was absent at
        // probe time (only a concurrent directory-create in between could
        // change that, and the next call resolves it).
        return ResolveCandidates(ResolveTable(contentRoot, appBaseDir, engineTemplates, Path.Combine("web", "templates"), "templates"));
    }
}
