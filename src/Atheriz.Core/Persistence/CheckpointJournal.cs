using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
