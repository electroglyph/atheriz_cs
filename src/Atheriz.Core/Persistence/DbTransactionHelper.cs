// Port of atheriz/database_setup.py:Database.lock RLock scaffold + do_setup transaction
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Crash-consistency journal for multi-table checkpoints.
/// A full checkpoint marks the row dirty BEFORE writing tables and clean AFTER
/// all tables commit. A dirty row at startup means the previous checkpoint died
/// mid-way and tables may be torn. Single-table saves never touch the journal.
/// The journal itself must never break a save: all methods swallow errors.
/// </summary>
public static class CheckpointJournal
{
    private const int RowId = 0;

    private static string ResolveDefaultPath() =>
        AtherizDbContextFactory.ResolveSavePath(AtherizSettings.Global);

    /// <summary>Dirty-mark; a null path uses the default-path resolution (same as parameterless factory/saves).</summary>
    public static void MarkDirty(string? savePath = null) => Mark(savePath ?? ResolveDefaultPath(), "dirty");

    /// <summary>Clean-mark; a null path uses the default-path resolution (same as parameterless factory/saves).</summary>
    public static void MarkClean(string? savePath = null) => Mark(savePath ?? ResolveDefaultPath(), "clean");

    /// <summary>Dirty-check; a null path uses the default-path resolution (same as parameterless factory/saves).
    /// True when a previous checkpoint died mid-way. Missing row (first boot) counts as clean.
    /// errors fail loud (the StartStop caller logs them); a full disk,
    /// read-only file, or torn table must never read back as "clean".</summary>
    public static bool IsDirty(string? savePath = null)
    {
        savePath ??= ResolveDefaultPath();
        DbWriteGate.Enter();
        try
        {
            using var db = AtherizDbContextFactory.Create(savePath);
            db.Database.EnsureCreated();
            var row = db.Checkpoints.Find(RowId);
            return row is not null && row.State == "dirty";
        }
        finally { DbWriteGate.Exit(); }
    }

    private static void Mark(string savePath, string state)
    {
        // Gated: the mark is part of the checkpoint write it brackets, and an
        // un-gated EnsureCreated+upsert here used to race concurrent saves
        // into SQLITE_BUSY. Re-entrant when called under an outer save gate.
        DbWriteGate.Enter();
        try
        {
            using var db = AtherizDbContextFactory.Create(savePath);
            db.Database.EnsureCreated();
            Upsert(db, state);
        }
        catch (Exception ex) { try { Console.Error.WriteLine($"checkpoint journal {state}-mark failed: {ex.Message}"); } catch (Exception) { } }
        finally { DbWriteGate.Exit(); }
    }

    private static void Upsert(AtherizDbContext db, string state)
    {
        var row = db.Checkpoints.Find(RowId);
        if (row is null)
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
        // Ambient-transaction re-entrancy: a caller that already opened a transaction
        // on this context (e.g. InitialSetup's single-transaction seed) runs inline —
        // a second BeginTransaction on the same connection throws. The outer
        // transaction owns atomicity; SaveChanges joins it. (Caller must EnsureCreated.)
        if (db.Database.CurrentTransaction is not null)
        {
            work(db);
            db.SaveChanges();
            return;
        }
        // Bounded take: a stuck holder fails loud instead of hanging all saves forever.
        if (!DbWriteGate.TryEnter(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("DbWriteGate held for over 30s; refusing to hang the save.");
        try
        {
            db.Database.EnsureCreated();
            // Engine-level busy_timeout=5000 is the primary contention mechanism;
            // this bounded retry covers residual SQLITE_BUSY/LOCKED races with
            // un-gated readers. Non-busy failures throw immediately.
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                using var tx = db.Database.BeginTransaction();
                try
                {
                    work(db);
                    db.SaveChanges();
                    tx.Commit();
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsBusyConflict(ex))
                {
                    try { tx.Rollback(); } catch (Exception) { }
                    // Clean tracker per attempt : retrying work() +
                    // SaveChanges() on the same context with a dirty tracker
                    // throws already-tracked / re-inserts the same key instead
                    // of a clean retry. work() re-fetches everything via Find.
                    db.ChangeTracker.Clear();
                    AtherizLogger.LogDebug($"Suppressed DbTransactionHelper.WithGateAndTransaction SQLITE_BUSY retry {attempt}: {ex.Message}", "DbTransactionHelper");
                }
                catch
                {
                    try { tx.Rollback(); } catch (Exception) { }
                    try { onRollback?.Invoke(); } catch (Exception) { }
                    throw;
                }
            }
        }
        finally
        {
            DbWriteGate.Exit();
        }
    }

    /// <summary>True when <paramref name="ex"/> (or any inner) is SQLITE_BUSY (5) or SQLITE_LOCKED (6).</summary>
    internal static bool IsBusyConflict(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is Microsoft.Data.Sqlite.SqliteException se
                && (se.SqliteErrorCode == 5 || se.SqliteErrorCode == 6))
                return true;
        }
        return false;
    }

    /// <summary>Generic upsert for any <see cref="IJsonEntity"/> row: Find → update Data else Add,
    /// with optional extra configuration (e.g., <c>Type</c> discriminator on <c>ObjectRow</c>).</summary>
    public static void UpsertJson<T>(DbSet<T> set, Func<T?> find, Func<T> create, string json, Action<T>? configure = null)
        where T : class, IJsonEntity
    {
        var existing = find();
        if (existing is not null)
        {
            existing.Data = json;
            configure?.Invoke(existing);
        }
        else
        {
            var row = create();
            row.Data = json;
            configure?.Invoke(row);
            set.Add(row);
        }
    }
}
