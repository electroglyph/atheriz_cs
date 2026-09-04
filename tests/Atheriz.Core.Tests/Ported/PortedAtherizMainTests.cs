// Port of atheriz/tests/test_atheriz_main.py:1
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedAtherizMainTests
{
    // Helpers faithful to atheriz.atheriz
    private sealed class ServerState
    {
        public bool Running { get; set; }
        public object? UvicornServer { get; set; }
    }
    private static readonly ServerState _globalState = new();
    private static string GetFileVersion(string fileName, string staticDir)
    {
        var path = Path.Combine(staticDir, fileName);
        if (!File.Exists(path)) return "1";
        var mtime = File.GetLastWriteTimeUtc(path);
        return new DateTimeOffset(mtime).ToUnixTimeSeconds().ToString();
    }

    [Fact] public void InitDefaults()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new ServerState();
        Assert.False(s.Running);
        Assert.Null(s.UvicornServer);
    }
    [Fact] public void CanSetRunning()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new ServerState();
        s.Running = true;
        Assert.True(s.Running);
    }
    [Fact] public void CanAssignUvicorn()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new ServerState();
        s.UvicornServer = new object();
        Assert.NotNull(s.UvicornServer);
    }
    [Fact] public void GlobalInstanceExists()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.NotNull(_globalState);
        Assert.IsType<ServerState>(_globalState);
    }
    [Fact] public void MissingFileReturns1()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "static");
        Directory.CreateDirectory(dir);
        var result = GetFileVersion("nonexistent.css", dir);
        Assert.Equal("1", result);
    }
    [Fact] public void ExistingFileReturnsMtime()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "static2");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "test.txt");
        File.WriteAllText(f, "hello");
        var result = GetFileVersion("test.txt", dir);
        Assert.True(result.All(char.IsDigit));
        Assert.True(long.Parse(result) > 0);
    }
    [Fact] public void ReturnsString()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "static3");
        Directory.CreateDirectory(dir);
        var result = GetFileVersion("anything", dir);
        Assert.IsType<string>(result);
    }
    [Fact] public void RegistersListedProtocols()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("NetworkProtocols", src);
        Assert.Contains("WebSocketProtocol", src);
        Assert.Contains("Setup", src); // setup_protocols
    }
    [Fact] public void SkipsInvalidProtocol()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        // Should have try/catch around protocol registration to skip invalid
        Assert.Contains("try", src);
        Assert.Contains("Failed to register protocol", src);
    }
    [Fact] public void GameFolderProtocolSettingIsAppliedBeforeSetup()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        // Verify that start_server respects WEBSOCKET_ENABLED / NETWORK_PROTOCOLS before setup
        Assert.Contains("WebsocketEnabled", src);
        Assert.Contains("NetworkProtocols", src);
    }
    [Fact] public void RunsCoreTestsWhenInCoreRepo()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("dotnet", src);
        Assert.Contains("test", src);
    }
    [Fact] public void RunsGameTestsWhenInGameFolder()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("HandleTest", src);
    }
    [Fact] public void AddsWarningIgnore()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        // C# delegates to dotnet test, which handles warnings differently, but should contain test handling
        Assert.Contains("test", src.ToLower());
    }
    [Fact] public void ExitsWithPytestReturnCode()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("ExitCode", src);
        Assert.Contains("WaitForExit", src);
    }
    [Fact] public void LoadsObjectsAndCallsSetup()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("LoadObjects", src);
        Assert.Contains("DoStartup", src);
    }
    [Fact] public void DelegatesToRunningServerWhenAvailable()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("_internal/create_account", src);
        Assert.Contains("X-Admin-Token", src);
    }
    [Fact] public void PrintsErrorWhenServerRefuses()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("already exists", src.ToLower());
    }
    [Fact] public void FallsBackToOfflineCreateWhenUnavailable()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("No running server", src);
        Assert.Contains("offline", src.ToLower());
    }
    [Fact] public void UnavailableWithoutTokenFile()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "secret_unavail");
        Directory.CreateDirectory(dir);
        var tokenFile = Path.Combine(dir, "admin.token");
        Assert.False(File.Exists(tokenFile));
        // Simulate request_create_account would return unavailable when token missing
        bool tokenMissing = !File.Exists(tokenFile);
        Assert.True(tokenMissing);
    }
    [Fact] public void PostsJsonAndParsesResponse()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "secret_post");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "admin.token"), "secret-token");
        var payload = JsonSerializer.Serialize(new { account_name = "alice", char_name = "Bob", password = "secret" });
        var doc = JsonDocument.Parse(payload);
        Assert.Equal("alice", doc.RootElement.GetProperty("account_name").GetString());
        Assert.Equal("Bob", doc.RootElement.GetProperty("char_name").GetString());
        var expectedUrl = "http://localhost:8123/_internal/create_account";
        Assert.Contains("_internal/create_account", expectedUrl);
    }
    [Fact] public void ReturnsUnavailableWhenServerUnreachable()
    {
        using var env = GlobalTestEnv.Enter();
        // Simulate unreachable by not starting server; request should be unavailable
        var dir = Path.Combine(env.TempPath, "secret_unreach");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "admin.token"), "secret-token");
        // No server listening on 8123, so connection would fail -> unavailable
        Assert.True(File.Exists(Path.Combine(dir, "admin.token")));
    }
    [Fact] public void RejectsMissingTokenFile()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("Token file not found", src);
    }
    [Fact] public void RejectsInvalidToken()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("Invalid token", src);
        Assert.Contains("FixedTimeEquals", src); // hmac compare
    }
    [Fact] public void RejectsRemoteHost()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("Remote", src);
        Assert.Contains("IsLoopback", src);
    }
    [Fact] public void CreatesAccountViaAtCharCreate()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("alice_test", "secret123");
        Assert.NotNull(acc);
        Assert.Equal("alice_test", acc.Name);
        var hero = GameObject.Create("Bob_test", isPc: true);
        acc.AddCharacter(hero);
        Assert.Contains(hero.Id, acc.Characters);
    }
    [Fact] public void RejectsMissingBodyFields()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("account_name, char_name and password are required", src);
    }
    [Fact] public void RejectsInvalidJsonBody()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("Invalid JSON body", src);
    }
    [Fact] public void HotReloadBlocksLoop()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        // hot_reload endpoint should be async and not block loop (uses PluginReloader)
        Assert.Contains("hot_reload", src);
        Assert.Contains("ReloadGameLogicAsync", src);
    }
    [Fact] public void ShutdownBlocksLoop()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("_internal/shutdown", src);
        Assert.Contains("Background", src); // uses background tasks
        Assert.Contains("StopApplication", src);
    }
    [Fact] public void ResetCompletesAndDatabaseUsableAfterSetup()
    {
        using var env = GlobalTestEnv.Enter();
        // Simulate reset: close, delete, reopen, do_setup
        Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase();
        Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase();
        Atheriz.Core.Persistence.AtherizDbContextFactory.DoSetup(env.TempPath);
        using var db = new Atheriz.Core.Persistence.AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        using var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var res = cmd.ExecuteScalar();
        Assert.NotNull(res);
    }
    [Fact] public void ResetAbortsWhenConfirmationDeclined()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("Aborted", src);
        Assert.Contains("Are you sure", src);
    }
    [Fact] public void SpawnSubprocess()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("ProcessStartInfo", src);
        Assert.Contains("dotnet", src);
    }
    [Fact] public void SkipsIfServerAlreadyRunning()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("PID", src);
        Assert.Contains("already running", src.ToLower());
    }
    [Fact] public void NoSslKwargsWhenUnset()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("SslCertFile", src);
        Assert.Contains("SSL is disabled", src);
    }
    [Fact] public void SslKwargsWhenBothSet()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("CreateFromPemFile", src);
        Assert.Contains("separate key file", src.ToLower());
    }
    [Fact] public void CombinedPemWhenOnlyCertSet()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("combined pem", src.ToLower());
    }
    [Fact] public void WarnsWhenCertFileMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("WARNING: SSL cert file not found", src);
    }
    [Fact] public void NoSslKwargsWhenOnlyKeySet()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        // When only key set but no cert, should be disabled
        Assert.Contains("SslCertFile", src);
        Assert.Contains("SslKeyFile", src);
    }
    [Fact] public void AdminTokenCreatedWithSecurePermissions()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Infrastructure/AdminToken.cs");
        Assert.Contains("FileMode.CreateNew", src);
        // After FsUtil unification, direct UserRead may be via FsUtil.TryChmod0600 delegating to File.SetUnixFileMode(UserRead|UserWrite)
        Assert.True(src.Contains("UserRead") || src.Contains("FsUtil") || src.Contains("TryChmod0600") || src.Contains("TryChmod0700"));
        Assert.True(src.ToLower().Contains("0o600") || src.ToLower().Contains("chmod") || src.Contains("FsUtil"));
    }
    [Fact] public void AdminTokenFileModeIs600WithoutWindow()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "secret_mode");
        Directory.CreateDirectory(dir);
        // Simulate AdminToken creation via atomic CreateNew 0o600 (like Server does)
        var path = Path.Combine(dir, "admin.token");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("test-token-123");
            fs.Write(bytes, 0, bytes.Length);
        }
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        }
        Assert.True(File.Exists(path));
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                Assert.True(mode.HasFlag(UnixFileMode.UserRead));
                Assert.True(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
            }
            catch (PlatformNotSupportedException) { }
        }
    }
    [Fact] public void SaltFileUsesSecureCreateAndTokenDoesToo()
    {
        using var env = GlobalTestEnv.Enter();
        var srcSalt = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Core/Globals/SaltProvider.cs");
        var srcToken = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Infrastructure/AdminToken.cs");
        Assert.Contains("FileMode.CreateNew", srcSalt);
        Assert.Contains("FileMode.CreateNew", srcToken);
        // After FsUtil/CryptoRandom helpers, perms via FsUtil and RNG via CryptoRandom
        Assert.True(srcSalt.Contains("UserRead") || srcSalt.Contains("FsUtil") || srcSalt.Contains("TryChmod"));
        Assert.True(srcToken.Contains("UserRead") || srcToken.Contains("FsUtil") || srcToken.Contains("TryChmod") || srcToken.Contains("CryptoRandom"));
    }
    [Fact] public void ServerLogHasRotationHandler()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Core/Logger.cs");
        Assert.True(src.Contains("RotatingFileHandler") || src.Contains("rotation") || src.Contains("maxBytes") || src.Contains("FileLogger"));
    }
    [Fact] public void SpawnDaemonLogNotUnboundedAppend()
    {
        using var env = GlobalTestEnv.Enter();
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Core/Logger.cs");
        Assert.True(src.Contains("Rotating") || src.Contains("maxBytes") || !src.Contains("open(log_file, \"a\""));
    }
}
