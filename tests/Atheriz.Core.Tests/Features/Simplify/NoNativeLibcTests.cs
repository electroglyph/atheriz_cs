namespace Atheriz.Core.Tests.Features.Simplify;

// Native P/Invoke is confined to the guarded detach helper (AGENTS.md):
// no Mono.Unix anywhere, and native imports exist only in
// Cli/DaemonDetach.cs behind OperatingSystem guards with a run-attached
// fallback. Never call the real detach in a test — setsid in the test
// host would detach the test runner itself.
[Collection("Ported")]
public class NoNativeLibcTests
{
    [Fact]
    public void Src_ConfinesNativePInvokeToDetach()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(f); }
            catch { continue; }
            if (text.Contains("Mono.Unix"))
                offenders.Add(Path.GetRelativePath(root, f));
            if ((text.Contains("DllImport(\"") || text.Contains("LibraryImport(\""))
                && !f.EndsWith(Path.Combine("Cli", "DaemonDetach.cs"), StringComparison.Ordinal))
                offenders.Add(Path.GetRelativePath(root, f));
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Detach_IsGuardedWithFallback()
    {
        var root = FindRepoRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Atheriz.Server", "Cli", "DaemonDetach.cs"));
        Assert.Contains("OperatingSystem.IsLinux()", text);
        Assert.Contains("OperatingSystem.IsMacOS()", text);
        Assert.Contains("OperatingSystem.IsWindows()", text);
        Assert.Contains("continuing attached", text);
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
