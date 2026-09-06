// Port of atheriz/database_setup.py:Database.lock RLock scaffold + do_setup transaction
using Atheriz.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Crash-consistency journal for multi-table checkpoints (audit A7).
/// A full checkpoint marks the row dirty BEFORE writing tables and clean AFTER
/// all tables commit. A dirty row at startup means the previous checkpoint died
/// mid-way and tables may be torn. Single-table saves never touch the journal.
/// The journal itself must never break a save: all methods swallow errors.
/// </summary>
public static class CheckpointJournal
{
    private const int RowId = 0;

    private static string ResolveDefaultPath() =>
        Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? AtherizSettings.Global.SavePath;

    /// <summary>Dirty-mark using the same default-path resolution as parameterless factory/saves.</summary>
    public static void MarkDirty() => MarkDirty(ResolveDefaultPath());

    /// <summary>Clean-mark using the same default-path resolution as parameterless factory/saves.</summary>
    public static void MarkClean() => MarkClean(ResolveDefaultPath());

    /// <summary>Dirty-check using the same default-path resolution as parameterless factory/saves.</summary>
    public static bool IsDirty() => IsDirty(ResolveDefaultPath());

    public static void MarkDirty(string savePath)
    {
        try
        {
            using var db = AtherizDbContextFactory.Create(savePath);
            db.Database.EnsureCreated();
            Upsert(db, "dirty");
        }
        catch (Exception ex) { try { Console.Error.WriteLine($"checkpoint journal dirty-mark failed: {ex.Message}"); } catch { } }
    }

    public static void MarkClean(string savePath)
    {
        try
        {
            using var db = AtherizDbContextFactory.Create(savePath);
            db.Database.EnsureCreated();
            Upsert(db, "clean");
        }
        catch (Exception ex) { try { Console.Error.WriteLine($"checkpoint journal clean-mark failed: {ex.Message}"); } catch { } }
    }

    /// <summary>True when a previous checkpoint died mid-way. Missing row/table (first boot) counts as clean.</summary>
    public static bool IsDirty(string savePath)
    {
        try
        {
            using var db = AtherizDbContextFactory.Create(savePath);
            db.Database.EnsureCreated();
            var row = db.Checkpoints.Find(RowId);
            return row != null && row.State == "dirty";
        }
        catch { return false; }
    }

    private static void Upsert(AtherizDbContext db, string state)
    {
        var row = db.Checkpoints.Find(RowId);
        if (row == null)
            db.Checkpoints.Add(new Entities.CheckpointRow
            {
                Id = RowId,
                State = state,
                Token = Guid.NewGuid().ToString("N"),
                SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
        else
        {
            row.State = state;
            row.Token = Guid.NewGuid().ToString("N");
            row.SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        db.SaveChanges();
    }
}

/// <summary>
/// Deduplicates the 4× (plus ObjectRegistry) <c>DbWriteGate.Enter / EnsureCreated / BeginTransaction / SaveChanges / Commit / Rollback / Exit</c>
/// scaffold from <c>NodeHandler.Save</c>, <c>MapHandler.Save</c>, <c>GameTime.Save</c>,
/// <c>ObjectRegistry.SaveObjects/DeleteObjects</c>.
/// Also hosts generic <c>UpsertJson</c> for the 6× Find→Update/Add pattern.
/// </summary>
public static class DbTransactionHelper
{
    /// <summary>
    /// Executes <paramref name="work"/> under <see cref="DbWriteGate"/> with <c>EnsureCreated</c>,
    /// a transaction, <c>SaveChanges</c> and commit; on exception rolls back, invokes <paramref name="onRollback"/>
    /// (used to re-mark dirty flags), and rethrows.
    /// Mirrors Python <c>with db.lock: BEGIN TRANSACTION ... COMMIT/ROLLBACK</c>.
    /// </summary>
    public static void WithGateAndTransaction(AtherizDbContext db, Action<AtherizDbContext> work, Action? onRollback = null)
    {
        DbWriteGate.Enter();
        try
        {
            db.Database.EnsureCreated();
            using var tx = db.Database.BeginTransaction();
            try
            {
                work(db);
                db.SaveChanges();
                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                try { onRollback?.Invoke(); } catch { }
                throw;
            }
        }
        finally
        {
            DbWriteGate.Exit();
        }
    }

    /// <summary>Generic upsert for any <see cref="IJsonEntity"/> row: Find → update Data else Add.</summary>
    public static void UpsertJson<T>(DbSet<T> set, Func<T?> find, Func<T> create, string json)
        where T : class, IJsonEntity
    {
        var existing = find();
        if (existing != null)
            existing.Data = json;
        else
        {
            var row = create();
            row.Data = json;
            set.Add(row);
        }
    }

    /// <summary>Upsert with extra configuration (e.g., <c>Type</c> discriminator on <c>ObjectRow</c>).</summary>
    public static void UpsertJson<T>(DbSet<T> set, Func<T?> find, Func<T> create, string json, Action<T> configure)
        where T : class, IJsonEntity
    {
        var existing = find();
        if (existing != null)
        {
            existing.Data = json;
            configure(existing);
        }
        else
        {
            var row = create();
            row.Data = json;
            configure(row);
            set.Add(row);
        }
    }
}
