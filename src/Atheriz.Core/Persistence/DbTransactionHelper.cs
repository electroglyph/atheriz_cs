// Port of atheriz/database_setup.py:Database.lock RLock scaffold + do_setup transaction
using Microsoft.EntityFrameworkCore;

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
