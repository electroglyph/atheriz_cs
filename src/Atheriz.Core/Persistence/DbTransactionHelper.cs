using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atheriz.Core.Persistence;

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
            // Gate the ambient path too: the old early return ran
            // work+SaveChanges with zero exclusion, so two ambient-txn
            // callers ran concurrently despite the gate contract. Re-entrant
            // on the owning flow (the checkpoint holds it outside), bounded
            // fail-loud otherwise — same shape as below, minus BeginTransaction.
            if (!DbWriteGate.TryEnter(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("DbWriteGate held for over 30s; refusing to hang the save.");
            try
            {
                work(db);
                db.SaveChanges();
            }
            catch
            {
                try { onRollback?.Invoke(); } catch (Exception) { }
                throw;
            }
            finally
            {
                DbWriteGate.Exit();
            }
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
                    RollbackQuiet(tx);
                    // Clean tracker per attempt : retrying work() +
                    // SaveChanges() on the same context with a dirty tracker
                    // throws already-tracked / re-inserts the same key instead
                    // of a clean retry. work() re-fetches everything via Find.
                    db.ChangeTracker.Clear();
                    AtherizLogger.LogDebug($"Suppressed DbTransactionHelper.WithGateAndTransaction SQLITE_BUSY retry {attempt}: {ex.Message}", "DbTransactionHelper");
                }
                catch
                {
                    RollbackQuiet(tx);
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

    /// <summary>Best-effort rollback; a rollback failure never masks the save error under handling.</summary>
    private static void RollbackQuiet(IDbContextTransaction tx)
    {
        try { tx.Rollback(); } catch (Exception) { }
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
