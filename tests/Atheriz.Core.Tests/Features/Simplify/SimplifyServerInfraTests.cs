using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Reflection;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from HostTlsFallbackAdminJsonTests.cs
// Shared TLS-flipped retry + per-caller admin JSON defaults: create ("error"),
// reload ("ok"), shutdown (""). Unifying the default would change behavior.
[Collection("Ported")]
public class HostTlsFallbackAdminJsonTests
{
    private static Type AdminResponseType()
        => typeof(ShutdownClient).Assembly.GetType("Atheriz.Server.Cli.AdminResponse")!;

    private static object NewResponse(string body)
    {
        var t = AdminResponseType();
        Assert.NotNull(t);
        var ctor = t!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return ctor.Invoke([200, body]);
    }

    private static string? GetStatus(object resp, string defaultValue)
    {
        var m = AdminResponseType().GetMethod("GetStatus", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string?)m.Invoke(resp, [defaultValue]);
    }

    private static string? GetMessage(object resp)
    {
        var m = AdminResponseType().GetMethod("GetMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string?)m.Invoke(resp, null);
    }

    [Fact]
    public void AdminJson_StatusAndMessage_Read()
    {
        var resp = NewResponse("""{"status":"ok","message":"hi"}""");
        Assert.Equal("ok", GetStatus(resp, "error"));
        Assert.Equal("hi", GetMessage(resp));
    }

    [Fact]
    public void AdminJson_MissingStatus_UsesCallerDefault()
    {
        var resp = NewResponse("""{"message":"m"}""");
        Assert.Equal("error", GetStatus(resp, "error"));
        Assert.Equal("ok", GetStatus(resp, "ok"));
        Assert.Equal("", GetStatus(resp, ""));
    }

    [Fact]
    public void AdminJson_MissingMessage_FallsBackToBody()
    {
        var body = """{"status":"ok"}""";
        Assert.Equal(body, GetMessage(NewResponse(body)));
    }

    [Fact]
    public void Shutdown_UsesTlsFallbackRetry_WithEmptyDefault()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        var region = SourceScan.Region(src, "internal static async Task<ShutdownRequestResult> TryRequestShutdownAsync(");
        Assert.Contains("PostAdminWithTlsFallbackAsync", region);
        Assert.Contains("GetStatus(\"\")", region);
    }

    [Fact]
    public void Create_And_Reload_KeepTheirOwnDefaults()
    {
        var create = SourceScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("PostAdminWithTlsFallbackAsync", create);
        Assert.Contains("GetStatus(\"error\")", create);
        var reload = SourceScan.Read("src", "Atheriz.Server", "Cli", "ReloadHandler.cs");
        Assert.Contains("PostAdminWithTlsFallbackAsync", reload);
        Assert.Contains("GetStatus(\"ok\")", reload);
    }
}

// Merged from KestrelFallbackGuardTests.cs
// One fail-closed core for both cert-failure paths: a configured cert that
// cannot serve must never silently serve plaintext holding the admin token.
// Paths keep their own messages and warning prints.
[Collection("Ported")]
public class KestrelFallbackGuardTests
{
    [Fact]
    public void FallbackDecision_LivesInOneHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "KestrelConfig.cs");
        Assert.Equal(1, SourceScan.Count(src, "void ThrowIfNoFallbackOrWarn("));
        Assert.Equal(2, SourceScan.Count(src, "ThrowIfNoFallbackOrWarn(") - 1);
        Assert.Equal(1, SourceScan.Count(src, "void ConfigureEndpoint("));
    }

    [Fact]
    public void BothFailurePaths_StayFailClosed_WithOwnMessages()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "KestrelConfig.cs");
        Assert.Contains("SSL cert configured but not found", src);
        Assert.Contains("SSL cert configured but unloadable", src);
        Assert.Contains("refusing insecure fallback", src);
        Assert.Contains("WARNING: SSL cert file not found", src);
        Assert.Contains("SSL load failed for", src);
    }
}

// Merged from EngineDirResolverTests.cs
// One assembly-dir probe for the engine resolvers (sub-path + optional
// fallback differ per caller). The ResolveTemplates post-check re-probe is
// gone: the table already yields the engine dir, so a null result means it
// was absent at probe time.
[Collection("Ported")]
public class EngineDirResolverTests
{
    [Fact]
    public void EngineProbe_DefinedOnce_DelegatedTwice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static string? ResolveEngineDir("));
        Assert.Contains("ResolveEngineDir(\"wwwroot\"", src);
        Assert.Contains("ResolveEngineDir(Path.Combine(\"web\", \"templates\")", src);
    }

    [Fact]
    public void ResolveTemplates_HasNoDeadPostCheck()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        var region = SourceScan.Region(src, "public static string? ResolveTemplates(");
        Assert.DoesNotContain("Directory.Exists(engineTemplates)", region);
        Assert.Contains("ResolveCandidates(", region);
    }

    [Fact]
    public void ResolveCandidates_FirstExistingWins_SkipsBlanks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_assetdir_" + Guid.NewGuid().ToString("N"));
        var want = Path.Combine(dir, "b");
        Directory.CreateDirectory(want);
        try
        {
            Assert.Equal(want, AssetPathResolver.ResolveCandidates([null, "", Path.Combine(dir, "missing"), want]));
            Assert.Null(AssetPathResolver.ResolveCandidates([null, "", Path.Combine(dir, "missing")]));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

// Merged from PidFileCompatOverloadTests.cs
// LocateServerPidFile ignores its port argument (always {savePath}/server.pid):
// the two-arg shape stays as a compatibility forwarder. Release funnels
// through the owner-verified delete.
[Collection("Ported")]
public class PidFileCompatOverloadTests
{
    [Fact]
    public void LocateServerPidFile_PortOverload_Forwards()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_pidcompat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(PidFile.LocateServerPidFile(dir), PidFile.LocateServerPidFile(1234, dir));
            Assert.Equal(Path.Combine(dir, "server.pid"), PidFile.LocateServerPidFile(9999, dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Release_UsesOwnerVerifiedDelete()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        var region = SourceScan.Region(src, "public void Release()");
        Assert.Contains("ReleaseIfOwner(PidPath, _ownPid)", region);
        Assert.Equal(1, SourceScan.Count(src, "public static string LocateServerPidFile(string savePath)"));
        Assert.Equal(1, SourceScan.Count(src, "public static string LocateServerPidFile(int port, string savePath)"));
    }
}

// Merged from PidFileReadFunnelTests.cs
// One pid-file read (ReadAllText + Trim + TryParse; failure = dead claim) for
// the CLI liveness probes, plus the live-claim wrapper.
[Collection("Ported")]
public class PidFileReadFunnelTests
{
    private static int? TryReadPid(string path)
    {
        var m = typeof(PidFile).GetMethod("TryReadPid", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int?)m.Invoke(null, [path]);
    }

    [Fact]
    public void TryReadPid_ValidParses_InvalidAndMissingAreNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_pidfunnel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "a.pid");
            File.WriteAllText(good, "  1234\n");
            Assert.Equal(1234, TryReadPid(good));
            var bad = Path.Combine(dir, "b.pid");
            File.WriteAllText(bad, "not-a-pid");
            Assert.Null(TryReadPid(bad));
            var empty = Path.Combine(dir, "c.pid");
            File.WriteAllText(empty, "");
            Assert.Null(TryReadPid(empty));
            Assert.Null(TryReadPid(Path.Combine(dir, "missing.pid")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Probes_FunnelThroughSharedRead()
    {
        foreach (var file in new[] { "Cli/ResetHandler.cs", "Cli/RestartHandler.cs", "Cli/StopHandler.cs" })
        {
            var src = SourceScan.Read("src", "Atheriz.Server", file);
            Assert.Contains("TryReadPid(", src);
            Assert.DoesNotContain("File.ReadAllText(pid", src);
        }
        var create = SourceScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("IsLiveClaim(", create);
        var gen = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        var region = SourceScan.Region(gen, "private static bool IsLiveServerFolder(");
        Assert.Contains("TryReadPid(", region);
        Assert.DoesNotContain("File.ReadAllText", region);
        var pid = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Equal(1, SourceScan.Count(pid, "internal static int? TryReadPid("));
        Assert.Equal(1, SourceScan.Count(pid, "internal static bool IsLiveClaim("));
    }
}

// Merged from PortWaitStateTests.cs
// One bounded quiet poll for a port to reach a state; the free wrapper is
// a thin polarity adapter over the shared core.
[Collection("Ported")]
public class PortWaitStateTests
{
    private static async Task<bool> WaitForPortState(int port, TimeSpan timeout, bool wantUp)
    {
        var m = typeof(RestartHandler).GetMethod("WaitForPortStateAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<bool>)m.Invoke(null, [port, timeout, wantUp])!;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task FreePort_IsFree_NotUp()
    {
        int port = FreePort();
        Assert.True(await WaitForPortState(port, TimeSpan.FromSeconds(1), wantUp: false));
        Assert.False(await WaitForPortState(port, TimeSpan.FromMilliseconds(200), wantUp: true));
    }

    [Fact]
    public async Task FreeWrapper_MatchesCorePolarity()
    {
        int port = FreePort();
        var m = typeof(RestartHandler).GetMethod("WaitForPortFreeAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(await (Task<bool>)m.Invoke(null, [port, TimeSpan.FromSeconds(1)])!);
    }

    [Fact]
    public void FreeWrapper_IsThinPolarityAdapter()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "RestartHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task<bool> WaitForPortStateAsync("));
        Assert.Contains("wantUp: false", src);
    }
}

// Merged from NoNativeLibcTests.cs
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

// Merged from VerifiedKillFunnelTests.cs
// One verified-kill funnel (server gate, quiet kill, owner-verified
// release) for the pid-file stop path. A dropped gate would silently kill
// a foreign process.
[Collection("Ported")]
public class VerifiedKillFunnelTests
{
    private static async Task<int> KillVerifiedPid(int pid, int port, string pidFilePath)
    {
        var m = typeof(StopHandler).GetMethod("KillVerifiedPidAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<int>)m.Invoke(null, [pid, port, pidFilePath])!;
    }

    private static async Task<(int Code, string Output)> CaptureOut(Func<Task<int>> fn)
    {
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { return (await fn(), sw.ToString()); }
        finally { Console.SetOut(orig); }
    }

    [Fact]
    public async Task ForeignLivePid_RefusedWithoutSignal()
    {
        // The test host itself: live, but not a server process. The gate
        // precedes any signal, so probing our own pid is safe.
        int self = Process.GetCurrentProcess().Id;
        var (code, output) = await CaptureOut(() => KillVerifiedPid(self, 59991, Path.GetTempFileName()));
        Assert.Equal(1, code);
        Assert.Contains("not a verified Atheriz server process; refusing to terminate", output);
    }

    [Fact]
    public async Task PidFilePath_DeadPid_RemovesStaleClaim()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_killfunnel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var pidFile = Path.Combine(dir, "server.pid");
        File.WriteAllText(pidFile, (int.MaxValue - 9).ToString());
        try
        {
            var (code, output) = await CaptureOut(() => KillVerifiedPid(int.MaxValue - 9, 59992, pidFile));
            Assert.Equal(0, code);
            Assert.Contains("removing stale PID file", output);
            Assert.False(File.Exists(pidFile));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void StopPath_UsesTheFunnel()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task<int> KillVerifiedPidAsync("));
        Assert.Equal(2, SourceScan.Count(src, "KillVerifiedPidAsync(")); // def + pid-file call site
        Assert.Contains("KillVerifiedPidAsync(pid.Value, port, pidFilePath)", src);
        Assert.Contains("IsProcessListeningOnPort(pid, port)", src);
        Assert.DoesNotContain("pidFilePath: null", src);
    }
}

// Merged from TokenFileEmptySemanticsTests.cs
// One token-file read shared by the five admin-token sites. Empty-file
// semantics stay per-site: EnsureToken self-heals an unusable file (a crash
// between CreateNew and Flush leaves a zero-byte wedge that would otherwise
// throw forever — F15), ReadToken/CheckAdmin let it flow into the comparison.
[Collection("Ported")]
public class TokenFileEmptySemanticsTests
{
    private static string NewSecretDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_tokensem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void EnsureToken_MissingFile_CreatesHexToken()
    {
        var dir = NewSecretDir();
        try
        {
            string token = AdminToken.EnsureToken(dir);
            Assert.Equal(64, token.Length);
            Assert.Matches("^[0-9a-f]+$", token);
            Assert.Equal(token, File.ReadAllText(Path.Combine(dir, "admin.token")).Trim());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void EnsureToken_EmptyFile_Regenerates()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "   \n");
            // Exists-but-empty (crash between CreateNew and Flush): after a
            // bounded wait for a concurrent writer, EnsureToken deletes the
            // poison and mints a fresh token instead of wedging forever.
            string token = AdminToken.EnsureToken(dir);
            Assert.Equal(64, token.Length);
            Assert.Matches("^[0-9a-f]+$", token);
            Assert.Equal(token, AdminToken.EnsureToken(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReadToken_EmptyFile_FlowsThrough()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "  \n");
            Assert.Equal("", AdminToken.ReadToken(dir));
            Assert.Null(AdminToken.ReadToken(Path.Combine(Path.GetTempPath(), "atheriz_tokensem_missing_" + Guid.NewGuid().ToString("N"))));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CheckAdmin_EmptyFile_Mismatches()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "\n");
            // A blank file fails closed before any comparison, whatever the
            // candidate token is (audit 10 finding 1).
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(dir, "127.0.0.1", "anything", "test"));
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(Path.Combine(dir, "nodir"), "127.0.0.1", "anything", "test"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TokenRead_LivesInOneHelper_PreservingEmpty()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AdminToken.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static string? TryReadTokenFile("));
        Assert.Contains("Empty is NOT mapped", src);
    }
}

// Merged from StaticRouteHelperTests.cs
// One no-cache trio, one first-existing-file helper, single draw/webclient
// alias registrations (duplicate slash/no-slash patterns threw
// AmbiguousMatchException), default documents for directory URLs, and one
// merged immutable-cache condition.
[Collection("Ported")]
public class StaticRouteHelperTests
{
    [Fact]
    public void NoCacheTrio_DefinedOnce_UsedThrice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static void SetNoCache("));
        Assert.Equal(4, SourceScan.Count(src, "SetNoCache(")); // def + 3 entry-HTML shapes
        Assert.Contains("no-cache, no-store, must-revalidate", src);
    }

    [Fact]
    public void EntryFiles_SingleRoot_NoTemplateFallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.DoesNotContain("templatesCandidate", src);
        Assert.DoesNotContain("FirstExisting(", src);
        Assert.Contains("staticCandidate", src);
    }

    [Fact]
    public void DrawAliases_SingleRegistrations_ImmutableMerged()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        // No slash/no-slash duplicate route pairs (AmbiguousMatchException);
        // the served spellings are pinned behaviorally by ServerHostingDirectTests.
        Assert.DoesNotContain("foreach (var route in", src);
        Assert.Contains("app.MapGet(\"/atheriz_draw\", ServeDraw);", src);
        Assert.Contains("app.MapGet(\"/atheriz_draw/index.html\", ServeDraw);", src);
        Assert.Contains("app.MapGet(\"/webclient\", () => Results.Redirect(\"/webclient/index.html\"));", src);
        Assert.Contains("app.UseDefaultFiles(", src);
        Assert.Contains("||", SourceScan.Region(src, "bool immutable ="));
        Assert.Contains("public, max-age=31536000, immutable", src);
        Assert.Contains("public, max-age=86400", src);
    }
}
