using System.Text.Json;
using System.Text.Json.Serialization;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

// ---------------------------------------------------------------------------
// MapHandler — mirrors atheriz/globals/map.py:MapHandler
// ---------------------------------------------------------------------------

/// <summary>
/// Persistence via EF Core JSON (replaces dill). ReaderWriterLockSlim mirrors RLock.
/// </summary>
public class MapHandler
{
    // Lock uses NoRecursion (no re-entrant path; snapshots used)
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    public ReaderWriterLockSlim SyncRoot => _lock;
    // Compat: keep public Lock for Ported tests (now delegates to private _lock); new code should use SyncRoot/ReadScope/WriteScope
    public ReaderWriterLockSlim Lock => _lock;
    public IDisposable ReadScope() { _lock.EnterReadLock(); return new LockScope(_lock, false); }
    public IDisposable WriteScope() { _lock.EnterWriteLock(); return new LockScope(_lock, true); }
    private readonly Dictionary<(string Area, int Z), MapInfo> _data = new();
    // Tombstones: keys removed via Clear() since the last successful save.
    // Save() deletes these rows in the same transaction as the upserts, so a
    // removed MapInfo cannot resurrect on the next Load. Guarded by Lock.
    private readonly HashSet<(string Area, int Z)> _removedSinceSave = new();
    private readonly AtherizSettings _settings;

    public MapHandler() : this(null, true) { }
    public MapHandler(AtherizSettings? settings, bool autoLoad = true)
    {
        _settings = settings ?? AtherizSettings.Global;
        if (autoLoad) Load();
    }
    public MapHandler(bool autoLoad) : this(null, autoLoad) { }

    public bool IsDirty()
    {
        // Snapshot refs under the handler lock, then probe each info outside it:
        // holding the handler read lock across O(N) info locks starves writers.
        List<MapInfo> infos;
        Lock.EnterReadLock();
        try { infos = _data.Values.ToList(); }
        finally { Lock.ExitReadLock(); }
        foreach (var mi in infos)
        {
            mi.Lock.EnterReadLock();
            try { if (mi.MapChanged || mi.IsModified) return true; }
            finally { mi.Lock.ExitReadLock(); }
        }
        return false;
    }

    public void Load()
    {
        try { Load(AtherizDbContextFactory.Create()); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed MapHandler.Load: " + logEx.Message, "MapHandler"); }
    }
    public void Load(AtherizDbContext db)
    {
        try
        {
            db.Database.EnsureCreated();
            Dictionary<(string, int), MapInfo> buffer = [];
            JsonTableLoader.LoadList(db.MapData, json => JsonSerializer.Deserialize<MapInfo.MapInfoPersistDto>(json, JsonOptions.Default), (dto, row) =>
            {
                // Per-row report: corrupt chunks are skipped, never silent.
                try
                {
                    var mi = dto!.ToDomain(_settings);
                    buffer[(row.Area, row.Z)] = mi;
                }
                catch (Exception ex)
                {
                    try { AtherizLogger.LogWarning($"[Load] skipping corrupt map chunk {row.Area}:{row.Z}: {ex.GetType().Name}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed MapHandler.Load: " + logEx.Message, "MapHandler"); }
                }
            });
            // the existence query runs BEFORE the write lock (all
            // readers blocked for the query duration otherwise). Only needed
            // when nothing usable loaded.
            bool dbHasRows = false;
            bool dbCheckFailed = false;
            if (buffer.Count == 0)
            {
                try { dbHasRows = db.MapData.AsNoTracking().Any(); }
                catch { dbCheckFailed = true; }
            }
            // Clear-then-swap (mirrors ObjectRegistry.LoadObjects): rows deleted
            // from the DB must not resurrect from memory on the next save.
            // But a failed load (all rows corrupt / I/O error) must preserve
            // live state: only an explicitly empty DB is a legitimate wipe.
            Lock.EnterWriteLock();
            try
            {
                bool shouldSwap = buffer.Count > 0 || (!dbCheckFailed && !dbHasRows);
                if (!shouldSwap)
                {
                    try { AtherizLogger.LogWarning("[Load] map load yielded no usable rows; preserving live map"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed MapHandler.Load: " + logEx.Message, "MapHandler"); }
                }
                else { _data.Clear(); foreach (var kv in buffer) _data[kv.Key] = kv.Value; _removedSinceSave.RemoveWhere(k => _data.ContainsKey(k)); }
            }
            finally { Lock.ExitWriteLock(); }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed MapHandler.Load: " + logEx.Message, "MapHandler"); }
    }

    /// <summary>
    /// Serializes one save snapshot DTO. Virtual so game code can customize
    /// snapshot encoding (e.g. compression); the default is plain JSON.
    /// Runs outside the DB gate — implementations must not take DbWriteGate.
    /// </summary>
    protected virtual string SerializeSnapshot(object dto) => JsonSerializer.Serialize(dto, JsonOptions.Default);

    public virtual void Save(bool force = false)
    {
        try { Save(AtherizDbContextFactory.Create(), force); }
        catch (Exception ex)
        {
            // Python: catches Exception around get_database and around save, restores flags
            // If Create throws (DB closed), ensure flags remain dirty (not cleared) – since we cleared optimistically inside Save(db), we need to restore
            // But if Create throws before Save(db) is entered, flags were not yet cleared, so nothing to restore
            // log instead of swallowing (map.py logs save errors),
            // so "skipped, still dirty" is distinguishable from "failed".
            try { AtherizLogger.LogWarning($"MapHandler.Save failed, flags left dirty: {ex.GetType().Name}: {ex.Message}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed MapHandler.Save: " + logEx.Message, "MapHandler"); }
        }
    }
    public virtual void Save(AtherizDbContext db, bool force = false)
    {
        // No separate IsDirty probe here: dirtiness is derived from the
        // snapshot below (per-item flags + tombstone sets). A probe-then-
        // snapshot gap drops tombstones (Clear dirties no surviving item,
        // so a false probe returns before the deletes are even read) and
        // misses late edits at shutdown's single non-force save.
        List<((string Area, int Z) Key, MapInfo Info)> refs;
        HashSet<(string Area, int Z)> deletes;
        using (ReadScope())
        {
            refs = _data.Select(kv => (kv.Key, kv.Value)).ToList();
            // A key re-added after Clear() (SetMapInfo/GetOrCreate) is live
            // again and must not be deleted.
            deletes = new HashSet<(string Area, int Z)>(_removedSinceSave);
            deletes.ExceptWith(_data.Keys);
        }

        List<((string Area, int Z) Key, MapInfo.MapInfoPersistDto Dto, MapInfo Original)> snapshot = [];
        List<MapInfo> cleared = [];
        List<((string Area, int Z) Key, string json)> jsons = [];

        void RestoreCleared()
        {
            foreach (var mi in cleared)
            {
                using (mi.WriteScope()) { mi.MapChanged = true; mi.IsModified = true; }
            }
        }

        try
        {
            foreach (var (k, mi) in refs)
            {
                bool wasChanged;
                using (mi.WriteScope())
                {
                    wasChanged = mi.MapChanged || mi.IsModified;
                    if (wasChanged)
                    {
                        mi.MapChanged = false;
                        mi.IsModified = false;
                        cleared.Add(mi);
                    }
                }

                // Skip unchanged maps before paying for the DTO snapshot.
                if (!force && !wasChanged && !_settings.AlwaysSaveAll && !ObjectRegistry.AlwaysSaveAll) continue;
                var dto = MapInfo.MapInfoPersistDto.FromDomain(mi);
                // if not changed we still snapshot for write if force? For non-force we only write changed.
                snapshot.Add((k, dto, mi));
            }

            if (snapshot.Count == 0 && deletes.Count == 0) return;

            // Serialize outside DB gate
            foreach (var (key, dto, _) in snapshot)
            {
                var json = SerializeSnapshot(dto);
                jsons.Add((key, json));
            }
        }
        catch
        {
            RestoreCleared();
            throw;
        }

        try
        {
            DbTransactionHelper.WithGateAndTransaction(db, ctx =>
            {
                foreach (var (key, json) in jsons)
                {
                    DbTransactionHelper.UpsertJson(ctx.MapData, () => ctx.MapData.Find(key.Area, key.Z), () => new MapDataRow { Area = key.Area, Z = key.Z }, json);
                }
                // Re-validate in-txn : a key removed then re-added
                // after the snapshot must not be deleted at commit.
                using (ReadScope()) { deletes.ExceptWith(_data.Keys); }
                foreach (var key in deletes)
                {
                    var row = ctx.MapData.Find(key.Area, key.Z);
                    if (row is not null) ctx.MapData.Remove(row);
                }
            }, onRollback: RestoreCleared);
        }
        catch (Exception ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing);
            // closed-DB races only — anything else propagates.
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping map save: {ex.Message}");
            RestoreCleared();
            return;
        }
        // Success path only (every failure path above returns or throws):
        // the tombstoned rows are now gone from the DB.
        if (deletes.Count > 0)
        {
            using (WriteScope()) { _removedSinceSave.ExceptWith(deletes); }
        }
    }

    public void SetMapInfo(string area, int z, MapInfo mapInfo)
    {
        using (WriteScope()) _data[(area, z)] = mapInfo;
    }

    public MapInfo? GetMapInfo(string area, int z)
    {
        using (ReadScope()) return _data.TryGetValue((area, z), out var mi) ? mi : null;
    }

    // atomic get-or-create. The old GetMapInfo→new→SetMapInfo sequence
    // let two concurrent creators for the same new area/Z both build and store,
    // last-wins, losing one's entries. Under one write hold the first stored
    // instance wins and every caller converges on it.
    public MapInfo GetOrAddMapInfo(string area, int z, MapInfo candidate)
    {
        using (WriteScope())
        {
            if (_data.TryGetValue((area, z), out var mi)) return mi;
            _data[(area, z)] = candidate;
            return candidate;
        }
    }

    /// <summary>
    /// Hot-reload rewire: swap stale listener/mapable instances for the replacement
    /// (same id). Snapshot infos under the handler lock, then replace under each
    /// info's own write scope (never nested) — mirrors MoveListener lock discipline.
    /// </summary>
    public void ReplaceMapEntries(int id, GameObject replacement)
    {
        List<MapInfo> infos;
        using (ReadScope()) infos = _data.Values.ToList();
        foreach (var mi in infos)
            mi.ReplaceEntry(id, replacement);
    }

    private MapInfo GetOrCreate(string area, int z)
    {
        Lock.EnterUpgradeableReadLock();
        try
        {
            if (!_data.TryGetValue((area, z), out var mi))
            {
                Lock.EnterWriteLock();
                try
                {
                    if (!_data.TryGetValue((area, z), out mi))
                    {
                        mi = new MapInfo { Name = area, Settings = _settings };
                        _data[(area, z)] = mi;
                    }
                }
                finally { Lock.ExitWriteLock(); }
            }
            return mi;
        }
        finally { Lock.ExitUpgradeableReadLock(); }
    }

    [Obsolete("Use EnsureMapInfo: identical lookup, one name going forward.")]
    public MapInfo GetOrCreatePublic(string area, int z) => GetOrCreate(area, z);
    public MapInfo EnsureMapInfo(string area, int z) => GetOrCreate(area, z);

    private static Coord? ExtractCoord(GameObject obj)
    {
        var loc = obj.Location;
        if (loc is Persistence.Dto.LocationRef.CoordLocation cl) return cl.Coord;
        if (loc is Persistence.Dto.LocationRef.ObjectLocation ol)
        {
            var target = ObjectRegistry.GetSingle(ol.ObjectId);
            if (target is Node n) return n.Coord;
        }
        // fallback: if GameObject is Node itself
        if (obj is Node node) return node.Coord;
        return null;
    }

    public void AddMapable(GameObject mapable, bool notify = false)
    {
        var coord = ExtractCoord(mapable);
        if (coord is null) return;
        var mi = GetOrCreate(coord.Value.Area, coord.Value.Z);
        mi.AddMapable(mapable, notify);
    }

    public void AddListener(GameObject listener, bool notify = false)
    {
        var coord = ExtractCoord(listener);
        if (coord is null) return;
        var mi = GetOrCreate(coord.Value.Area, coord.Value.Z);
        mi.AddListener(listener, notify);
    }

    public void RemoveListener(GameObject listener)
    {
        var coord = ExtractCoord(listener);
        if (coord is null) return;
        MapInfo? mi;
        using (ReadScope()) { _data.TryGetValue((coord.Value.Area, coord.Value.Z), out mi); }
        mi?.RemoveListener(listener);
    }

    // Shared move resolve: snapshot fromMap under the handler read lock,
    // release, then GetOrCreate the destination. Covers snapshot + create
    // only; add/remove/render/SendUnbackground stay per caller. The read hold
    // is never carried across the create or any info lock (snapshot-then-mutate).
    private void ResolveMoveMaps(Coord? fromCoord, Coord toCoord, out MapInfo? fromMap, out MapInfo toMap)
    {
        using (ReadScope())
        {
            fromMap = null;
            if (fromCoord is not null)
                _data.TryGetValue((fromCoord.Value.Area, fromCoord.Value.Z), out fromMap);
        }
        toMap = GetOrCreate(toCoord.Area, toCoord.Z);
    }

    public void MoveListener(GameObject listener, Coord toCoord, Coord? fromCoord = null)
    {
        bool areaChanged = fromCoord is not null && (fromCoord.Value.Area != toCoord.Area || fromCoord.Value.Z != toCoord.Z);
        ResolveMoveMaps(fromCoord, toCoord, out var fromMap, out var toMap);
        fromMap?.RemoveListener(listener);
        toMap.AddListener(listener);
        if (areaChanged) SendUnbackground(listener);
        fromMap?.Render(false);
        toMap?.Render(true);
    }

    private static void SendUnbackground(GameObject listener)
    {
        MapInfo.Suppress(() => { listener.Session?.Connection?.SendCommand("unbackground", new List<object?> { "" }, null); }, "SendUnbackground");
    }

    public void MoveMapable(GameObject mapable, Coord toCoord, Coord? fromCoord = null)
    {
        if (fromCoord is not null && fromCoord.Value.Area == toCoord.Area && fromCoord.Value.Z == toCoord.Z)
        {
            var cur = GetOrCreate(toCoord.Area, toCoord.Z);
            cur.AddMapable(mapable);
            cur.Render(true);
            return;
        }
        ResolveMoveMaps(fromCoord, toCoord, out var fromMap, out var toMap);
        fromMap?.RemoveMapable(mapable);
        toMap.AddMapable(mapable);
        fromMap?.Render(false);
        toMap?.Render(true);
    }

    public void MoveListenerAndMapable(GameObject obj, Coord toCoord, Coord? fromCoord = null)
    {
        if (fromCoord is not null && fromCoord.Value.Area == toCoord.Area && fromCoord.Value.Z == toCoord.Z)
        {
            var cur = GetOrCreate(toCoord.Area, toCoord.Z);
            cur.AddListenerAndMapable(obj);
            cur.RenderLegend();
            cur.Render(true);
            return;
        }
        bool areaChanged = fromCoord is not null && (fromCoord.Value.Area != toCoord.Area || fromCoord.Value.Z != toCoord.Z);
        // Snapshot under the handler lock, then mutate via each MapInfo's own
        // lock: the old code wrote fromMap/toMap.Listeners/Objects directly
        // while holding only the handler lock, racing Render/AddMapable which
        // take the info lock (lost entries / torn dictionaries).
        ResolveMoveMaps(fromCoord, toCoord, out var fromMap, out var toMap);
        if (fromMap is not null && !ReferenceEquals(fromMap, toMap))
        {
            fromMap.RemoveListener(obj);
            fromMap.RemoveMapable(obj);
        }
        toMap.AddListener(obj);
        toMap.AddMapable(obj, false);
        if (areaChanged) SendUnbackground(obj);
        if (fromMap is not null && !ReferenceEquals(fromMap, toMap))
        {
            fromMap.RenderLegend();
            fromMap.Render(false);
        }
        toMap.RenderLegend();
        toMap.Render(true);
    }

    public void RemoveMapable(GameObject mapable, string fromArea, int fromZ)
    {
        MapInfo? fromMap;
        using (ReadScope()) { _data.TryGetValue((fromArea, fromZ), out fromMap); }
        fromMap?.RemoveMapable(mapable);
    }

    public void Clear()
    {
        using (WriteScope())
        {
            foreach (var k in _data.Keys) _removedSinceSave.Add(k);
            _data.Clear();
        }
    }

    public IReadOnlyDictionary<(string Area, int Z), MapInfo> Snapshot()
    {
        using (ReadScope()) { return new Dictionary<(string, int), MapInfo>(_data); }
    }
}
