namespace Atheriz.Core.Tests.Features.Simplify;

// Cross-platform rule (AGENTS.md): production code must build and run on
// Windows, macOS, and Linux — no native libc P/Invoke, no Mono.Unix.
// Managed cross-platform APIs only.
[Collection("Ported")]
public class NoNativeLibcTests
{
    [Fact]
    public void Src_HasNoNativeLibcPInvoke()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(f); }
            catch { continue; }
            if (text.Contains("DllImport(\"libc\"") || text.Contains("Mono.Unix"))
                offenders.Add(Path.GetRelativePath(root, f));
        }
        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Atheriz.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (Atheriz.sln) not found above test binaries.");
    }
}
