using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression tests for the server host, CLI, settings, and utilities.
// Each test fails while the defect is present and passes once fixed; a few
// pin verified-correct behavior and pass as-is.
[Collection("Ported")]
public class ServerRegressionTests
{
    // stop fallback must require the PID itself to hold the port.
    [Fact]
    public void StopFallback_RequiresPerPidHold()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.DoesNotContain("PidFile.IsPortListening(port) && PidFile.IsServerProcess(pid.Value)", src);
    }

    // reset must re-check liveness after the stop-wait, before wiping.
    [Fact]
    public void Reset_RechecksLivenessBeforeWipe()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        int wait = src.IndexOf("WaitForPidExitAsync", StringComparison.Ordinal);
        int wipe = src.IndexOf("Directory.Delete(savePath", wait, StringComparison.Ordinal);
        Assert.True(wait >= 0 && wipe > wait);
        var between = src.Substring(wait, wipe - wait);
        Assert.True(between.Contains("IsServerProcess") || between.Contains("IsPortListening"));
    }

    // --host must not survive bash -c expansion ($,`,\,!).
    // Fixed with single-quote armor (nothing expands inside '...') plus a
    // host charset allowlist; stronger than per-char double-quote escaping.
    [Fact]
    public void DaemonArgs_EscapeShellMetachars()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.Contains("BashQuote(string s)", src);
        Assert.Contains("Replace(\"'\", \"'\\\\''\"", src);
        int i = src.IndexOf("escapedArgs =", StringComparison.Ordinal);
        Assert.True(i >= 0);
        var line = src.Substring(i, src.IndexOf('\n', i) - i);
        Assert.Contains("BashQuote", line);
        Assert.Contains("IsSafeHost", src);
    }

    // the fallback spawn must fail loudly, never attach a pump that dies
    // with the spawner and leaves the child on a readerless pipe.
    [Fact]
    public void FallbackSpawn_FailsLoudWithoutPump()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.DoesNotContain("trying direct", src);
        Assert.DoesNotContain("BeginOutputReadLine", src);
        Assert.Contains("use --foreground instead", src);
    }

    // process identity must not match on a mere substring.
    [Fact]
    public void ProcessIdentity_IsStrict()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("|| lower.Contains(\"atheriz\")", src);
    }

    // helper output reads must be async/timeout-bound, never blocking.
    [Fact]
    public void HelperReads_AreTimeoutBound()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Contains("ReadToEndAsync", src);
    }

    // pid lookup must stay pinned to the target game (no listener-
    // cwd probing, no directory-tree walk that could return a foreign game).
    // NB: scoped to LocateServerPidFile — /proc per-PID verification
    // (IsProcessListeningOnPort, HasServerCmdline) stays: the stop path needs it.
    [Fact]
    public void PidLocator_StaysPinned()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        var region = SourceScan.Region(src, "public static string LocateServerPidFile(");
        Assert.DoesNotContain("/proc/", region);
        Assert.DoesNotContain("GetCurrentDirectory", region);
        Assert.DoesNotContain("TryFindPidListeningOnPort", region);
    }

    // a corrupt pid file must not bypass the port guard (split-brain).
    [Fact]
    public void CorruptPid_DoesNotBypassPortGuard()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.True(SourceScan.Count(src, "IsPortListening(webserverPort)") >= 2);
    }

    // rolled-back seeds must not be reported as success.
    // (NB: the CI shell exports ATHERIZ_SUPERUSER_*; they are cleared here
    // so the missing-creds early return is actually exercised.)
    [Fact]
    public void RolledBackSeed_IsNotReportedAsSuccess()
    {
        if (!Console.IsInputRedirected) return; // needs non-interactive stdin
        var ou = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var op = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", null);
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", null);
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"));
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

    // reset must not interactively prompt for a superuser ("limbo only").
    [Fact]
    public void Reset_PassesExplicitNoPromptCreds()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        Assert.DoesNotContain("DoSetup(savePath);", src);
    }

    // GuardWipePath must require world markers, not just a save leaf.
    [Fact]
    public void WipeGuard_RequiresWorldMarkers()
    {
        var leaf = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"), "save");
        Assert.Throws<InvalidOperationException>(() => PathGuards.GuardWipePath(leaf, false));
    }

    // loopback TLS bypass must inspect errors / pin the cert.
    [Fact]
    public void LoopbackTls_InspectsErrors()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.Contains("SslPolicyErrors", src);
    }

    // disconnect must run on the registering manager instance.
    [Fact]
    public void Disconnect_UsesRegisteringManager()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "WebSocketHandler.cs");
        Assert.DoesNotContain("var mgr = ConnectionManager.GlobalInstance;", src);
    }

    // certs without a private key must fail at load, not handshake.
    [Fact]
    public void KeylessCert_FailsAtLoad()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=regtest", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pubPem = cert.ExportCertificatePem();
        var path = Path.Combine(Path.GetTempPath(), "reg_pub_" + Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(path, pubPem);
        try
        {
            Assert.ThrowsAny<Exception>(() => TlsCertLoader.Load(path, null));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // codegen must preserve ref/out/in/params and invariant defaults.
    [Fact]
    public void Codegen_PreservesSignatures()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        var region = SourceScan.Region(src, "private static string BuildParamList(");
        Assert.Contains("IsByRef", region);
        Assert.Contains("InvariantCulture", region);
    }

    // glued -p1 parses as a port; trailing bare flags never become values.
    [Fact]
    public void GluedPort_Parses()
    {
        Assert.Equal(1, ArgumentParser.ParsePort(new[] { "-p1" }));
    }

    [Fact]
    public void TrailingBareFlag_NeverBecomesValue()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("i + 1 >= a.Length", src);
        var novel = SourceScan.Read("src", "Atheriz.Server", "Cli", "NewHandler.cs");
        Assert.Contains("i + 1 >= a.Length", novel);
        Assert.Contains("filtered[0].StartsWith(\"-\", StringComparison.Ordinal)", novel);
    }

    // token lookup must be port/listener-scoped, never a blind tree scan.
    [Fact]
    public void TokenLookup_IsScoped()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.DoesNotContain("SearchOption.AllDirectories", src);
    }

    // single token resolution end-to-end; scheme fallback on mismatch.
    [Fact]
    public void Reload_ResolvesTokenOnceWithFallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ReloadHandler.cs");
        Assert.DoesNotContain("FindTokenFile", src);
        Assert.Equal(2, SourceScan.Count(src, "PostAdminAsync"));
    }

    // respawn preserves the CLI telnet-port override.
    [Fact]
    public void Respawn_PreservesTelnetPort()
    {
        var restart = SourceScan.Read("src", "Atheriz.Server", "Cli", "RestartHandler.cs");
        Assert.Contains("--telnet-port", restart);
        var reset = SourceScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        Assert.Contains("--telnet-port", reset);
    }

    // pid waits must report their outcome.
    [Fact]
    public void PidWait_ReportsOutcome()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        Assert.Contains("public static async Task<bool> WaitForPidExitAsync", src);
    }

    // start handoff must be atomic (no acquire-release-respawn window).
    [Fact]
    public void StartHandoff_IsAtomic()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.DoesNotContain("spawnPid?.Release();", src);
    }

    // the banner scheme must agree with the actual Kestrel scheme.
    [Fact]
    public void BannerScheme_AgreesWithKestrel()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.Contains("AllowInsecureTlsFallback", src);
    }

    // any-address binds must be dual-stack, not v6-only-by-luck.
    [Fact]
    public void AnyAddress_IsDualStack()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "KestrelConfig.cs");
        Assert.DoesNotContain("if (host == \"::\") ip = IPAddress.IPv6Any;", src);
    }

    // declared-length bodies must be read through the byte cap.
    [Fact]
    public void DeclaredLengthBody_IsCapped()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.DoesNotContain("return await JsonDocument.ParseAsync(ctx.Request.Body);", src);
    }

    // open sockets must observe host shutdown.
    [Fact]
    public void Sockets_ObserveShutdown()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "WebSocketHandler.cs");
        Assert.DoesNotContain("CancellationToken.None", src);
    }

    // per-game web customizations win over install assets.
    [Fact]
    public void GameWebCustomizations_Win()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        int cwd = src.IndexOf("Directory.GetCurrentDirectory(), subA", StringComparison.Ordinal);
        int root = src.IndexOf("Path.Combine(contentRoot, subA)", StringComparison.Ordinal);
        Assert.True(cwd >= 0 && root >= 0 && cwd < root);
    }

    // neighbor listing and pathfinding share one caller dispatch
    // (door-blind for caller=None per pathfind.py:122-126, door-aware otherwise).
    [Fact]
    public void NeighborsAndPathfinding_AgreeOnDoors()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.Contains("private static List<Node> GetLinkNodes(Node node, NodeHandler handler)", src);
        Assert.Contains("caller == null", src);
    }

    // the open queue orders nodes via CompareTo (heapq __lt__ parity);
    // no sequence tiebreak fields.
    [Fact]
    public void PathfindQueue_UsesNodeComparer()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.DoesNotContain("_seq", src);
        Assert.DoesNotContain("_nextSeq", src);
        Assert.Contains("PriorityQueue<PathNode, PathNode>", src);
    }

    // unbounded/negative limits must fail validation.
    // (Absolute temp paths isolate the target rule from CWD-dependent
    // SavePath/SecretPath guards.)
    [Fact]
    public void Validator_BoundsLimits()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"));
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

    // configured levels must actually silence stderr.
    [Fact]
    public void LogLevel_SilencesStderr()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.DoesNotContain("Console.Error.WriteLine(filteredLine)", src);
    }

    // scaffold must not emit dangling project references — the emitted
    // Include must resolve to an existing file.
    [Fact]
    public void ScaffoldReference_AlwaysResolves()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(GameTemplateGenerator.CreateGameFolder(tmp, "reggame", overwrite: true));
            var csproj = File.ReadAllText(Path.Combine(tmp, "reggame.csproj"));
            Assert.Contains("Atheriz.Core", csproj);
            var m = System.Text.RegularExpressions.Regex.Match(csproj, "Include=\"([^\"]*Atheriz.Core[^\"]*)\"");
            Assert.True(m.Success);
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(tmp, m.Groups[1].Value))));
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    // interactive replace must either setup the world or say so.
    [Fact]
    public void Replace_SetsUpWorld()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        Assert.Contains("shouldSetup = true", src);
    }

    // seed clock must honor operator time settings.
    [Fact]
    public void SeedClock_HonorsSettings()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.DoesNotContain("new AtherizSettings { SavePath = absSave }", src);
    }

    // dead age arithmetic goes.
    [Fact]
    public void DeadAgeArithmetic_Removed()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("var age = DateTimeOffset.UtcNow", src);
    }

    // one shared /proc/net/tcp helper (duplicated, not triplicated).
    [Fact]
    public void TcpParse_HasSharedHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Contains("GetListeningInodes(", src);
    }

    // refusal messages name the failed check.
    [Fact]
    public void RefusalMessages_NameCheck()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "is not listening on port {port}; refusing"));
    }

    // single thread-pool hop for shutdown.
    [Fact]
    public void ShutdownRoute_SingleHop()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.DoesNotContain("Task.Run(async ()", src);
    }

    // streaming gate is the single enforcement point.
    [Fact]
    public void SizeGate_IsSingle()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "WebSocketHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "WebSocketCloseStatus.MessageTooBig"));
    }

    // dead alias and no-op rethrows go.
    [Fact]
    public void DeadAliasAndRethrows_Removed()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "ServerLifecycle.cs");
        Assert.DoesNotContain("ShutdownLock", src);
        Assert.DoesNotContain("catch { throw; }", src);
        Assert.DoesNotContain("catch (InvalidOperationException) { throw; }", src);
    }

    // single chmod; 64B token re-read per request.
    [Fact]
    public void TokenHandling_IsSimple()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AdminToken.cs");
        Assert.Equal(1, SourceScan.Count(src, "TryChmod0600(tokenFile)"));
        Assert.DoesNotContain("_cachedToken", src);
    }

    // rotation compares bytes to bytes.
    [Fact]
    public void RotationComparesBytes()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "FileLogger.cs");
        var region = SourceScan.Region(src, "if (File.Exists(file))");
        Assert.Contains("GetByteCount", region);
    }

    // write-only latch goes.
    [Fact]
    public void WriteOnlyLatch_Removed()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.DoesNotContain("_fileEnabled", src);
    }

    // dice validates its faces argument.
    [Fact]
    public void DiceFaces_Validated()
    {
        var ex = Record.Exception(() => GameUtils.DiceRoll(1, -1));
        Assert.IsType<ArgumentOutOfRangeException>(ex);
        Assert.Equal("faces", ((ArgumentOutOfRangeException)ex!).ParamName);
    }

    // start dequeued before the loop; no magic sentinel.
    [Fact]
    public void PathfindLoop_IsClean()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.DoesNotContain("var currentNode = startNode;", src);
        Assert.DoesNotContain("if (maxIterations == 50000)", src);
    }

    // equal ports with telnet disabled are no conflict; disabled
    // telnet ports are still range-checked for later re-enable.
    [Fact]
    public void PortRules_ArePrecise()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var v = new AtherizSettingsValidator();
            var sameDisabled = new AtherizSettings { SavePath = tmp, SecretPath = tmp, TelnetEnabled = false, WebserverPort = 9999, TelnetPort = 9999 };
            Assert.False(v.Validate(null, sameDisabled).Failed);
            var badTelnet = new AtherizSettings { SavePath = tmp, SecretPath = tmp, TelnetEnabled = false, TelnetPort = 99999 };
            Assert.True(v.Validate(null, badTelnet).Failed);
            // no privileged-port floor: 80/443 are operator's call, not config errors.
            var lowPort = new AtherizSettings { SavePath = tmp, SecretPath = tmp, TelnetEnabled = false, WebserverPort = 80, TelnetPort = 443 };
            Assert.False(v.Validate(null, lowPort).Failed);
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

}
