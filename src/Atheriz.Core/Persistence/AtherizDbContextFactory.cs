using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Factory porting <c>atheriz/database_setup.py</c> reopen pattern.
/// Mirrors <c>get_database / reopen_database / do_setup</c> with static <c>_CLOSED</c> guard.
/// </summary>
public static class AtherizDbContextFactory
{
    // Gate forwarding to DbWriteGate (mirrors Database.lock) — obsolete alias for tests
    [Obsolete("Use DbWriteGate.SemaphoreForTesting")]
    public static System.Threading.SemaphoreSlim Gate => DbWriteGate.SemaphoreForTesting;

    public static bool IsClosed => AtherizDbContext.IsClosed;

    // Port of database_setup.py:45 reopen_database() — clears _CLOSED for reset command (atheriz.py:1474)
    public static void ReopenDatabase() => AtherizDbContext.ReopenDatabase();

    // Alias for Python Database.close() marking closed
    public static void CloseDatabase() => AtherizDbContext.CloseDatabase();

    // Port of database_setup.py:56 get_database() — creates context with guard and directory ensure.
    // Guard violations (bad save path) PROPAGATE: silently substituting an
    // ephemeral :memory: database makes writes succeed and go nowhere.
    // Tests needing memory use CreateForTests() explicitly.
    public static AtherizDbContext Create(string savePath)
    {
        // Guard mirrors get_database raising if _CLOSED
        if (IsClosed) throw new InvalidOperationException("database is closed; refusing to reopen");
        return new AtherizDbContext(savePath);
    }

    // Parameterless: same default-path resolution as ObjectRegistry.SaveObjects
    // (ATHERIZ_SAVE_PATH else configured SavePath) — never a divergent file.
    public static AtherizDbContext Create()
    {
        var savePath = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? AtherizSettings.Global.SavePath;
        return Create(savePath);
    }

    // Port of test helper: in-memory or temp file context
    public static AtherizDbContext CreateForTests(string? savePath = null)
    {
        if (savePath == null)
        {
            var opts = new DbContextOptionsBuilder<AtherizDbContext>().UseSqlite("Data Source=:memory:").Options;
            var ctx = new AtherizDbContext(opts);
            ctx.Database.OpenConnection();
            ctx.Database.EnsureCreated();
            return ctx;
        }
        return Create(savePath);
    }

    // Port of database_setup.py:92 do_setup() — EnsureCreated + seed gametime id 0 if missing
    public static void DoSetup(string savePath)
    {
        using var ctx = Create(savePath);
        DoSetup(ctx);
    }

    public static void DoSetup(AtherizDbContext ctx)
    {
        ctx.EnsureCreated();
        // WAL pragma already applied in EnsureCreated with fallback log; extra attempt for safety
        try { ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); } catch (Exception ex) { Console.Error.WriteLine($"WAL pragma fallback in DoSetup: {ex.Message}"); try { ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;"); } catch (Exception) { } }
        // No gametime row seed (database_setup.py:92-111 do_setup creates
        // tables only): GameTime.Save upserts, and a seeded "{}" row would
        // shadow legacy save/time migration (time.py:72-74 migrates only
        // when the row is missing).
    }

    public static async Task DoSetupAsync(string savePath, CancellationToken ct = default)
    {
        await using var ctx = Create(savePath);
        await ctx.EnsureCreatedAsync(ct);
        try { await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct); } catch (Exception ex) { Console.Error.WriteLine($"WAL pragma fallback in DoSetupAsync: {ex.Message}"); }
        try
        {
            var exists = await ctx.GameTime.AsNoTracking().AnyAsync(x => x.Id == 0, ct);
            if (!exists)
            {
                ctx.GameTime.Add(new GameTimeRow { Id = 0, Data = "{}" });
                await ctx.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"DoSetupAsync seed failed: {ex.Message}"); }
    }

    // Parameterless overload using default settings SavePath
    public static void DoSetup()
    {
        var savePath = AtherizSettings.Global.SavePath;
        DoSetup(savePath);
    }
}
