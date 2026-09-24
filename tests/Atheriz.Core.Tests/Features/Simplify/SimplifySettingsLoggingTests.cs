using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Atheriz.Server.Cli;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from LoggerEchoAppendTests.cs
// One echo-and-append tail for both Write branches (per-sink bytes identical);
// the Robust wrappers collapse bodies into LogRobust but keep their names.
[Collection("Ported")]
public class LoggerEchoAppendTests
{
    [Fact]
    public void Write_LoggerBranch_EmitsExactLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_echoapp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var marker = "echo-bytes-" + Guid.NewGuid().ToString("N");
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = dir });
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                AtherizLogger.LogInformation(marker);
                log = cap.Read();
            }
            Assert.Contains($"INFORMATION: atheriz: {marker}", log);
            Assert.Contains(marker, File.ReadAllText(Path.Combine(dir, "server.log")));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void RobustWrappers_Forward_WithNamesKept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_robust_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var marker = "robust-" + Guid.NewGuid().ToString("N");
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = dir });
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                AtherizLogger.LogErrorRobust(marker);
                AtherizLogger.LogInformationRobust(marker);
                AtherizLogger.LogRobust(LogLevel.Warning, marker);
                log = cap.Read();
            }
            Assert.Contains($"ERROR: atheriz: {marker}", log);
            Assert.Contains($"INFORMATION: atheriz: {marker}", log);
            Assert.Contains($"WARNING: atheriz: {marker}", log);
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void EchoAndAppend_DefinedOnce_RobustCollapsed()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static void EchoAndAppend("));
        Assert.Equal(1, SourceScan.Count(src, "public static void LogRobust("));
    }
}

// Merged from LoggerLevelMapTests.cs
// The level map is built once (frozen, case-insensitive); lock takes stay
// split (coalescing around the factory refresh would self-deadlock).
[Collection("Ported")]
public class LoggerLevelMapTests
{
    [Fact]
    public void LevelMap_CaseInsensitive_DebugEnables()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_lvlmap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "DEBUG", SavePath = dir });
            Assert.True(AtherizLogger.GetLogger("t").IsEnabled(LogLevel.Debug));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LevelMap_Unknown_FallsBackToInformation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_lvlmap2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "bogus", SavePath = dir });
            Assert.False(AtherizLogger.GetLogger("t").IsEnabled(LogLevel.Debug));
            Assert.True(AtherizLogger.GetLogger("t").IsEnabled(LogLevel.Information));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LevelMap_BuiltOnce_LocksStaySplit()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.Contains("FrozenDictionary<string, LogLevel> LevelMap", src);
        Assert.Contains("ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)", src);
        Assert.DoesNotContain("var map = new Dictionary<string, LogLevel>", src);
    }
}

// Merged from SettingsCleanupTests.cs
// Settings without the singleton machinery: lock-free Global publish, no
// shared Default, validator as plain conditionals with file-exists-only
// cert validation (crypto load happens once at host startup).
public class SettingsCleanupTests
{
    [Fact]
    public void Global_IsLockFreePublish_WithNullGuard()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Settings", "AtherizSettings.cs");
        Assert.DoesNotContain("_globalLock", src);
        Assert.DoesNotContain("static AtherizSettings Default", src);
        Assert.Contains("Volatile.Read(ref _global)", src);
    }

    [Fact]
    public void Global_RoundTrips_AndRejectsNull()
    {
        var original = AtherizSettings.Global;
        try
        {
            var fresh = new AtherizSettings { ServerName = "Probe" };
            AtherizSettings.Global = fresh;
            Assert.Same(fresh, AtherizSettings.Global);
            AtherizSettings.Global = null!;
            Assert.NotNull(AtherizSettings.Global);
        }
        finally { AtherizSettings.Global = original; }
    }

    [Fact]
    public void Validator_HasNoClosureFramework_AndNoCryptoLoad()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Settings", "AtherizSettingsValidator.cs");
        Assert.DoesNotContain("FailWhen(", src);
        Assert.DoesNotContain("CollectGuard(", src);
        Assert.DoesNotContain("TlsCertLoader", src);
    }

    [Fact]
    public void Validator_CertFileExistsOnly()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_val_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var v = new AtherizSettingsValidator();
            // Present-but-garbage cert passes validation (the host
            // fail-fasts at startup); a missing file still fails here.
            var garbage = Path.Combine(tmp, "garbage.pem");
            File.WriteAllText(garbage, "not a real certificate");
            var present = new AtherizSettings
            {
                SavePath = tmp, SecretPath = tmp,
                SslCertFile = garbage, AllowInsecureTlsFallback = false,
            };
            Assert.True(v.Validate(null, present).Succeeded);
            var missing = new AtherizSettings
            {
                SavePath = tmp, SecretPath = tmp,
                SslCertFile = Path.Combine(tmp, "missing.pem"),
            };
            Assert.Contains("SslCertFile not found", v.Validate(null, missing).FailureMessage ?? "");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}

// Merged from ValidatorCollectorTests.cs
// Table-driven range checks: preformatted messages and stable failure-list
// ordering are preserved by the local collectors.
public class ValidatorCollectorTests
{
    private static AtherizSettings ValidIn(string dir) => new() { SavePath = dir, SecretPath = dir };

    [Fact]
    public void FailureList_PreservesOrderAndMessages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = ValidIn(dir);
            options.MaxCharacters = 0;
            options.ServerName = "";
            options.LogLevel = "bogus";
            var result = new AtherizSettingsValidator().Validate(null, options);
            Assert.True(result.Failed);
            var message = result.FailureMessage ?? "";
            Assert.Contains("MaxCharacters must be >0 and <=100 (was 0).", message);
            Assert.Contains("ServerName must be non-empty.", message);
            Assert.Contains("LogLevel must be one of debug/info/warning/error/critical (was 'bogus').", message);
            Assert.True(message.IndexOf("MaxCharacters", StringComparison.Ordinal)
                < message.IndexOf("ServerName", StringComparison.Ordinal));
            Assert.True(message.IndexOf("ServerName", StringComparison.Ordinal)
                < message.IndexOf("LogLevel", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void InterfaceChecks_FoldedTwins_KeepExactMessages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var validator = new AtherizSettingsValidator();
            var bad = ValidIn(dir);
            bad.TelnetInterface = "bogus";
            Assert.Contains("TelnetInterface unparseable: 'bogus'.",
                validator.Validate(null, bad).FailureMessage ?? "");
            var ipv6Any = ValidIn(dir);
            ipv6Any.WebserverInterface = "::";
            Assert.False(validator.Validate(null, ipv6Any).Failed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void GuardCollector_ReportsSavePathInvalid()
    {
        var validator = new AtherizSettingsValidator();
        var options = new AtherizSettings { SavePath = "rel-save", SecretPath = "rel-secret" };
        var message = validator.Validate(null, options).FailureMessage ?? "";
        Assert.Contains("SavePath invalid:", message);
        Assert.Contains("SecretPath invalid:", message);
    }
}

// Merged from PluginRegistrationHelperTests.cs
// Shared EntityReplacement registration core: both scans validate, skip loudly,
// register, and count through one helper.
public class PluginRegistrationHelperTests
{
    private sealed class RegistrationProbeA : GameObject { }

    private sealed class RegistrationProbeB : GameObject { }

    private static bool TryRegister(PluginLoader loader, Type? baseType, Type? replacement, string source, string detail)
    {
        var method = typeof(PluginLoader).GetMethod("TryRegister", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(method);
        return (bool)method.Invoke(loader, new object?[] { baseType, replacement, source, detail })!;
    }

    [Fact]
    public void TryRegister_Valid_RegistersAndCountsOnce()
    {
        using var loader = new PluginLoader();
        Assert.True(TryRegister(loader, typeof(GameObject), typeof(RegistrationProbeA), "from Probe", ""));
        Assert.True(loader.Replacements.TryGetValue(typeof(GameObject), out var registered));
        Assert.Equal(typeof(RegistrationProbeA), registered);
        Assert.Single(loader.Replacements);
    }

    [Fact]
    public void TryRegister_SameBaseTwice_OverwritesWithoutDoubleCount()
    {
        using var loader = new PluginLoader();
        Assert.True(TryRegister(loader, typeof(GameObject), typeof(RegistrationProbeA), "from Probe", ""));
        Assert.True(TryRegister(loader, typeof(GameObject), typeof(RegistrationProbeB), "assembly", ""));
        Assert.Single(loader.Replacements);
        Assert.Equal(typeof(RegistrationProbeB), loader.Replacements[typeof(GameObject)]);
    }

    [Fact]
    public void TryRegister_Unassignable_SkipsWithoutRegistering()
    {
        using var loader = new PluginLoader();
        Assert.False(TryRegister(loader, typeof(GameObject), typeof(string), "from Probe", ""));
        Assert.Empty(loader.Replacements);
    }
}

// Merged from SetupStagesTests.cs
// World setup takes one options record and runs in named stages —
// directories, salt, registry reset, limbo build, credentials, checkpoint —
// instead of one 290-line method with five positional strings.
public class SetupStagesTests
{
    [Fact]
    public void Setup_TakesOptionsRecord()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "IGameSetup.cs");
        Assert.Contains("public sealed record SetupOptions(", src);
        Assert.Contains("void DoSetup(SetupOptions options);", src);
        Assert.DoesNotContain("DoSetup(string savePath", src);
    }

    [Fact]
    public void DoSetup_RunsNamedStages()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.Contains("PrepareDirectories(options.SavePath, options.SecretPath)", src);
        Assert.Contains("ResetWorldState(absSecret)", src);
        Assert.Contains("BuildLimboWorld(settings)", src);
        Assert.Contains("ResolveCredentials(options)", src);
        Assert.Contains("PersistSeed(world, settings, creds, absSave, absSecret)", src);
        Assert.Contains("SeedAccount(world, settings, creds, absSecret, db)", src);
    }

    [Fact]
    public void DoSetup_HasNoPositionalStringOverloads()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.DoesNotContain("string? username", src);
        Assert.DoesNotContain("TextReader? input", src);
    }
}

// Merged from TlsCertLoaderChainTests.cs
// Single-attempt load: fatal crypto errors rethrow, missing key files fail
// fast, and an unloadable file fails loudly.
public class TlsCertLoaderChainTests
{
    [Fact]
    public void Load_MissingKeyFile_ThrowsFileNotFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cert = Path.Combine(dir, "cert.pem");
            File.WriteAllText(cert, "not a real certificate");
            var missing = Path.Combine(dir, "missing.key");
            Assert.Throws<FileNotFoundException>(() => TlsCertLoader.Load(cert, missing));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Load_GarbageFile_FailsLoudly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var garbage = Path.Combine(dir, "garbage.pem");
            File.WriteAllText(garbage, "not a real certificate");
            Assert.ThrowsAny<Exception>(() => TlsCertLoader.Load(garbage, null));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

// Merged from DotWaitNativeSplitTests.cs
// Quiet bounded waits (WaitForExitAsync + pid polling); no dot progress
// output. Native (libc) P/Invoke is banned repo-wide — see
// NoNativeLibcTests.
[Collection("Ported")]
public class DotWaitNativeSplitTests
{
    [Fact]
    public async Task WaitForPidExit_DeadPid_ReportsExited()
    {
        Assert.True(await ProcessHelper.WaitForPidExitAsync(int.MaxValue));
    }

    [Fact]
    public async Task TerminateAsync_ExitedProcess_SettlesPromptly()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--version");
        using var p = Process.Start(psi);
        Assert.NotNull(p);
        Assert.True(p!.WaitForExit(15000));
        await ProcessHelper.TerminateAsync(p).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(p.HasExited);
    }

    [Fact]
    public void Waits_AreQuiet_NoDotProgress()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        Assert.DoesNotContain("Console.Write(\".\")", src);
    }
}
