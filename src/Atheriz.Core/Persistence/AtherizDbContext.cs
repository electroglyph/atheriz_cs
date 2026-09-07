using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// EF Core context. Single SQLite file at <c>{SavePath}/database.sqlite3</c>,
/// matching Python's <c>atheriz/database_setup.py:get_database</c>.
/// Uses serialized writes via SemaphoreSlim (replaces Database.lock RLock).
/// </summary>
public sealed class AtherizDbContext : DbContext
{
    private readonly string _savePath;
    private readonly string _dbPath;

    public DbSet<ObjectRow> Objects => Set<ObjectRow>();
    public DbSet<MapDataRow> MapData => Set<MapDataRow>();
    public DbSet<AreaRow> Areas => Set<AreaRow>();
    public DbSet<TransitionRow> Transitions => Set<TransitionRow>();
    public DbSet<DoorRow> Doors => Set<DoorRow>();
    public DbSet<GameTimeRow> GameTime => Set<GameTimeRow>();
    public DbSet<CheckpointRow> Checkpoints => Set<CheckpointRow>();

    // Shared write gate lives in DbWriteGate (re-entrant RLock semantics).
    // (An earlier per-class SemaphoreSlim was removed: zero uses, DbWriteGate only.)

    // Port of database_setup.py:14-15 _CLOSED and _DATABASE global
    private static bool _closed = false;
    private static readonly object _initLock = new();
    public static bool IsClosed { get { lock (_initLock) return _closed; } }

    // Port of database_setup.py:45 reopen_database() — clears _CLOSED flag for reset command (atheriz.py:1472)
    public static void ReopenDatabase()
    {
        lock (_initLock) _closed = false;
    }

    // Port of database_setup.py:24 Database.close() marking _CLOSED
    public static void CloseDatabase()
    {
        lock (_initLock) _closed = true;
    }

    // Instance close mirrors Python Database.close() via static flag
    public void Close()
    {
        lock (_initLock) _closed = true;
        try { Database.CloseConnection(); } catch (Exception) { }
    }

    public AtherizDbContext(string savePath)
    {
        lock (_initLock)
        {
            if (_closed) throw new InvalidOperationException("database is closed; refusing to reopen");
        }
        PathGuards.GuardSavePath(savePath);
        _savePath = savePath;
        _dbPath = Path.Combine(_savePath, "database.sqlite3");
    }

    public AtherizDbContext(AtherizSettings settings) : this(settings.SavePath) { }

    // For testing — in-memory or temp file via options (bypasses _closed guard)
    public AtherizDbContext(DbContextOptions<AtherizDbContext> options) : base(options)
    {
        _savePath = "";
        _dbPath = "";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (options.IsConfigured) return;
        if (!string.IsNullOrEmpty(_savePath))
            Directory.CreateDirectory(_savePath);
        options.UseSqlite($"Data Source={_dbPath};Cache=Shared");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ObjectRow>().ToTable("objects");
        b.Entity<ObjectRow>().HasKey(x => x.Id);
        b.Entity<ObjectRow>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<MapDataRow>().ToTable("mapdata");
        b.Entity<MapDataRow>().HasKey(x => new { x.Area, x.Z });

        b.Entity<AreaRow>().ToTable("areas");
        b.Entity<TransitionRow>().ToTable("transitions");
        b.Entity<TransitionRow>().HasKey(x => new { x.ToArea, x.ToX, x.ToY, x.ToZ });
        b.Entity<DoorRow>().ToTable("doors");
        b.Entity<DoorRow>().HasKey(x => new { x.Area, x.X, x.Y, x.Z });
        b.Entity<GameTimeRow>().ToTable("gametime");
        b.Entity<GameTimeRow>().HasKey(x => x.Id);
        b.Entity<GameTimeRow>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<CheckpointRow>().ToTable("checkpoints");
        b.Entity<CheckpointRow>().HasKey(x => x.Id);
        b.Entity<CheckpointRow>().Property(x => x.Id).ValueGeneratedNever();
    }

    private void ApplyWalPragmas()
    {
        try { Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;"); }
        catch (Exception ex)
        {
            // Loud: falling back to DELETE journaling on a multi-context workload
            // risks SQLITE_BUSY surfacing to saves; the fallback stays (port) but must be visible.
            AtherizLogger.LogError($"WAL pragmas failed ({ex.Message}); falling back to DELETE journal.", ex);
            try { Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;"); Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;"); Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;"); }
            catch (Exception ex2) { AtherizLogger.LogError($"WAL fallback pragmas failed: {ex2.Message}", ex2); }
        }
        try { Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;"); } catch (Exception ex3) { AtherizLogger.LogDebug($"Suppressed AtherizDbContext.ApplyWalPragmas busy_timeout: {ex3.Message}", "AtherizDbContext"); }
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        await DbWriteGate.EnterAsync(ct);
        try
        {
            await Database.EnsureCreatedAsync(ct);
            try { ApplyWalPragmas(); } catch (Exception ex) { Console.Error.WriteLine($"WAL pragma fallback: {ex.Message}"); }
        }
        finally
        {
            // The awaits above may resume on another pool thread, but the hold
            // belongs to this async flow (AsyncLocal continuity), not to the
            // taker thread — so the hop-aware exit is the correct pairing
            // whether a hop happened or not (no thread-id compare to mis-pair).
            DbWriteGate.ExitAfterThreadHop();
        }
    }

    public void EnsureCreated()
    {
        DbWriteGate.Enter();
        try
        {
            Database.EnsureCreated();
            try { ApplyWalPragmas(); } catch (Exception ex) { Console.Error.WriteLine($"WAL pragma fallback: {ex.Message}"); }
        }
        finally { DbWriteGate.Exit(); }
    }

    // Convenience: create tables if missing, mirroring do_setup() CREATE TABLE IF NOT EXISTS
    public static async Task<AtherizDbContext> CreateAndMigrateAsync(string savePath, CancellationToken ct = default)
    {
        var ctx = new AtherizDbContext(savePath);
        await ctx.EnsureCreatedAsync(ct);
        return ctx;
    }
}
