using System.Diagnostics;
using Atheriz.Core.Globals;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// One checkpoint shape for every save entry: a single
/// context with a single transaction covering objects, map, node, and time.
/// Each section still logs its own failure; the transaction commits only when
/// every section succeeded, so a crash never tears between tables. Section
/// failures roll back the healthy rows too — most failures are DB-level and
/// hit all sections alike, and a torn partial would leave dangling references
/// (object Location rows vs map rows). The journal stays dirty on any failure
/// so boot reports the torn state loudly.
/// </summary>
public static class CheckpointWriter
{
    public sealed record SectionFailure(string Name, string Message);

    /// <summary>
    /// Runs the full checkpoint. Null map/nodes resolve from the live
    /// singletons; time saves only when <paramref name="includeTime"/> is set
    /// and a handler is available. Returns the failed sections (empty = clean).
    /// </summary>
    public static List<SectionFailure> WriteCheckpoint(
        AtherizSettings settings,
        string caller,
        MapHandler? map = null,
        NodeHandler? nodes = null,
        GameTime? time = null,
        bool includeTime = true,
        bool forceNodes = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string savePath = AtherizDbContextFactory.ResolveSavePath(settings);
        CheckpointJournal.MarkDirty(savePath);
        List<SectionFailure> failures = [];
        var sw = Stopwatch.StartNew();
        // The gate is held for the whole checkpoint: the inner saves re-enter
        // through the re-entrant gate (AsyncLocal holder count), so without
        // this a concurrent tick could interleave mid-checkpoint. Bounded
        // take: on timeout the checkpoint still runs without the atomic
        // transaction (degraded mode; the journal still reports failures).
        bool atomic = DbWriteGate.TryEnter(TimeSpan.FromSeconds(30));
        if (!atomic)
            AtherizLogger.LogWarning($"checkpoint {caller}: gate busy; saving without atomic transaction (journal still detects).");
        try
        {
            using var db = AtherizDbContextFactory.CreateForSettings(settings);
            db.Database.EnsureCreated();
            if (atomic)
            {
                using var tx = db.Database.BeginTransaction();
                RunSections(db, failures, caller, map, nodes, time, includeTime, forceNodes);
                if (failures.Count == 0) tx.Commit();
                // !ok: dispose uncommitted = rollback; the journal stays dirty.
            }
            else RunSections(db, failures, caller, map, nodes, time, includeTime, forceNodes);
        }
        catch (Exception ex)
        {
            failures.Add(new SectionFailure("context", ex.Message));
            AtherizLogger.LogError($"checkpoint {caller} context failed:\n{ex}");
        }
        finally { if (atomic) DbWriteGate.Exit(); }
        sw.Stop();
        AtherizLogger.LogInformation($"checkpoint {caller} held the gate {sw.Elapsed.TotalMilliseconds} milliseconds ({failures.Count} failures).");
        if (failures.Count == 0) CheckpointJournal.MarkClean(savePath);
        return failures;
    }

    private static void RunSections(
        AtherizDbContext db,
        List<SectionFailure> failures,
        string caller,
        MapHandler? map,
        NodeHandler? nodes,
        GameTime? time,
        bool includeTime,
        bool forceNodes)
    {
        RunSection(db, failures, caller, "objects", h => ObjectRegistry.SaveObjects(h));
        // Map/node saves get one same-context retry: a separate context would
        // hit SQLITE_BUSY against this open transaction (and break the very
        // atomicity the checkpoint exists for). The tracker is cleared first —
        // a failed SaveChanges may have left it poisoned; the save re-fetches
        // via Find. Objects are not wrapped: SaveObjects retries internally.
        RunSection(db, failures, caller, "map",
            h => Resolve(map, GlobalServices.GetMapHandler).Save(h),
            h => Resolve(map, GlobalServices.GetMapHandler).Save(h));
        RunSection(db, failures, caller, "nodes",
            h => Resolve(nodes, GlobalServices.GetNodeHandler).Save(h, forceNodes),
            h => Resolve(nodes, GlobalServices.GetNodeHandler).Save(h, forceNodes));
        if (includeTime)
            RunSection(db, failures, caller, "time",
                h => Resolve(time, GlobalServices.GetGameTime).Save(h));
    }

    private static T Resolve<T>(T? cached, Func<T> getter) where T : class
        => cached ?? getter();

    private static void RunSection(AtherizDbContext db, List<SectionFailure> failures, string caller, string name, Action<AtherizDbContext> save, Action<AtherizDbContext>? retry = null)
    {
        try { save(db); }
        catch (Exception ex)
        {
            if (retry is null)
            {
                failures.Add(new SectionFailure(name, ex.Message));
                AtherizLogger.LogError($"checkpoint {caller}: {name} failed:\n{ex}");
                return;
            }
            try
            {
                db.ChangeTracker.Clear();
                retry(db);
            }
            catch (Exception retryEx)
            {
                failures.Add(new SectionFailure(name, retryEx.Message));
                AtherizLogger.LogError($"checkpoint {caller}: {name} failed:\n{retryEx}");
            }
        }
    }
}
