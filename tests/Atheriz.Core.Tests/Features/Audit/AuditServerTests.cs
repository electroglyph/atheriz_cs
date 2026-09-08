using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Audit;

// Fourth-pass regression tests for audit2.md §5 (Server/Utils/Settings).
// Each test FAILS while the finding is present and PASSES once fixed.
// S-P3-13 is EXEMPT per the owner ruling recorded in
// GameTemplateGenerator.cs:6-9 (reflection-for-codegen); no test.
// No production code touched.
[Collection("Ported")]
public class AuditServerTests
{
    // S-P0-1: stop fallback must require the PID itself to hold the port.
    [Fact]
    public void S_P0_1_StopFallback_RequiresPerPidHold()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.DoesNotContain("PidFile.IsPortListening(port) && PidFile.IsServerProcess(pid.Value)", src);
    }

    // S-P0-2: reset must re-check liveness after the stop-wait, before wiping.
    [Fact]
    public void S_P0_2_Reset_RechecksLivenessBeforeWipe()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        int wait = src.IndexOf("WaitForPidExitAsync", StringComparison.Ordinal);
        int wipe = src.IndexOf("Directory.Delete(savePath", wait, StringComparison.Ordinal);
        Assert.True(wait >= 0 && wipe > wait);
        var between = src.Substring(wait, wipe - wait);
        Assert.True(between.Contains("IsServerProcess") || between.Contains("IsPortListening"));
    }

    // S-P1-1: --host must not survive bash -c double-quote expansion ($,`,\,!).
    [Fact]
    public void S_P1_1_DaemonArgs_EscapeShellMetachars()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        int i = src.IndexOf("escapedArgs =", StringComparison.Ordinal);
        Assert.True(i >= 0);
        var line = src.Substring(i, src.IndexOf('\n', i) - i);
        Assert.Contains("Replace(\"`\", ", line);
    }

    // S-P1-2: the fallback spawn must attach the log stream to the child.
    [Fact]
    public void S_P1_2_FallbackSpawn_AttachesLog()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        int i = src.IndexOf("trying direct", StringComparison.Ordinal);
        Assert.True(i >= 0);
        Assert.Contains("RedirectStandardOutput = true", src.Substring(i));
    }

    // S-P1-3: process identity must not match on a mere substring.
    [Fact]
    public void S_P1_3_ProcessIdentity_IsStrict()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("|| lower.Contains(\"atheriz\")", src);
    }

    // S-P1-4: helper output reads must be async/timeout-bound, never blocking.
    [Fact]
    public void S_P1_4_HelperReads_AreTimeoutBound()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Contains("ReadToEndAsync", src);
    }

    // S-P1-5: pid/token lookup must stay pinned to the target game.
    [Fact]
    public void S_P1_5_PidLocator_StaysPinned()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("/proc/", src);
        Assert.DoesNotContain("for (int i = 0; i < 6", src);
    }

    // S-P1-6: a corrupt pid file must not bypass the port guard (split-brain).
    [Fact]
    public void S_P1_6_CorruptPid_DoesNotBypassPortGuard()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.True(AuditScan.Count(src, "IsPortListening(webserverPort)") >= 2);
    }

    // S-P1-7: rolled-back seeds must not be reported as success.
    // (NB: the CI shell exports ATHERIZ_SUPERUSER_*; they are cleared here
    // so the missing-creds early return is actually exercised.)
    [Fact]
    public void S_P1_7_RolledBackSeed_IsNotReportedAsSuccess()
    {
        if (!Console.IsInputRedirected) return; // needs non-interactive stdin
        var ou = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var op = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", null);
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", null);
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Atheriz.Core.InitialSetup.DoSetup(dir, "", "", Path.Combine(dir, "secret"));
            using var db = new Atheriz.Core.Persistence.AtherizDbContext(dir);
            Assert.True(db.Areas.Any() || db.MapData.Any());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", ou);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", op);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // S-P1-8: reset must not interactively prompt for a superuser ("limbo only").
    [Fact]
    public void S_P1_8_Reset_PassesExplicitNoPromptCreds()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        Assert.DoesNotContain("DoSetup(savePath);", src);
    }

    // S-P1-9: GuardWipePath must require world markers, not just a save leaf.
    [Fact]
    public void S_P1_9_WipeGuard_RequiresWorldMarkers()
    {
        var leaf = Path.Combine(Path.GetTempPath(), "atheriz_audit_" + Guid.NewGuid().ToString("N"), "save");
        Assert.Throws<InvalidOperationException>(() => PathGuards.GuardWipePath(leaf, false));
    }

    // S-P1-10: loopback TLS bypass must inspect errors / pin the cert.
    [Fact]
    public void S_P1_10_LoopbackTls_InspectsErrors()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.Contains("SslPolicyErrors", src);
    }

    // S-P1-11: disconnect must run on the registering manager instance.
    [Fact]
    public void S_P1_11_Disconnect_UsesRegisteringManager()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Hosting", "WebSocketHandler.cs");
        Assert.DoesNotContain("var mgr = ConnectionManager.GlobalInstance;", src);
    }

    // S-P1-12: certs without a private key must fail at load, not handshake.
    [Fact]
    public void S_P1_12_KeylessCert_FailsAtLoad()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=audit", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pubPem = cert.ExportCertificatePem();
        var path = Path.Combine(Path.GetTempPath(), "audit_pub_" + Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(path, pubPem);
        try
        {
            Assert.ThrowsAny<Exception>(() => TlsCertLoader.Load(path, null));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // S-P1-13: codegen must preserve ref/out/in/params and invariant defaults.
    [Fact]
    public void S_P1_13_Codegen_PreservesSignatures()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        var region = AuditScan.Region(src, "private static string BuildParamList(");
        Assert.Contains("IsByRef", region);
        Assert.Contains("InvariantCulture", region);
    }

    // S-P2-1: glued -p1 parses as a port; trailing bare flags never become values.
    [Fact]
    public void S_P2_1_GluedPort_Parses()
    {
        Assert.Equal(1, ArgumentParser.ParsePort(new[] { "-p1" }));
    }

    [Fact]
    public void S_P2_1b_TrailingBareFlag_NeverBecomesValue()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("i + 1 >= a.Length", src);
    }

    // S-P2-2: token lookup must be port/listener-scoped, never a blind tree scan.
    [Fact]
    public void S_P2_2_TokenLookup_IsScoped()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.DoesNotContain("SearchOption.AllDirectories", src);
    }

    // S-P2-3: single token resolution end-to-end; scheme fallback on mismatch.
    [Fact]
    public void S_P2_3_Reload_ResolvesTokenOnceWithFallback()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "ReloadHandler.cs");
        Assert.DoesNotContain("FindTokenFile", src);
        Assert.Equal(2, AuditScan.Count(src, "PostAdminAsync"));
    }

    // S-P2-4: respawn preserves the CLI telnet-port override.
    [Fact]
    public void S_P2_4_Respawn_PreservesTelnetPort()
    {
        var restart = AuditScan.Read("src", "Atheriz.Server", "Cli", "RestartHandler.cs");
        Assert.Contains("--telnet-port", restart);
        var reset = AuditScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        Assert.Contains("--telnet-port", reset);
    }

    // S-P2-5: pid waits must report their outcome.
    [Fact]
    public void S_P2_5_PidWait_ReportsOutcome()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        Assert.Contains("public static async Task<bool> WaitForPidExitAsync", src);
    }

    // S-P2-6: start handoff must be atomic (no acquire-release-respawn window).
    [Fact]
    public void S_P2_6_StartHandoff_IsAtomic()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.DoesNotContain("spawnPid?.Release();", src);
    }

    // S-P2-7: the banner scheme must agree with the actual Kestrel scheme.
    [Fact]
    public void S_P2_7_BannerScheme_AgreesWithKestrel()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.Contains("AllowInsecureTlsFallback", src);
    }

    // S-P2-8: any-address binds must be dual-stack, not v6-only-by-luck.
    [Fact]
    public void S_P2_8_AnyAddress_IsDualStack()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Hosting", "KestrelConfig.cs");
        Assert.DoesNotContain("if (host == \"::\") ip = IPAddress.IPv6Any;", src);
    }

    // S-P2-9: declared-length bodies must be read through the byte cap.
    [Fact]
    public void S_P2_9_DeclaredLengthBody_IsCapped()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.DoesNotContain("return await JsonDocument.ParseAsync(ctx.Request.Body);", src);
    }

    // S-P2-10: open sockets must observe host shutdown.
    [Fact]
    public void S_P2_10_Sockets_ObserveShutdown()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Hosting", "WebSocketHandler.cs");
        Assert.DoesNotContain("CancellationToken.None", src);
    }

    // S-P2-11: per-game web customizations win over install assets.
    [Fact]
    public void S_P2_11_GameWebCustomizations_Win()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        int cwd = src.IndexOf("Directory.GetCurrentDirectory(), subA", StringComparison.Ordinal);
        int root = src.IndexOf("Path.Combine(contentRoot, subA)", StringComparison.Ordinal);
        Assert.True(cwd >= 0 && root >= 0 && cwd < root);
    }

    // S-P2-12: neighbor listing and pathfinding must share door semantics.
    [Fact]
    public void S_P2_12_NeighborsAndPathfinding_AgreeOnDoors()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.DoesNotContain("? GetLinkNodes(currentNode.Position, nh)", src);
    }

    // S-P2-13: unbounded/negative limits must fail validation.
    // (Absolute temp paths isolate the target rule from CWD-dependent
    // SavePath/SecretPath guards.)
    [Fact]
    public void S_P2_13_Validator_BoundsLimits()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var v = new AtherizSettingsValidator();
            var neg = new AtherizSettings { SavePath = tmp, SecretPath = tmp, TelnetMaxPendingSends = -1 };
            Assert.Contains("TelnetMaxPendingSends", v.Validate(null, neg).FailureMessage ?? "");
            var huge = new AtherizSettings { SavePath = tmp, SecretPath = tmp, WebsocketMaxMessageSize = int.MaxValue };
            Assert.Contains("WebsocketMaxMessageSize", v.Validate(null, huge).FailureMessage ?? "");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    // S-P2-14: configured levels must actually silence stderr.
    // NOTE: PortedConnectionTestsPart2.DispatchUnknownCmdLogged pins the
    // current echo; it must be updated with this fix (see audit2.md notes).
    [Fact]
    public void S_P2_14_LogLevel_SilencesStderr()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.DoesNotContain("Console.Error.WriteLine(filteredLine)", src);
    }

    // S-P2-15: scaffold must not emit dangling project references.
    [Fact]
    public void S_P2_15_ScaffoldReference_AlwaysResolves()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        Assert.Contains("PackageReference Include=\"Atheriz.Core\"", src);
    }

    // S-P2-16: interactive replace must either setup the world or say so.
    [Fact]
    public void S_P2_16_Replace_SetsUpWorld()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        Assert.Contains("shouldSetup = true", src);
    }

    // S-P2-17: seed clock must honor operator time settings.
    [Fact]
    public void S_P2_17_SeedClock_HonorsSettings()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.DoesNotContain("new AtherizSettings { SavePath = absSave }", src);
    }

    // S-P3-1: dead age arithmetic goes.
    [Fact]
    public void S_P3_1_DeadAgeArithmetic_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("var age = DateTimeOffset.UtcNow", src);
    }

    // S-P3-2 PARTIAL: one shared /proc/net/tcp helper (duplicated, not triplicated).
    [Fact]
    public void S_P3_2_TcpParse_HasSharedHelper()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Contains("GetListeningInodes(", src);
    }

    // S-P3-3: refusal messages name the failed check.
    [Fact]
    public void S_P3_3_RefusalMessages_NameCheck()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Equal(1, AuditScan.Count(src, "is not listening on port {port}; refusing"));
    }

    // S-P3-4: single thread-pool hop for shutdown.
    [Fact]
    public void S_P3_4_ShutdownRoute_SingleHop()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.DoesNotContain("Task.Run(async ()", src);
    }

    // S-P3-5: streaming gate is the single enforcement point.
    [Fact]
    public void S_P3_5_SizeGate_IsSingle()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Hosting", "WebSocketHandler.cs");
        Assert.Equal(1, AuditScan.Count(src, "WebSocketCloseStatus.MessageTooBig"));
    }

    // S-P3-6: dead alias and no-op rethrows go.
    [Fact]
    public void S_P3_6_DeadAliasAndRethrows_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "ServerLifecycle.cs");
        Assert.DoesNotContain("ShutdownLock", src);
        Assert.DoesNotContain("catch { throw; }", src);
        Assert.DoesNotContain("catch (InvalidOperationException) { throw; }", src);
    }

    // S-P3-7: single chmod; 64B token re-read per request.
    [Fact]
    public void S_P3_7_TokenHandling_IsSimple()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "AdminToken.cs");
        Assert.Equal(1, AuditScan.Count(src, "TryChmod0600(tokenFile)"));
        Assert.DoesNotContain("_cachedToken", src);
    }

    // S-P3-8: rotation compares bytes to bytes.
    [Fact]
    public void S_P3_8_RotationComparesBytes()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "FileLogger.cs");
        var region = AuditScan.Region(src, "if (File.Exists(file))");
        Assert.Contains("GetByteCount", region);
    }

    // S-P3-9: write-only latch goes.
    [Fact]
    public void S_P3_9_WriteOnlyLatch_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.DoesNotContain("_fileEnabled", src);
    }

    // S-P3-10: dice validates; detach throws; one signature helper.
    [Fact]
    public void S_P3_10_DiceDetachSignature_Fixed()
    {
        var ex = Record.Exception(() => GameUtils.DiceRoll(1, -1));
        Assert.IsType<ArgumentOutOfRangeException>(ex);
        Assert.Equal("faces", ((ArgumentOutOfRangeException)ex!).ParamName);
        Assert.ThrowsAny<Exception>(() => GameUtils.Detach<Action>(() => { }));
        var src = AuditScan.Read("src", "Atheriz.Core", "Utils", "GameUtils.cs");
        Assert.DoesNotContain("BuildSignatureFromCode", src);
    }

    // S-P3-11: dead tiebreak state goes.
    [Fact]
    public void S_P3_11_DeadTiebreak_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.DoesNotContain("private readonly long _seq;", src);
        Assert.DoesNotContain("public int CompareTo(PathNode? other)", src);
    }

    // S-P3-12: start dequeued before the loop; no magic sentinel.
    [Fact]
    public void S_P3_12_PathfindLoop_IsClean()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.DoesNotContain("var currentNode = startNode;", src);
        Assert.DoesNotContain("if (maxIterations == 50000)", src);
    }

    // S-P3-14: overloads agree positionally; hashing is capped.
    [Fact]
    public void S_P3_14_SyncChecker_IsConsistentAndCapped()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Infrastructure", "WebclientSyncChecker.cs");
        Assert.DoesNotContain("string gameCwd, string? osName = null, string? engineWebOverride = null", src);
        Assert.DoesNotContain("sha.ComputeHash(fs)", src);
    }

    // S-P3-15: equal ports with telnet disabled are no conflict; disabled
    // telnet ports are still range-checked for later re-enable.
    [Fact]
    public void S_P3_15_PortRules_ArePrecise()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var v = new AtherizSettingsValidator();
            var sameDisabled = new AtherizSettings { SavePath = tmp, SecretPath = tmp, TelnetEnabled = false, WebserverPort = 9999, TelnetPort = 9999 };
            Assert.False(v.Validate(null, sameDisabled).Failed);
            var badTelnet = new AtherizSettings { SavePath = tmp, SecretPath = tmp, TelnetEnabled = false, TelnetPort = 99999 };
            Assert.True(v.Validate(null, badTelnet).Failed);
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    // S-P3-16: platform suppression scoped to call sites, not the assembly.
    [Fact]
    public void S_P3_16_PlatformSuppression_IsScoped()
    {
        var src = AuditScan.Read("src", "Atheriz.Server", "Atheriz.Server.csproj");
        Assert.DoesNotContain("<NoWarn>CA1416</NoWarn>", src);
    }

    // S-P3-17: portable glob; single central-management flag.
    [Fact]
    public void S_P3_17_BuildFiles_ArePortableAndSingleSourced()
    {
        var csproj = AuditScan.Read("src", "Atheriz.Server", "Atheriz.Server.csproj");
        Assert.DoesNotContain("web\\**\\*", csproj);
        var build = AuditScan.Read("Directory.Build.props");
        var packages = AuditScan.Read("Directory.Packages.props");
        Assert.Equal(1, AuditScan.Count(build, "ManagePackageVersionsCentrally") + AuditScan.Count(packages, "ManagePackageVersionsCentrally"));
    }
}
