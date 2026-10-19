using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// server.pid keeps the pid on line 1 and writes the UTC claim time on line 2,
// so a pid the OS handed to an unrelated process later (a recycled pid) is not
// mistaken for the server that wrote the file. Files written before the claim
// time existed (a bare pid on one line) still parse.
[Collection("Ported")]
public class PidClaimIdentityTests
{
    private static object? TryReadClaim(string path)
        => typeof(PidFile).GetMethod("TryReadClaim", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [path]);

    private static int ClaimPid(object claim)
        => (int)claim.GetType().GetProperty("Pid")!.GetValue(claim)!;

    private static DateTime? ClaimWrittenUtc(object claim)
        => (DateTime?)claim.GetType().GetProperty("WrittenUtc")!.GetValue(claim);

    private static bool ClaimTimeCouldBelongToProcess(int pid, DateTime? written)
    {
        var claimType = typeof(PidFile).Assembly.GetType("Atheriz.Server.Infrastructure.PidClaim")!;
        var claim = Activator.CreateInstance(claimType, [pid, written])!;
        return (bool)typeof(PidFile)
            .GetMethod("ClaimTimeCouldBelongToProcess", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [claim])!;
    }

    private static bool ReleaseIfOwner(string path, int pid, DateTime? written)
    {
        var claimType = typeof(PidFile).Assembly.GetType("Atheriz.Server.Infrastructure.PidClaim")!;
        var claim = Activator.CreateInstance(claimType, [pid, written])!;
        var method = typeof(PidFile).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "ReleaseIfOwner"
                && m.GetParameters() is { Length: 2 } parameters
                && parameters[1].ParameterType == claimType);
        return (bool)method.Invoke(null, [path, claim])!;
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_pidclaim_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void TryAcquire_WritesPidThenClaimTime()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = NewDir();
        try
        {
            Assert.True(PidFile.TryAcquire(dir, out var claim, out var reason, FreePort()), reason);
            using (claim)
            {
                Assert.NotNull(claim);
                var lines = File.ReadAllLines(Path.Combine(dir, "server.pid"));
                Assert.Equal(2, lines.Length);
                Assert.Equal(Environment.ProcessId, int.Parse(lines[0], CultureInfo.InvariantCulture));
                Assert.True(DateTime.TryParse(
                    lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written));
                Assert.Equal(DateTimeKind.Utc, written.Kind);
                // The claim records the writer's own start time (the same
                // process reads it back exactly), so it is the host's start,
                // never in the future.
                using var self = Process.GetCurrentProcess();
                var start = self.StartTime.ToUniversalTime();
                Assert.True(written >= start - TimeSpan.FromSeconds(2),
                    $"claim time {written:O} precedes the host start {start:O}");
                Assert.True(written <= DateTime.UtcNow + TimeSpan.FromSeconds(2),
                    $"claim time {written:O} is in the future");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TryReadClaim_LegacyBarePid_HasNoTime()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "server.pid");
            File.WriteAllText(path, "  1234\n");
            var claim = TryReadClaim(path);
            Assert.NotNull(claim);
            Assert.Equal(1234, ClaimPid(claim));
            Assert.Null(ClaimWrittenUtc(claim));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TryReadClaim_ReadsTheClaimTime()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "server.pid");
            File.WriteAllText(path, "4321\n2026-01-02T03:04:05.0000000Z");
            var claim = TryReadClaim(path);
            Assert.NotNull(claim);
            Assert.Equal(4321, ClaimPid(claim));
            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), ClaimWrittenUtc(claim));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TryReadClaim_CorruptClaimTime_IsNotTreatedAsLegacy()
    {
        // Falling back to the pid alone would silently drop the identity
        // check on a file that was meant to carry one.
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "server.pid");
            File.WriteAllText(path, "1234\nnot-a-time");
            Assert.Null(TryReadClaim(path));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ClaimTime_AtOrAfterProcessStart_IsAllowed()
    {
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();
        // The process already existed when the claim was written, so it could
        // be the one that wrote the file.
        Assert.True(ClaimTimeCouldBelongToProcess(self.Id, start));
        // Clock-read skew between writer and reader is tolerated.
        Assert.True(ClaimTimeCouldBelongToProcess(self.Id, start.AddSeconds(1)));
        Assert.True(ClaimTimeCouldBelongToProcess(self.Id, start.AddMinutes(5)));
    }

    [Fact]
    public void ClaimTime_BeforeProcessStart_IsRuledOut()
    {
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();
        // The claimed process started after the claim was written: the OS
        // recycled the number onto something unrelated.
        Assert.False(ClaimTimeCouldBelongToProcess(self.Id, start.AddSeconds(-60)));
    }

    [Fact]
    public void ClaimTime_LegacyClaimOrUnreadableProcess_Passes()
    {
        // No recorded time, or a pid whose start time cannot be read, must
        // not turn into a stale verdict: the shape check alone decides.
        Assert.True(ClaimTimeCouldBelongToProcess(Environment.ProcessId, null));
        Assert.True(ClaimTimeCouldBelongToProcess(int.MaxValue, DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void TryAcquire_ReplacesLegacyStaleClaim()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "server.pid"), "99999");
            Assert.True(PidFile.TryAcquire(dir, out var claim, out var reason, FreePort()), reason);
            using (claim)
            {
                var lines = File.ReadAllLines(Path.Combine(dir, "server.pid"));
                Assert.Equal(Environment.ProcessId, int.Parse(lines[0], CultureInfo.InvariantCulture));
                Assert.Equal(2, lines.Length);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReleaseIfOwner_ClaimTimeMismatch_KeepsTheFile()
    {
        // A successor that reused the pid and rewrote the claim must keep its
        // file: only the exact claim that was verified may be deleted.
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "server.pid");
            var claimTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.WriteAllText(path, "2468\n" + claimTime.ToString("O", CultureInfo.InvariantCulture));
            Assert.False(ReleaseIfOwner(path, 2468, claimTime.AddSeconds(30)));
            Assert.True(File.Exists(path));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReleaseIfOwner_MatchingClaimTime_DeletesTheFile()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "server.pid");
            var claimTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.WriteAllText(path, "2468\n" + claimTime.ToString("O", CultureInfo.InvariantCulture));
            Assert.True(ReleaseIfOwner(path, 2468, claimTime));
            Assert.False(File.Exists(path));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
