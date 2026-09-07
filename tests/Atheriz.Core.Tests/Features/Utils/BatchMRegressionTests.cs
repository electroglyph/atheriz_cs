using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Microsoft.Data.Sqlite;

namespace Atheriz.Core.Tests.Features.Utils;

// P1-17 Batch M (Menu/Logger/ServerEvents/InitialSetup) regression pins.
[Collection("Ported")]
public class BatchMRegressionTests
{
    // Port of menu.py asyncio.wait_for cancelling the prompt coroutine: a timed-out
    // prompt must complete (not hang) and leave no orphaned InputFuture.
    [Fact]
    public async Task PromptTimeout_CancelsPendingFuture()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new Session();
        var r = await MenuPrompt.PromptWithTimeoutAsync(s, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(r);
        Assert.True(s.InputFuture == null || s.InputFuture.Task.IsCompleted);
    }

    // CancelPrompt only completes the still-pending future (stale tokens are ignored
    // so a newer prompt is never cancelled by an old timeout).
    [Fact]
    public async Task CancelPrompt_TokenGuard()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new Session();
        var t = s.Prompt("x");
        var f = s.InputFuture;
        Assert.NotNull(f);
        Assert.False(s.CancelPrompt(new object()));
        Assert.False(f!.Task.IsCompleted);
        Assert.True(s.CancelPrompt(f));
        Assert.Equal("", await t);
    }

    // Spec-Menu OptionDescs ride alongside Options and render as "[key] desc".
    [Fact]
    public async Task Menu_OptionDescs_Rendered()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConn("m1", "127.0.0.99");
        var sess = new Session(conn);
        var menu = new Menu("Pick", new Dictionary<string, Func<Session, string, Task<bool>>>
        {
            ["a"] = (_, _) => Task.FromResult(false),
        });
        menu.OptionDescs["a"] = "Apple";
        var run = menu.Run(sess, "Pick");
        string display = "";
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < end)
        {
            foreach (var s in conn.Sent)
                foreach (var a in s.Args)
                    if (a is string str && str.Contains("[a]")) display += str;
            if (display.Contains("[a]")) break;
            await Task.Delay(20);
        }
        Assert.Contains("[a] Apple", display);
        sess.InputFuture!.TrySetResult("a");
        var done = await Task.WhenAny(run, Task.Delay(5000));
        Assert.Same(run, done);
        Assert.False(await run);
    }

    // Single-echo: a kept message hits Console.Error exactly once (no AddConsole echo).
    [Fact]
    public void Logger_KeptMessage_EchoesExactlyOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var marker = "echo-once-" + Guid.NewGuid().ToString("N");
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            AtherizLogger.LogInformation(marker);
            log = cap.Read();
        }
        Assert.Equal(1, CountOccurrences(log, marker));
    }

    // Factory refresh: a level change via ApplySettings takes effect on file routing
    // (filtered levels still echo to Console.Error by pinned deviation, but must not
    // reach server.log; re-enabling the level must restore file output).
    [Fact]
    public void Logger_FactoryRefresh_AppliesNewLevel()
    {
        using var env = GlobalTestEnv.Enter();
        var logDir = Path.Combine(Path.GetTempPath(), "atheriz_logrefresh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        var serverLog = Path.Combine(logDir, "server.log");
        var w = "refresh-warn-" + Guid.NewGuid().ToString("N");
        var d = "refresh-debug-" + Guid.NewGuid().ToString("N");
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "error", SavePath = logDir });
            string gated;
            using (var cap = new CaptureAtherizLog())
            {
                AtherizLogger.LogWarning(w);
                gated = cap.Read();
            }
            Assert.Contains(w, gated); // pinned deviation: filtered echo stays
            Assert.False(File.Exists(serverLog) && File.ReadAllText(serverLog).Contains(w));
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = logDir });
            string shown;
            using (var cap = new CaptureAtherizLog())
            {
                AtherizLogger.LogDebug(d);
                shown = cap.Read();
            }
            Assert.Contains(d, shown);
            Assert.Contains(d, File.ReadAllText(serverLog));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(logDir, recursive: true); } catch { }
        }
    }

    // Lock narrowing: concurrent same-name creates serialize (no hang, one winner).
    [Fact]
    public void ServerEvents_ConcurrentSameName_SingleHeroNoHang()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(AtherizSettings.Global.DefaultHome);
        ObjectRegistry.AddObject(home);
        var t1 = Task.Run(() => ServerEvents.AtCharCreate("acc1", "Hero", "password123"));
        var t2 = Task.Run(() => ServerEvents.AtCharCreate("acc1", "Hero", "password123"));
        Assert.True(Task.WaitAll(new[] { t1, t2 }, 15000), "concurrent AtCharCreate hung");
        Assert.Single(ObjectRegistry.FilterBy(o => o.IsPc && o.Name == "Hero"));
    }

    // Setup atomicity: the setup connection runs PRAGMA synchronous=FULL.
    [Fact]
    public void InitialSetup_UsesFullSynchronousPragma()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_pragma_" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(tmp, "save");
        var secret = Path.Combine(tmp, "secret");
        try
        {
            InitialSetup.DoSetup(save, "admin", "password123", secret);
            using var c = new SqliteConnection($"Data Source={Path.Combine(save, "database.sqlite3")}");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA synchronous;";
            Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    private static int CountOccurrences(string hay, string needle)
    {
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
