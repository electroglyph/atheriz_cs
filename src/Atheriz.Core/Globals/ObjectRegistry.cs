using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

/// <summary>
/// Global object registry: id -> object world state plus the save/delete
/// plumbing. Classification state (IP bans, creation cooldowns) lives in
/// IpBanStore / CreationCooldownStore; the registry forwards for call-site
/// continuity.
/// All access guarded by a single non-reentrant <see cref="Lock"/>.
/// Persistence via <see cref="AtherizDbContext"/> + JSON (replaces dill).
/// </summary>
public static class ObjectRegistry
{
    // --- state ---
    // Single non-reentrant hold: every take below is a leaf (enter, touch the
    // dicts, exit) — no path takes AllLock twice, so recursion is never
    // needed. Uniqueness predicates (AddObjectUnique) must only read object
    // properties, never touch the registry.
    internal static readonly Lock AllLock = new();
    private static readonly Dictionary<int, GameObject> AllObjects = new();

    // reverse index (reference -> key) so re-keying an already
    // registered reference is O(1). Replaces the old per-insert O(n) scan
    // for stale keys. Reference identity (never Id equality) — the same
    // reference may be re-registered under a new Id after reload/rollback.
    private sealed class ReferenceComparer : IEqualityComparer<GameObject>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(GameObject? a, GameObject? b) => ReferenceEquals(a, b);
        public int GetHashCode(GameObject o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
    private static readonly Dictionary<GameObject, int> _keysByRef = new(ReferenceComparer.Instance);

    // Tombstone journal for game deletes: Delete() unregisters the object
    // immediately (in-memory authority) but the DB row can only die at a
    // checkpoint (DB discipline: writes happen on save). SaveObjects only
    // upserts live members, so without this the row survives and the object
    // resurrects on the next load. Drained by SaveObjects; dropped by
    // ClearAll/LoadObjects (world replacement discards pending intent).
    // Guarded by AllLock like AllObjects.
    private static readonly HashSet<int> _pendingDeletions = new();

    /// <summary>
    /// Journals a deleted non-temporary id so the next checkpoint removes
    /// its row. Called at every site that builds a delete op.
    /// </summary>
    public static void NoteDeleted(int id)
    {
        lock (AllLock) { _pendingDeletions.Add(id); }
    }

    // All callers hold AllLock write.
    private static void IndexInsert(GameObject obj)
    {
        if (_keysByRef.TryGetValue(obj, out var oldKey) && oldKey != obj.Id)
            AllObjects.Remove(oldKey);
        // Overwriting a different live occupant must also drop that
        // occupant's reverse entry, or a later RemoveObject(victim) would
        // still see it as indexed at this id.
        if (AllObjects.TryGetValue(obj.Id, out var prev) && !ReferenceEquals(prev, obj))
            _keysByRef.Remove(prev);
        AllObjects[obj.Id] = obj;
        _keysByRef[obj] = obj.Id;
    }

    public static bool AlwaysSaveAll { get; set; } = false;

    // --- bans (canonical home: IpBanStore) ---
    public static bool IsIpBanned(string host, double? now = null) => IpBanStore.IsIpBanned(host, now);
    public static void BanIp(string host, double? expires = null) => IpBanStore.BanIp(host, expires);
    public static void UnbanIp(string host) => IpBanStore.UnbanIp(host);

    // --- creation cooldowns (canonical home: CreationCooldownStore; unified per host, ? bypass) ---
    public static bool CreationCooldownActive(string host, double now) =>
        CreationCooldownStore.CreationCooldownActive(host, now);
    public static void ApplyCreationCooldown(string op, string host, double now, double cooldown) =>
        CreationCooldownStore.ApplyCreationCooldown(op, host, now, cooldown);
    public static bool TryReserveCreationCooldown(string op, string host, double now, double cooldown) =>
        CreationCooldownStore.TryReserveCreationCooldown(op, host, now, cooldown);
    public static void ClearCreationCooldown(string host) =>
        CreationCooldownStore.ClearCreationCooldown(host);

    // --- failed login map exposed for parity ---
    public static BoundedDictionary<string, int> FailedLogins => IpBanStore.FailedLogins;

    // --- core registry ---
    public static List<GameObject> FilterBy(Func<GameObject, bool> predicate)
    {
        List<GameObject> snap;
        lock (AllLock) { snap = AllObjects.Values.ToList(); }
        return snap.Where(predicate).ToList();
    }

    // coord→node resolution. Prefers the O(1) NodeHandler area/grid/(x,y)
    // index; falls back to a registry scan for nodes not grafted into any grid
    // (fresh `new Node` + AddObject with no handler/grid, as in tests). Single
    // choke point so callers never hand-roll full-registry node scans.
    public static Node? FindNodeByCoord(Coord coord)
    {
        try
        {
            var indexed = NodeHandler.GetCurrent()?.TryGetNodeAt(coord);
            if (indexed is not null) return indexed;
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ObjectRegistry.FindNodeByCoord index lookup: " + logEx.Message, "ObjectRegistry"); }
        List<GameObject> snap;
        lock (AllLock) { snap = AllObjects.Values.ToList(); }
        foreach (var o in snap)
            if (o is Node n && n.Coord.Equals(coord)) return n;
        return null;
    }

    public static List<GameObject> GetByTag(object tag, bool all = false)
    {
        // (TypeError → ArgumentException per BCL convention) instead of
        // silently matching everything (empty-set subset is vacuously true).
        HashSet<string> tags = tag switch
        {
            string s => [s],
            IEnumerable<string> e => new HashSet<string>(e),
            _ => throw new ArgumentException($"tag must be a string or list/set of strings, got {tag?.GetType().Name ?? "null"}")
        };
        if (all)
            return FilterBy(o => tags.IsSubsetOf(o.TagsSnapshot));
        return FilterBy(o => tags.Overlaps(o.TagsSnapshot));
    }

    public static List<GameObject> Get(int id)
    {
        lock (AllLock) { return AllObjects.TryGetValue(id, out var o) ? [o] : []; }
    }
    public static List<GameObject> Get(IEnumerable<int> ids)
    {
        // materialize the caller enumerable BEFORE the read lock —
        // a lazy enumerable would execute arbitrary caller code under AllLock.
        var list = ids.ToList();
        lock (AllLock)
        {
            List<GameObject> res = new(list.Count);
            foreach (var id in list)
                if (AllObjects.TryGetValue(id, out var o) && o is not null)
                    res.Add(o);
            return res;
        }
    }

    /// <summary>
    /// Single-id fetch core shared by the per-command <c>Get(id).FirstOrDefault()</c>
    /// sites. Returns the object or null — the same "not found" outcome each site
    /// already expects — with one dictionary lookup and no list allocation.
    /// Deliberately command-agnostic (id in, object-or-null out) so script and
    /// tick paths can share it too.
    /// Keep <see cref="Get(int)"/> for list-shaped callers.
    /// </summary>
    public static bool TryGetSingle(int id, out GameObject? obj)
    {
        lock (AllLock) { return AllObjects.TryGetValue(id, out obj); }
    }

    /// <summary>
    /// Single-id fetch returning the object or null (no list alloc).
    /// </summary>
    public static GameObject? GetSingle(int id)
        => TryGetSingle(id, out var o) ? o : null;

    public static void AddObject(GameObject obj)
    {
        lock (AllLock)
        {
            IndexInsert(obj);
        }
    }

    public static GameObject? GetEver(int id)
    {
        // Live-map only (F005): the old EverCreated strong-ref cache leaked memory and
        // resurrected ClearAll'd objects. Callers needing a stale object must hold their own ref.
        lock (AllLock) { return AllObjects.TryGetValue(id, out var o) ? o : null; }
    }

    public static void AddObjectUnique(GameObject obj, Func<GameObject, bool> predicate, string error)
    {
        // Single hold covering check+insert (F005) — no read-check/write-recheck spin.
        // Safe: uniqueness predicates only read object properties (no registry
        // re-entry — the hold is non-reentrant, so a registry-touching
        // predicate would deadlock instead of merely racing).
        lock (AllLock)
        {
            if (AllObjects.Values.Any(predicate)) throw new InvalidOperationException(error);
            IndexInsert(obj);
        }
    }

    public static void RemoveObject(GameObject obj)
    {
        lock (AllLock)
        {
            // Remove the live entry only when it is this object — the id
            // may have been overwritten by a different occupant since.
            if (AllObjects.TryGetValue(obj.Id, out var cur) && ReferenceEquals(cur, obj))
                AllObjects.Remove(obj.Id);
            // Drop the reverse entry only if it points at the removed key —
            // AllObjects[obj.Id] may have been a different occupant.
            if (_keysByRef.TryGetValue(obj, out var k) && k == obj.Id) _keysByRef.Remove(obj);
        }
    }

    public static void ClearAll()
    {
        // Hold both AllLock and IdGenerator lock to prevent duplicate Id race with concurrent GetUniqueId
        lock (IdGenerator.LockObj)
        {
            lock (AllLock)
            {
                AllObjects.Clear();
                _keysByRef.Clear();
                _pendingDeletions.Clear();
                // Already holding IdGenerator.LockObj; SetId is safe (Monitor is re-entrant).
                IdGenerator.SetId(-1);
            }
        }
        IpBanStore.Clear();
        CreationCooldownStore.Clear();
    }

    public static int Count
    {
        get { lock (AllLock) { return AllObjects.Count; } }
    }

    private static bool IsStillSaveable(GameObject obj, bool forSave = false, bool force = false)
    {
        var id = obj.Id;
        // lock coupling — the obj read lock is taken BEFORE AllLock
        // is released, so the registry-membership check and the flag reads
        // are atomic (no evict/delete/re-key can interleave). The coupling is
        // one scope, not nesting: the obj lock is taken inside the AllLock
        // hold and outlives it, and callers never hold obj locks into this
        // method.
        lock (AllLock)
        {
            if (!AllObjects.TryGetValue(id, out var cur) || !ReferenceEquals(cur, obj)) return false;
            obj.SyncRoot.EnterReadLock();
        }
        // need obj lock
        try
        {
            if (obj.IsDeleted) return false;
            if (obj.IsTemporary) return false;
            if (forSave && !AlwaysSaveAll && !force && !obj.IsModified) return false;
        }
        finally { obj.SyncRoot.ExitReadLock(); }
        return true;
    }

    // --- persistence ---

    public static void LoadObjects(AtherizDbContext db)
    {
        Dictionary<int, GameObject> objects = [];
        var maxId = -1;
        try
        {
            // Buffer rows first (no locks held during deserialization), then
            // convert row by row. Corrupt rows are SKIPPED but REPORTED with
            // their row id — never silently dropped.
            var rows = JsonTableLoader.LoadRows(db.Objects);
            foreach (var row in rows)
            {
                Persistence.Dto.GameObjectDto? dto;
                try
                {
                    dto = GameObjectDtoSerializer.Migrate(GameObjectDtoSerializer.FromJson(row.Data));
                }
                catch (Exception ex)
                {
                    try { AtherizLogger.LogWarning($"[Load] skipping corrupt object row {row.Id}: {ex.GetType().Name}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.LoadObjects: " + logEx.Message, "BoundedDictionary"); }
                    continue;
                }
                if (dto is null)
                {
                    try { AtherizLogger.LogWarning($"[Load] skipping null object row {row.Id}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.LoadObjects: " + logEx.Message, "BoundedDictionary"); }
                    continue;
                }
                try
                {
                    var obj = GameObject.FromDto(dto);
                    objects[row.Id] = obj;
                    if (row.Id > maxId) maxId = row.Id;
                }
                catch (Exception ex)
                {
                    try { AtherizLogger.LogWarning($"[Load] skipping unrestorable object row {row.Id}: {ex.GetType().Name}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.LoadObjects: " + logEx.Message, "BoundedDictionary"); }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing).
            RunClosedDbGuard(ex, "load");
            AtherizLogger.LogWarning("database closed");
            return;
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            RunClosedDbGuard(ex, "load");
            return;
        }
        if (objects.Count == 0)
        {
            // Zero usable rows must never wipe the live world (corrupt or
            // unreadable rows all skipped above; empty table on a running
            // server likewise keeps live state). Mirrors MapHandler.Load /
            // NodeHandler.Load preserving live on zero usable rows.
            try { AtherizLogger.LogWarning("[Load] object load yielded no usable rows; preserving live registry"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.LoadObjects: " + logEx.Message, "BoundedDictionary"); }
            return;
        }
        // Swap + id watermark atomically under both locks: a concurrent
        // GetUniqueId between the swap and SetId could otherwise hand out an
        // id that a row then overwrites.
        lock (IdGenerator.LockObj)
        {
            lock (AllLock)
            {
                AllObjects.Clear();
                _keysByRef.Clear();
                // World replacement: pending delete intent belongs to the
                // discarded world (un-saved deletes come back with the load).
                _pendingDeletions.Clear();
                foreach (var kv in objects) { AllObjects[kv.Key] = kv.Value; _keysByRef[kv.Value] = kv.Key; }
            }

            if (maxId > IdGenerator.GetId())
                IdGenerator.SetId(maxId);
        }

        List<GameObject> snap;
        lock (AllLock) { snap = AllObjects.Values.ToList(); }
        foreach (var o in snap)
        {
            try { o.ResolveRelations(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.LoadObjects: " + logEx.Message, "BoundedDictionary"); }
        }
    }

    // Closed-DB guard: the IsClosed flag decides (no message sniffing).
    // Rethrows anything raised while the DB is open; logs and swallows
    // only the closed-DB race.
    private static void RunClosedDbGuard(Exception ex, string what)
    {
        if (!AtherizDbContext.IsClosed)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        AtherizLogger.LogWarning($"database closed; skipping {what}: {ex.Message}");
    }

    public static void LoadObjects(string savePath)
    {
        AtherizDbContext db;
        try { db = new AtherizDbContext(savePath); }
        catch (InvalidOperationException ex)
        {
            RunClosedDbGuard(ex, "load");
            return;
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            RunClosedDbGuard(ex, "load");
            return;
        }
        using (db)
        {
            try { db.Database.EnsureCreated(); }
            catch (InvalidOperationException ex)
            {
                RunClosedDbGuard(ex, "load");
                return;
            }
            catch (Exception ex)
            {
                // Closed-DB races only (no message sniffing): anything else propagates.
                RunClosedDbGuard(ex, "load");
                return;
            }
            try { LoadObjects(db); }
            catch (InvalidOperationException ex)
            {
                RunClosedDbGuard(ex, "load");
                return;
            }
            catch (Exception ex)
            {
                // Closed-DB races only (no message sniffing): anything else propagates.
                RunClosedDbGuard(ex, "load");
                return;
            }
        }
    }

    public static void SaveObjects(AtherizDbContext db, bool force = false)
    {
        List<GameObject> snapshot;
        lock (AllLock) { snapshot = AllObjects.Values.ToList(); }

        List<GameObject> filtered = new(snapshot.Count);
        foreach (var o in snapshot)
        {
            o.SyncRoot.EnterReadLock();
            bool keep;
            try { keep = !o.IsTemporary && !o.IsNode && !o.IsDeleted; }
            finally { o.SyncRoot.ExitReadLock(); }
            if (keep) filtered.Add(o);
        }

        // Drain the delete journal with this checkpoint: ids re-registered
        // since the delete (same-id recreate) are live and must survive.
        List<int> tombstones;
        lock (AllLock)
        {
            tombstones = _pendingDeletions.Where(id => !AllObjects.ContainsKey(id)).ToList();
            _pendingDeletions.Clear();
        }

        // A failed checkpoint must not lose delete intent: re-journal so a
        // later checkpoint retries (the rows are still there).
        void RestoreTombstones()
        {
            if (tombstones.Count == 0) return;
            try
            {
                lock (AllLock) { foreach (var id in tombstones) _pendingDeletions.Add(id); }
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.SaveObjects: " + logEx.Message, "BoundedDictionary"); }
        }

        // Best-effort dirty-restore shared by every failure path below:
        // per-item suppression so one torn lock can't abort the restore
        // of the rest (shutdown races).
        static void Restore(IEnumerable<GameObject> objs)
        {
            foreach (var o in objs)
            {
                try
                {
                    o.SyncRoot.EnterWriteLock();
                    try { o.IsModified = true; }
                    finally { o.SyncRoot.ExitWriteLock(); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.SaveObjects: " + logEx.Message, "BoundedDictionary"); }
            }
        }

        List<(GameObject obj, string json)> pending = [];
        List<GameObject> cleared = [];
        foreach (var obj in filtered)
        {
            if (!IsStillSaveable(obj, forSave: true, force: force)) continue;
            try
            {
                // atomic clear inside GetSaveOperationClearing
                var op = obj.GetSaveOperationClearing();
                pending.Add((obj, op.Json));
                cleared.Add(obj);
            }
            catch (Exception)
            {
                // row aborts the whole checkpoint — nothing has been written yet,
                // so restore every cleared flag and rethrow (the transaction
                // below never runs).
                Restore(cleared.Append(obj));
                RestoreTombstones();
                throw;
            }
        }

        if (pending.Count == 0 && tombstones.Count == 0) return;

        try
        {
            DbTransactionHelper.WithGateAndTransaction(db, ctx =>
            {
                foreach (var (obj, json) in pending)
                {
                    if (!IsStillSaveable(obj, forSave: false, force: force))
                    {
                        // Clear-then-skip: the flag was cleared in the serialize
                        // phase but this row is not being written — restore dirty
                        // so the next checkpoint retries (for deleted/evicted
                        // objects the flag dies with the object; harmless).
                        Restore([obj]);
                        continue;
                    }
                    DbTransactionHelper.UpsertJson(ctx.Objects, () => ctx.Objects.Find(obj.Id), () => new ObjectRow { Id = obj.Id, Version = 1 }, json, row =>
                    {
                        row.Type = obj.IsAccount ? "account" : obj.IsChannel ? "channel" : "object";
                    });
                }
                // Deletes ride the same transaction, after the upserts: a
                // tombstone always postdates its object's unregistration, so
                // no live row written above carries a tombstoned id.
                if (tombstones.Count > 0)
                    ctx.Objects.Where(o => tombstones.Contains(o.Id)).ExecuteDelete();
            }, onRollback: () => Restore(pending.Select(p => p.obj)));
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            RestoreTombstones();
            RunClosedDbGuard(ex, "save");
            Restore(pending.Select(p => p.obj));
            return;
        }
    }

    // ATHERIZ_SAVE_PATH env override (tests point it at a temp dir, like conftest).
    public static void SaveObjects(bool force = false)
    {
        var savePath = global::Atheriz.Core.Persistence.AtherizDbContextFactory.ResolveSavePath(AtherizSettings.Global);
        SaveObjects(savePath, force);
    }

    public static void SaveObjects(string savePath, bool force = false)
    {        AtherizDbContext db;
        try { db = new AtherizDbContext(savePath); }
        catch (Exception ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing).
            RunClosedDbGuard(ex, "save");
            // restore cleared flags? none yet, but ensure pending objects stay dirty
            // We haven't built pending yet, so nothing to restore; just log warning not exception
            return;
        }
        using (db)
        {
            try { db.Database.EnsureCreated(); }
            catch (Exception ex)
            {
                // Closed-DB guard is the IsClosed flag (no message sniffing).
                RunClosedDbGuard(ex, "save");
                return;
            }
            try { SaveObjects(db, force); }
            catch (Exception ex)
            {
                // Closed-DB guard is the IsClosed flag (no message sniffing).
                RunClosedDbGuard(ex, "save");
                return;
            }
        }
    }

    public static void DeleteObjects(AtherizDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return;
        DbTransactionHelper.WithGateAndTransaction(db, ctx =>
        {
            // Single set-based delete, not per-id Find+Remove (N+1
            // round-trips). Deletes stay loud on failure (unlike the save
            // paths' closed-DB skip): callers roll back the in-memory removal
            // on throw, so swallowing here would resurrect the row on next
            // load. The asymmetry is intentional.
            ctx.Objects.Where(o => ids.Contains(o.Id)).ExecuteDelete();
        });
    }
}
