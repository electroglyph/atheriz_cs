using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// `new` scaffolds per-game build.sh/build.cmd wrappers: Release plugin
// build + webclient redeploy into the game, with a baked relative engine
// path. --no-web is plugin-only, --web is web-only, --reload hot-loads a
// running server; no flags runs both build steps.
[Collection("Ported")]
public class GameBuildScaffoldTests
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
    public void Scaffold_EmitsPerGameBuildScripts()
    {
        // New game folders contain working build.sh/build.cmd wrappers:
        // Release plugin build, web redeploy, mode flags, and a baked
        // relative engine path that resolves to a real engine checkout.
        var folder = FreshFolder("atheriz_build_", "bg1");
        using (SuperuserEnv("bg1admin", "bg1Pass123"))
        {
            try
            {
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "buildgame", overwrite: true));
                var sh = Path.Combine(folder, "build.sh");
                var cmd = Path.Combine(folder, "build.cmd");
                Assert.True(File.Exists(sh));
                Assert.True(File.Exists(cmd));
                var shText = File.ReadAllText(sh);
                Assert.StartsWith("#!/usr/bin/env bash", shText);
                Assert.Contains("dotnet build", shText);
                Assert.Contains("-c Release", shText);
                Assert.Contains("buildgame.csproj", shText);
                Assert.Contains("deploy.py", shText);
                Assert.Contains("--web-root", shText);
                Assert.Contains("--no-web", shText);
                Assert.Contains("--reload", shText);
                Assert.Contains("ATHERIZ_ROOT", shText);
                Assert.Contains("atheriz.sh\" reload", shText);
                // The baked fallback is relative and resolves from the game
                // folder to the engine checkout that generated it.
                var baked = System.Text.RegularExpressions.Regex.Match(shText, "ENGINE_ROOT=\"\\$GAME_DIR/([^\"]*)\"");
                Assert.True(baked.Success, "no baked relative engine path in build.sh");
                var rel = baked.Groups[1].Value;
                Assert.False(Path.IsPathRooted(rel), "baked engine path must be relative");
                var resolved = Path.GetFullPath(Path.Combine(folder, rel));
                Assert.True(File.Exists(Path.Combine(resolved, "src", "Atheriz.Server", "Atheriz.Server.csproj")),
                    $"baked relative path resolves outside an engine checkout: {resolved}");
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch { } }
        }
    }

    [Fact]
    public void Scaffold_EmitsPerGameBuildCmd()
    {
        // The Windows twin carries the same steps: Release plugin build,
        // web redeploy, mode flags, and a reload forward to atheriz.cmd.
        var folder = FreshFolder("atheriz_buildcmd_", "bg2");
        using (SuperuserEnv("bg2admin", "bg2Pass123"))
        {
            try
            {
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "buildgame", overwrite: true));
                var cmdText = File.ReadAllText(Path.Combine(folder, "build.cmd"));
                Assert.Contains("dotnet build", cmdText);
                Assert.Contains("-c Release", cmdText);
                Assert.Contains("buildgame.csproj", cmdText);
                Assert.Contains("deploy.py", cmdText);
                Assert.Contains("--web-root", cmdText);
                Assert.Contains("--no-web", cmdText);
                Assert.Contains("--reload", cmdText);
                Assert.Contains("ATHERIZ_ROOT", cmdText);
                Assert.Contains("atheriz.cmd\" reload", cmdText);
                // Raw-string templates are literal: a doubled backslash
                // would leak into the batch file (regression: BuildCmd once
                // emitted \\ throughout while single-\ asserts still passed
                // as substrings).
                Assert.DoesNotContain("\\\\", cmdText);
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch { } }
        }
    }

    [Fact]
    public void Scaffold_MarksBuildShExecutable()
    {
        // The shell wrapper must be directly runnable on POSIX.
        if (OperatingSystem.IsWindows()) return;
        var folder = FreshFolder("atheriz_buildex_", "bg3");
        using (SuperuserEnv("bg3admin", "bg3Pass123"))
        {
            try
            {
                Assert.True(GameTemplateGenerator.CreateGameFolder(folder, "buildgame", overwrite: true));
                var mode = File.GetUnixFileMode(Path.Combine(folder, "build.sh"));
                Assert.True((mode & UnixFileMode.UserExecute) != 0, $"missing owner-execute bit: {mode}");
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch { } }
        }
    }
}
