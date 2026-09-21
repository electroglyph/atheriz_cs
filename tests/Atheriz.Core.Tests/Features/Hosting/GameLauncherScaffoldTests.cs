using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// `new` scaffolds per-game atheriz.sh/atheriz.cmd launchers that forward
// every command to the engine launcher with the game folder as CWD.
[Collection("Ported")]
public class GameLauncherScaffoldTests
{
    private static IDisposable SuperuserEnv(string user, string pass)
    {
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", user);
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", pass);
        return new RestoreEnv(oldUser, oldPass);
    }

    private sealed class RestoreEnv(string? user, string? pass) : IDisposable
    {
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", user);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", pass);
        }
    }

    private static string FreshFolder(string prefix, string name)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, name);
    }

    [Fact]
    public void Scaffold_EmitsPerGameLaunchers()
    {
        // New game folders contain working atheriz.sh/atheriz.cmd wrappers:
        // env override, engine discovery, and full argument passthrough.
        var folder = FreshFolder("atheriz_launch_", "lg1");
        using (SuperuserEnv("lg1admin", "lg1Pass123"))
        {
            try
            {
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "launchergame", overwrite: true));
                var sh = Path.Combine(folder, "atheriz.sh");
                var cmd = Path.Combine(folder, "atheriz.cmd");
                Assert.True(File.Exists(sh));
                Assert.True(File.Exists(cmd));
                var shText = File.ReadAllText(sh);
                Assert.StartsWith("#!/usr/bin/env bash", shText);
                Assert.Contains("ATHERIZ_ROOT", shText);
                Assert.Contains("Atheriz.Server.csproj", shText);
                Assert.Contains("exec \"$ENGINE_ROOT/atheriz.sh\" \"$@\"", shText);
                var cmdText = File.ReadAllText(cmd);
                Assert.Contains("ATHERIZ_ROOT", cmdText);
                Assert.Contains("call \"%ENGINE_ROOT%\\atheriz.cmd\" %*", cmdText);
                // The baked fallback names a real engine checkout (the one
                // that generated the folder), so the wrapper works with no
                // env override and no upward search hit.
                var baked = System.Text.RegularExpressions.Regex.Match(shText, "then ENGINE_ROOT=\"([^\"$]+)\"");
                Assert.True(baked.Success, "no baked engine-root fallback in atheriz.sh");
                var bakedRoot = baked.Groups[1].Value;
                Assert.True(Directory.Exists(bakedRoot), $"baked engine root missing: {bakedRoot}");
                Assert.True(File.Exists(Path.Combine(bakedRoot, "src", "Atheriz.Server", "Atheriz.Server.csproj")));
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch { } }
        }
    }

    [Fact]
    public void Overwrite_RefreshesCustomizedLaunchers()
    {
        // `new --overwrite` rewrites the wrappers like every other template
        // file: a customized launcher is refreshed, never preserved.
        var folder = FreshFolder("atheriz_launchref_", "lg2");
        using (SuperuserEnv("lg2admin", "lg2Pass123"))
        {
            try
            {
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "launchergame", overwrite: true));
                var sh = Path.Combine(folder, "atheriz.sh");
                File.WriteAllText(sh, "# customized by operator\n");
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "launchergame", overwrite: true));
                var refreshed = File.ReadAllText(sh);
                Assert.Contains("exec \"$ENGINE_ROOT/atheriz.sh\" \"$@\"", refreshed);
                Assert.DoesNotContain("customized by operator", refreshed);
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch { } }
        }
    }

    [Fact]
    public void Scaffold_MarksShWrapperExecutable()
    {
        // The shell wrapper must be directly runnable on POSIX.
        if (OperatingSystem.IsWindows()) return;
        var folder = FreshFolder("atheriz_launchex_", "lg3");
        using (SuperuserEnv("lg3admin", "lg3Pass123"))
        {
            try
            {
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "launchergame", overwrite: true));
                var mode = File.GetUnixFileMode(Path.Combine(folder, "atheriz.sh"));
                Assert.True((mode & UnixFileMode.UserExecute) != 0, $"missing owner-execute bit: {mode}");
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch { } }
        }
    }
}
