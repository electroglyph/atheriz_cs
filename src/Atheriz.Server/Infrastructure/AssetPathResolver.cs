namespace Atheriz.Server.Infrastructure;

/// <summary>Resolves wwwroot / templates paths, deduplicating candidate arrays. Port of P1.8.</summary>
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

    public static string? ResolveWwwRoot(string contentRoot, string appBaseDir)
    {
        var engineWwwroot = ResolveEngineWwwRoot();
        var candidates = new[]
        {
            Path.Combine(contentRoot, "wwwroot"),
            Path.Combine(contentRoot, "web", "static"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "web", "static"),
            engineWwwroot ?? "wwwroot",
            Path.Combine(appBaseDir, "wwwroot"),
            Path.Combine(appBaseDir, "web", "static"),
            "wwwroot",
            Path.Combine(appBaseDir, "wwwroot"),
        };
        return ResolveCandidates(candidates);
    }

    public static string? ResolveTemplates(string contentRoot, string appBaseDir)
    {
        var engineTemplates = ResolveEngineTemplates();
        var candidates = new[]
        {
            Path.Combine(contentRoot, "web", "templates"),
            Path.Combine(contentRoot, "templates"),
            Path.Combine(Directory.GetCurrentDirectory(), "web", "templates"),
            Path.Combine(Directory.GetCurrentDirectory(), "templates"),
            engineTemplates ?? string.Empty,
            Path.Combine(appBaseDir, "web", "templates"),
            Path.Combine(appBaseDir, "web", "templates"),
        };
        var result = ResolveCandidates(candidates);
        if (result == null && engineTemplates != null && Directory.Exists(engineTemplates))
            return engineTemplates;
        return result;
    }
}
