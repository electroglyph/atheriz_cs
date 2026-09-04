using System.Diagnostics;
using System.Reflection;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests;

// Port of atheriz/tests/conftest.py:90 global_test_env faithful helper (not IClassFixture yet)
public static class GlobalTestEnv
{
    // Port of conftest.py:90-115 setup
    public static async Task<TempDirScope> EnterAsync(string? testName = null)
    {
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH"); // Port 100 old_save_path
        var origSave = origEnv ?? new AtherizSettings().SavePath;
        var origSalt = GetCurrentSalt(); // Port 101 old_salt
        var temp = Path.Combine(Path.GetTempPath(), $"atheriz_test_{Guid.NewGuid():N}"); // Port 102 tempfile.mkdtemp
        Directory.CreateDirectory(temp);
        var absTemp = Path.GetFullPath(temp); // absolute for guard Port 66
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", absTemp); // Port 103 settings.SAVE_PATH = temp
        if (origSalt is null)
            SaltProvider.SetSaltForTesting("testsalt"); // Port 104-105

        // Port 108-115 fresh DB
        try { AtherizDbContextFactory.CloseDatabase(); } catch { }
        AtherizDbContextFactory.ReopenDatabase(); // Port 114 _CLOSED=False
        AtherizDbContextFactory.DoSetup(absTemp); // Port 115 do_setup

        // Port 118-183 clear globals
        ObjectRegistry.ClearAll(); // Port 119 _clear_all_objects_nonblocking
        IdGenerator.SetId(-1); // Port 163
        ClearTickerIfExists(); // Port 47 _clear_ticker
        GlobalServices.ResetForTesting(); // Port 166-172 _NODE_HANDLER etc
        ConnectionManager.GlobalInstance = null; // Port 172
        Autosave.ResetForTesting(); // Port 306 reset_autosave
        try { StartStop.ResetForTesting(); } catch { } // Port 186 _shutdown_completed
        try { NodeHandler.SetCurrent(null); } catch { }

        // Port 201-209 watchdog 25s
        var cts = new CancellationTokenSource();
        var name = testName ?? "test";
        var wd = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(25), cts.Token); } catch (OperationCanceledException) { return; }
            if (!cts.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[Watchdog] {name} still running after 25s (hang?)"); // Port 205
                try { Console.Error.WriteLine(Environment.StackTrace); } catch { } // Port 36 dump_threads simple
            }
        });

        // small async yield to keep signature async (no real async work)
        await Task.Yield();
        return new TempDirScope(absTemp, origSave, origEnv, origSalt, cts, wd);
    }

    public static TempDirScope Enter(string? testName = null) => EnterAsync(testName).GetAwaiter().GetResult(); // sync wrapper

    // Port 213-302 teardown
    public static async Task ExitAsync(TempDirScope scope)
    {
        try { scope.WatchdogCts.Cancel(); } catch { } // Port 214 _watchdog_stop.set
        try { await scope.WatchdogTask; } catch { }

        ClearTickerIfExists(); // Port 219 _clear_ticker before close
        try { AtherizDbContextFactory.CloseDatabase(); } catch (Exception ex) { Console.Error.WriteLine($"DB close failed: {ex}"); } // Port 225
        AtherizDbContextFactory.ReopenDatabase(); // Port 230 _CLOSED=False

        try { if (Directory.Exists(scope.TempPath)) Directory.Delete(scope.TempPath, recursive: true); } catch (Exception ex) { Console.Error.WriteLine($"rmtree failed: {ex}"); } // Port 232

        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", scope.OrigEnvSavePath); // Port 238
        if (scope.OrigSalt is not null) SaltProvider.SetSaltForTesting(scope.OrigSalt); else SaltProvider.Clear(); // Port 239

        ObjectRegistry.ClearAll(); // Port 240
        IdGenerator.SetId(-1);
        ClearTickerIfExists();
        GlobalServices.ResetForTesting();
        ConnectionManager.GlobalInstance = null;
        Autosave.ResetForTesting();
        try { StartStop.ResetForTesting(); } catch { }
        try { NodeHandler.SetCurrent(null); } catch { }
        // Port 302 leave
    }

    public static void Exit(TempDirScope scope) => ExitSync(scope);

    // Synchronous cleanup without async blocking — used by Dispose (avoids GetAwaiter().GetResult() sync-over-async)
    internal static void ExitSync(TempDirScope scope)
    {
        try { scope.WatchdogCts.Cancel(); } catch { }
        try { scope.WatchdogTask.Wait(500); } catch { }

        ClearTickerIfExists();
        try { AtherizDbContextFactory.CloseDatabase(); } catch (Exception ex) { Console.Error.WriteLine($"DB close failed: {ex}"); }
        AtherizDbContextFactory.ReopenDatabase();

        try { if (Directory.Exists(scope.TempPath)) Directory.Delete(scope.TempPath, recursive: true); } catch (Exception ex) { Console.Error.WriteLine($"rmtree failed: {ex}"); }

        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", scope.OrigEnvSavePath);
        if (scope.OrigSalt is not null) SaltProvider.SetSaltForTesting(scope.OrigSalt); else SaltProvider.Clear();

        ObjectRegistry.ClearAll();
        IdGenerator.SetId(-1);
        ClearTickerIfExists();
        GlobalServices.ResetForTesting();
        ConnectionManager.GlobalInstance = null;
        Autosave.ResetForTesting();
        try { StartStop.ResetForTesting(); } catch { }
        try { NodeHandler.SetCurrent(null); } catch { }
    }

    private static string? GetCurrentSalt()
    {
        try
        {
            var f = typeof(SaltProvider).GetField("_salt", BindingFlags.NonPublic | BindingFlags.Static);
            return f?.GetValue(null) as string;
        }
        catch { return null; }
    }

    private static void ClearTickerIfExists()
    {
        try
        {
            var f = typeof(GlobalServices).GetField("_asyncTicker", BindingFlags.NonPublic | BindingFlags.Static);
            var t = f?.GetValue(null) as AsyncTicker;
            if (t != null) { try { t.Clear(); } catch { } try { t.Stop(); } catch { } }
            var f2 = typeof(GlobalServices).GetField("_asyncThreadPool", BindingFlags.NonPublic | BindingFlags.Static);
            var p = f2?.GetValue(null) as AsyncThreadPool;
            if (p != null) { try { p.Stop(wait: false); } catch { } }
        }
        catch { }
    }
}

// Port of conftest.py yield scope — TempDirScope : IDisposable, IAsyncDisposable with TempPath+OrigSavePath
public sealed class TempDirScope : IDisposable, IAsyncDisposable
{
    public string TempPath { get; } // Port 212 yield temp_dir
    public string OrigSavePath { get; } // Port 100 old_save_path
    public string? OrigEnvSavePath { get; }
    public string? OrigSalt { get; }
    internal CancellationTokenSource WatchdogCts { get; }
    internal Task WatchdogTask { get; }

    internal TempDirScope(string tempPath, string origSave, string? origEnv, string? origSalt, CancellationTokenSource cts, Task wd)
    {
        TempPath = tempPath;
        OrigSavePath = origSave;
        OrigEnvSavePath = origEnv;
        OrigSalt = origSalt;
        WatchdogCts = cts;
        WatchdogTask = wd;
    }

    public void Dispose() => GlobalTestEnv.ExitSync(this);
    public ValueTask DisposeAsync() => new ValueTask(GlobalTestEnv.ExitAsync(this));
}
