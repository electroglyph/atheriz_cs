using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Regression pins for the utils batch: sphere pagination, game-folder
// cache, save-dir writability probe, Levenshtein cap, TLS chain bundle,
// LockScope dispose guard, and the new validator checks.
public sealed class UtilsBatchRegressionTests
{
    private static AtherizSettings AbsolutePaths(AtherizSettings s)
    {
        s.SavePath = Path.Combine(Path.GetTempPath(), "atheriz_ureg_save");
        s.SecretPath = Path.Combine(Path.GetTempPath(), "atheriz_ureg_secret");
        return s;
    }

    [Fact]
    public void Sphere_MaxResults_CapsEnumeration()
    {
        var full = GameUtils.GetPointsInSphere((0, 0, 0), 5);
        Assert.True(full.Count > 100);
        var capped = GameUtils.GetPointsInSphere((0, 0, 0), 5, maxResults: 10);
        Assert.Equal(10, capped.Count);
        Assert.Subset(full.ToHashSet(), capped.ToHashSet());
    }

    [Fact]
    public void IsInGameFolder_RepeatedCalls_Agree()
    {
        // Exercises the per-CWD cache path; result must equal a fresh probe.
        Assert.Equal(GameUtils.IsInGameFolder(), GameUtils.IsInGameFolder());
        Assert.Equal(GameUtils.IsInGameFolder("posix"), GameUtils.IsInGameFolder("posix"));
    }

    [Fact]
    public void EnsureSaveDirectory_ReadOnlyDir_ThrowsLoud()
    {
        if (OperatingSystem.IsWindows()) return; // chmod semantics are POSIX-only
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_ureg_ro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
#pragma warning disable CA1416 // POSIX-only test path, guarded above
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
            // Owner-chmodable dir is self-healed to 0700 and passes (TryChmod0700 before probe).
            PathGuards.EnsureSaveDirectory(dir);
            var mode = File.GetUnixFileMode(dir);
            Assert.True((mode & UnixFileMode.UserWrite) != 0, "read-only save dir should be self-healed writable");
            // Unfixable dir (save leaf under a read-only parent) fails LOUD, not silent.
            string nested = Path.Combine(dir, "sub", "save");
            string sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
#pragma warning disable CA1416
            File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
#pragma warning restore CA1416
            Assert.Throws<UnauthorizedAccessException>(() => PathGuards.EnsureSaveDirectory(nested));
        }
        finally
        {
#pragma warning disable CA1416
            try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
#pragma warning restore CA1416
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureSaveDirectory_WritableDir_Passes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_ureg_rw_" + Guid.NewGuid().ToString("N"));
        try
        {
            PathGuards.EnsureSaveDirectory(dir);
            Assert.True(Directory.Exists(dir));
            Assert.False(File.Exists(Path.Combine(dir, ".atheriz_write_probe")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Levenshtein_OversizedInput_ReturnsCappedBoundWithoutTable()
    {
        var a = new string('x', 2000);
        var b = new string('y', 2000);
        var sw = Stopwatch.StartNew();
        int d = StringDistance.Levenshtein(a, b);
        sw.Stop();
        Assert.Equal(2000, d);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public void TlsCertLoader_CombinedPemWithIntermediate_LoadsWithKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_ureg_tls_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            RunOpenssl(dir, "req -x509 -newkey rsa:2048 -keyout leaf.key -out leaf.crt -days 2 -nodes -subj /CN=leaf");
            RunOpenssl(dir, "req -x509 -newkey rsa:2048 -keyout ca.key -out ca.crt -days 2 -nodes -subj /CN=ca");
            // leaf + intermediate + key order: the old splitter fed the
            // intermediate block to CreateFromPem as the "key" and threw.
            var combined = Path.Combine(dir, "combined.pem");
            File.WriteAllText(combined,
                File.ReadAllText(Path.Combine(dir, "leaf.crt"))
                + File.ReadAllText(Path.Combine(dir, "ca.crt"))
                + File.ReadAllText(Path.Combine(dir, "leaf.key")));
            using var cert = TlsCertLoader.Load(combined, null);
            Assert.True(cert.HasPrivateKey);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LockScope_DoubleDispose_DoesNotThrow()
    {
        var t = typeof(GameUtils).Assembly.GetType("Atheriz.Core.LockScope");
        Assert.NotNull(t);
        using var rw = new ReaderWriterLockSlim();
        rw.EnterWriteLock();
        var scope = Activator.CreateInstance(t!, rw, true);
        Assert.NotNull(scope);
        var dispose = t!.GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(dispose);
        dispose!.Invoke(scope, null);
        Assert.True(rw.TryEnterWriteLock(0));
        rw.ExitWriteLock();
        // Second dispose must be a guarded no-op, not SynchronizationLockException.
        dispose.Invoke(scope, null);
    }

    [Fact]
    public void SettingsValidator_UnparseableInterface_Fails()
    {
        var v = new AtherizSettingsValidator();
        var r = v.Validate(null, AbsolutePaths(new AtherizSettings { WebserverInterface = "not-an-ip" }));
        Assert.True(r.Failed);
        Assert.Contains("WebserverInterface", r.FailureMessage);
    }

    [Fact]
    public void SettingsValidator_PortCollision_Fails()
    {
        var v = new AtherizSettingsValidator();
        var r = v.Validate(null, AbsolutePaths(new AtherizSettings { WebserverPort = 4444 }));
        Assert.True(r.Failed);
        Assert.Contains("TelnetPort", r.FailureMessage);
    }

    [Fact]
    public void SettingsValidator_EmptyServerName_Fails()
    {
        var v = new AtherizSettingsValidator();
        var r = v.Validate(null, AbsolutePaths(new AtherizSettings { ServerName = "  " }));
        Assert.True(r.Failed);
        Assert.Contains("ServerName", r.FailureMessage);
    }

    [Fact]
    public void SettingsValidator_ZeroTimeFields_Fails()
    {
        var v = new AtherizSettingsValidator();
        var r = v.Validate(null, AbsolutePaths(new AtherizSettings { HoursPerDay = 0 }));
        Assert.True(r.Failed);
        Assert.Contains("HoursPerDay", r.FailureMessage);
    }

    private static void RunOpenssl(string dir, string args)
    {
        using var p = Process.Start(new ProcessStartInfo("openssl", args)
        {
            WorkingDirectory = dir,
            RedirectStandardError = true,
        });
        Assert.NotNull(p);
        Assert.True(p!.WaitForExit(30000), "openssl timed out: " + args);
        Assert.Equal(0, p.ExitCode);
    }
}
