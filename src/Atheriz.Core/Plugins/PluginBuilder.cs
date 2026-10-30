using System.Diagnostics;
using System.Text;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Plugins;

// Build-first helpers shared by the in-game reload command and the CLI
// reload handler: game-project discovery, staleness checks, and the dotnet
// build runner. Reload itself never shells a compiler directly.
public static class PluginBuilder
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Directories a game project may live in: the current directory, the
    /// directory holding the save folder, and that directory's parent.
    /// </summary>
    public static List<string> GameSearchDirectories(AtherizSettings settings)
    {
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { searchDirs.Add(Directory.GetCurrentDirectory()); } catch { }
        try
        {
            var sp = settings.SavePath;
            if (!string.IsNullOrWhiteSpace(sp))
            {
                var abs = Path.IsPathRooted(sp) ? sp : Path.Combine(Directory.GetCurrentDirectory(), sp);
                var gameRoot = Path.GetDirectoryName(Path.GetFullPath(abs));
                if (!string.IsNullOrWhiteSpace(gameRoot) && Directory.Exists(gameRoot)) searchDirs.Add(gameRoot);
                // also parent of gameRoot (handles save/ inside mygame)
                try { var parent = Path.GetDirectoryName(gameRoot!); if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) searchDirs.Add(parent); } catch { }
            }
        }
        catch { }
        return [.. searchDirs];
    }

    // Project names that are the engine and its template, never a game.
    internal static bool IsEngineProjectName(string name) =>
        string.Equals(name, "Atheriz.Server", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Atheriz.Core", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Atheriz.GameTemplate", StringComparison.OrdinalIgnoreCase);

    // Path segments that never feed the game assembly: build output and test
    // projects. A change there must not make the built dll look stale.
    internal static bool IsIgnoredPathSegment(string segment) =>
        segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
        segment.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);

    // Directories under a scan root that hold their own project file. Their
    // sources belong to that project, not to the game assembly.
    internal static HashSet<string> NestedProjectDirectories(string scanRoot)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var project in Directory.EnumerateFiles(scanRoot, "*.csproj", SearchOption.AllDirectories))
            {
                var directory = Path.GetDirectoryName(project);
                if (directory is null || string.Equals(directory, scanRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = Path.GetRelativePath(scanRoot, directory);
                if (relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(IsIgnoredPathSegment)) continue;
                directories.Add(directory);
            }
        }
        catch { }
        return directories;
    }

    internal static bool IsNonGameSource(string gameDir, string file, IReadOnlySet<string> projectDirectories)
    {
        try
        {
            var rel = Path.GetRelativePath(gameDir, file);
            if (rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(IsIgnoredPathSegment))
                return true;

            for (var directory = Path.GetDirectoryName(file); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            {
                if (projectDirectories.Contains(directory)) return true;
                if (string.Equals(directory, gameDir, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Game plugin projects in one folder: top-level *.csproj files, excluding
    /// the server project itself and test projects (reload never builds those).
    /// </summary>
    public static IReadOnlyList<string> FindPluginProjects(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory)) return [];
        var found = new List<string>();
        foreach (var csproj in Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(csproj);
            if (name.Equals("Atheriz.Server.csproj", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)) continue;
            found.Add(csproj);
        }
        return found;
    }

    /// <summary>
    /// Every game project discoverable for these settings: top-level csprojs
    /// across the search directories, minus engine/template projects. Matches
    /// the set <c>PluginReloader.DiscoverGameAssembly</c> loads from, so the
    /// build set and the load set never disagree.
    /// </summary>
    public static IReadOnlyList<string> FindGameProjects(AtherizSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var found = new List<string>();
        foreach (var dir in GameSearchDirectories(settings))
        {
            string[] csprojs;
            try { csprojs = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var csproj in csprojs)
            {
                if (IsEngineProjectName(Path.GetFileNameWithoutExtension(csproj))) continue;
                if (!found.Contains(csproj, StringComparer.OrdinalIgnoreCase)) found.Add(csproj);
            }
        }
        return found;
    }

    // Per-generation load copies (`{name}.gen-&lt;guid&gt;.dll`, see
    // PluginLoader) are never plugin candidates themselves: they are staged
    // beside real outputs and would otherwise double-load.
    internal static bool IsGenerationCopy(string path)
    {
        var file = Path.GetFileName(path);
        return file.Contains(".gen-", StringComparison.OrdinalIgnoreCase)
            && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deletes orphaned per-generation load copies (<c>*.gen-*.dll</c>) left
    /// beside game outputs by earlier reloads or crashes. Runs at boot, when
    /// no ALC maps them yet, so it is safe on all platforms.
    /// </summary>
    public static void DeleteStaleGenerationCopies(AtherizSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var csproj in FindGameProjects(settings))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(csproj));
            if (dir is null) continue;
            var name = Path.GetFileNameWithoutExtension(csproj);
            foreach (var config in new[] { "Release", "Debug" })
            {
                var bin = Path.Combine(dir, "bin", config, "net10.0");
                string[] files;
                try { files = Directory.GetFiles(bin, $"{name}.gen-*.dll"); }
                catch { continue; }
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                    try { File.Delete(Path.ChangeExtension(f, ".pdb")); } catch { }
                }
            }
        }
    }
    /// <summary>
    /// Already-built dll for a game project: Release first, Debug as fallback.
    /// Null when neither exists.
    /// </summary>
    public static string? BuiltDllPath(string csprojPath)
    {
        ArgumentNullException.ThrowIfNull(csprojPath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(csprojPath));
        if (dir is null) return null;
        var name = Path.GetFileNameWithoutExtension(csprojPath);
        var release = Path.Combine(dir, "bin", "Release", "net10.0", $"{name}.dll");
        if (File.Exists(release)) return release;
        var debug = Path.Combine(dir, "bin", "Debug", "net10.0", $"{name}.dll");
        if (File.Exists(debug)) return debug;
        return null;
    }

    /// <summary>
    /// True when the built dll is missing or older than the project or any
    /// game source feeding it. Same rule the loader uses to skip stale dlls.
    /// </summary>
    public static bool DllNeedsBuild(string csprojPath, out string reason)
    {
        ArgumentNullException.ThrowIfNull(csprojPath);
        var dll = BuiltDllPath(csprojPath);
        if (dll is null) { reason = "no built dll"; return true; }
        var dir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        try
        {
            var dllTime = File.GetLastWriteTimeUtc(dll);
            if (File.GetLastWriteTimeUtc(csprojPath) > dllTime)
            {
                reason = $"{Path.GetFileName(csprojPath)} is newer than {Path.GetFileName(dll)}";
                return true;
            }
            // Any game *.cs newer (recursive: subdirectory edits must not
            // look "up to date") — but only sources that feed the game dll.
            var projectDirectories = NestedProjectDirectories(dir);
            foreach (var cs in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsNonGameSource(dir, cs, projectDirectories)) continue;
                if (File.GetLastWriteTimeUtc(cs) > dllTime)
                {
                    reason = $"{Path.GetRelativePath(dir, cs)} is newer than {Path.GetFileName(dll)}";
                    return true;
                }
            }
        }
        catch { }
        reason = "";
        return false;
    }

    /// <summary>
    /// Runs dotnet build on one plugin project (Release). Build output is
    /// printed only on failure. Returns false when the SDK is missing, the
    /// build fails, or the timeout elapses.
    /// </summary>
    public static async Task<bool> BuildAsync(string csprojPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csprojPath);
        Console.Error.WriteLine($"Building {Path.GetFileName(csprojPath)}...");
        var output = new StringBuilder();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Release --nologo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        try
        {
            if (!process.Start())
            {
                Console.Error.WriteLine($"Could not start dotnet to build {csprojPath}.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start dotnet to build {csprojPath}: {ex.Message}");
            return false;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Build timed out; stopping the build left: {ex.Message}");
            }
            Console.Error.WriteLine($"Build of {csprojPath} timed out after {BuildTimeout.TotalMinutes:F0} minutes.");
            return false;
        }
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Build of {csprojPath} failed (exit {process.ExitCode}):");
            Console.Error.WriteLine(output.ToString());
            return false;
        }
        Console.Error.WriteLine($"Built {Path.GetFileName(csprojPath)}.");
        return true;
    }
}
