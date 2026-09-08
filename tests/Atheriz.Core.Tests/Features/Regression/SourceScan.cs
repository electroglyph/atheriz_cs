using System.Text.RegularExpressions;

namespace Atheriz.Core.Tests.Features.Regression;

// Shared source-scan helpers for regression tests.
//
// Behavioral tests are preferred where deterministic; source scans pin
// races, lock-order, perf and style findings that cannot fail deterministically
// at runtime. Reflection is explicitly permitted in tests/.
internal static class SourceScan
{
    public static string RepoRoot()
    {
        var candidates = new[]
        {
            "/home/anon/atheriz-cs",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        };
        foreach (var c in candidates)
            if (Directory.Exists(Path.Combine(c, "src"))) return c;
        var cwd = Directory.GetCurrentDirectory();
        var d = new DirectoryInfo(cwd);
        while (d != null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src"))) return d.FullName;
            d = d.Parent;
        }
        throw new InvalidOperationException("repo root with src/ not found");
    }

    public static string Read(params string[] parts)
    {
        var p = parts.Length == 1 && Path.IsPathRooted(parts[0])
            ? parts[0]
            : Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(p);
    }

    // Text between a start marker line and the next line matching endPattern
    // (defaults to the next member declaration at 4-space indent).
    public static string Region(string source, string startMarker, string endPattern = @"\n    (public|private|protected|internal|static|~|\})")
    {
        int s = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(s >= 0, $"start marker not found: {startMarker}");
        var m = Regex.Match(source.Substring(s + startMarker.Length), endPattern);
        return m.Success ? source.Substring(s, startMarker.Length + m.Index) : source.Substring(s);
    }

    public static int Count(string source, string needle)
    {
        int n = 0, i = 0;
        while ((i = source.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
