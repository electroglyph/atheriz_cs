using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

/// <summary>
/// Port of <c>atheriz/globals/objects.py</c>.
/// Global object registry + temp bans + creation cooldowns.
/// All access guarded by ReaderWriterLockSlim (mirrors Python RLock).
/// Persistence via <see cref="AtherizDbContext"/> + JSON (replaces dill).
/// </summary>
public static class ObjectRegistry
{
    // --- bounded dict (FIFO eviction at 4000) ---
    public sealed class BoundedDictionary<TKey, TValue> where TKey : notnull
    {
        private const int Limit = 4000;
        private readonly Dictionary<TKey, TValue> _dict = new();
        private readonly Queue<TKey> _order = new();
        private readonly object _lock = new();
        private void EvictIfNeeded()
        {
            if (_dict.Count <= Limit) return;
            // Only newly-enqueued keys call this, and they land at the tail,
            // so the head can never be the just-set key — plain dequeue.
            _dict.Remove(_order.Dequeue());
        }
        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                var isNew = !_dict.ContainsKey(key);
                _dict[key] = value;
                if (!isNew) return;
                _order.Enqueue(key);
                EvictIfNeeded();
            }
        }
        /// <summary>Atomic read-modify-write under the dict lock (F005 login hot path).</summary>
        public TValue AddOrUpdate(TKey key, Func<bool, TValue, TValue> updater)
        {
            lock (_lock)
            {
                var isNew = !_dict.ContainsKey(key);
                var nv = updater(!isNew, isNew ? default! : _dict[key]);
                _dict[key] = nv;
                if (isNew)
                {
                    _order.Enqueue(key);
                    EvictIfNeeded();
                }
                return nv;
            }
        }
        /// <summary>
        /// Atomic check-then-set under the dict lock: sets <paramref name="value"/> only when
        /// <paramref name="allow"/> (exists, current-or-default) returns true.
        /// </summary>
        public bool CheckAndSet(TKey key, TValue value, Func<bool, TValue, bool> allow)
        {
            lock (_lock)
            {
                var exists = _dict.ContainsKey(key);
                if (!allow(exists, exists ? _dict[key] : default!)) return false;
                _dict[key] = value;
                if (!exists)
                {
                    _order.Enqueue(key);
                    EvictIfNeeded();
                }
                return true;
            }
        }
        public TValue? Get(TKey key)
        {
            lock (_lock) return _dict.TryGetValue(key, out var v) ? v : default;
        }
        public bool Contains(TKey key) { lock (_lock) return _dict.ContainsKey(key); }
        public bool TryGetValue(TKey key, out TValue? value) { lock (_lock) return _dict.TryGetValue(key, out value); }
        public void Remove(TKey key)
        {
            lock (_lock)
            {
                _dict.Remove(key);
                PurgeLocked(key);
            }
        }
        /// <summary>
        /// Atomic remove-if-value-matches under the dict lock: expiry cleanup
        /// must not delete a concurrently refreshed entry.
        /// </summary>
        public bool RemoveIfEqual(TKey key, TValue expected)
        {
            lock (_lock)
            {
                if (!_dict.TryGetValue(key, out var cur)) return false;
                if (!EqualityComparer<TValue>.Default.Equals(cur, expected)) return false;
                _dict.Remove(key);
                PurgeLocked(key);
                return true;
            }
        }
        private void PurgeLocked(TKey key)
        {
            // Purge the FIFO queue too — otherwise eviction later removes the wrong live key (F005).
            if (_order.Count > 0)
            {
                var kept = new Queue<TKey>(_order.Count);
                foreach (var k in _order) if (!EqualityComparer<TKey>.Default.Equals(k, key)) kept.Enqueue(k);
                _order.Clear();
                foreach (var k in kept) _order.Enqueue(k);
            }
        }
        public void Clear() { lock (_lock) { _dict.Clear(); _order.Clear(); } }
        public Dictionary<TKey, TValue> Snapshot() { lock (_lock) return new Dictionary<TKey, TValue>(_dict); }
        public int Count { get { lock (_lock) return _dict.Count; } }
    }

    // --- state ---
    internal static readonly ReaderWriterLockSlim AllLock = new(LockRecursionPolicy.SupportsRecursion);
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

    // All callers hold AllLock write.
    private static void IndexInsert(GameObject obj)
    {
        if (_keysByRef.TryGetValue(obj, out var oldKey) && oldKey != obj.Id)
            AllObjects.Remove(oldKey);
        AllObjects[obj.Id] = obj;
        _keysByRef[obj] = obj.Id;
    }

    private static readonly BoundedDictionary<string, double> TempBannedIps = new();
    private static readonly BoundedDictionary<string, double> CreationCooldowns = new();
    private static readonly BoundedDictionary<string, int> FailedLoginAttempts = new();

    public static bool AlwaysSaveAll { get; set; } = false;

    // --- bans ---
    public static bool IsIpBanned(string host, double? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Expiry cleanup is remove-if-equal: a BanIp landing between the read
        // and the cleanup must not delete the fresh ban.
        if (!TempBannedIps.TryGetValue(host, out var exp)) return false;
        if (t < exp) return true;
        TempBannedIps.RemoveIfEqual(host, exp);
        return false;
    }
    public static void BanIp(string host, double? expires = null)
    {
        var exp = expires ?? double.PositiveInfinity;
        TempBannedIps.Set(host, exp);
    }
    public static void UnbanIp(string host) => TempBannedIps.Remove(host);

    // --- creation cooldowns (unified per host, ? bypass) ---
    private static string CooldownKey(string host) => host;
    public static bool CreationCooldownActive(string host, double now)
    {
        if (host == "?") return false;
        var key = CooldownKey(host);
        if (!CreationCooldowns.TryGetValue(key, out var exp)) return false;
        if (exp > now) return true;
        CreationCooldowns.RemoveIfEqual(key, exp);
        return false;
    }
    public static void ApplyCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?" || cooldown <= 0) return;
        CreationCooldowns.Set(CooldownKey(host), now + cooldown);
    }
    public static bool TryReserveCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?") return true;
        var key = CooldownKey(host);
        if (cooldown <= 0) return !CreationCooldownActive(host, now);
        // Atomic check+reserve on the dict's own lock (F005) — no snapshot, no outer lock.
        return CreationCooldowns.CheckAndSet(key, now + cooldown, (exists, exp) => !exists || exp <= now);
    }
    public static void ClearCreationCooldown(string host)
    {
        if (host == "?") return;
        CreationCooldowns.Remove(CooldownKey(host));
    }

    // --- failed login map exposed for parity ---
    public static BoundedDictionary<string, int> FailedLogins => FailedLoginAttempts;

    // --- core registry ---
    public static List<GameObject> FilterBy(Func<GameObject, bool> predicate)
    {
        List<GameObject> snap;
        AllLock.EnterReadLock();
        try { snap = AllObjects.Values.ToList(); }
        finally { AllLock.ExitReadLock(); }
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
        AllLock.EnterReadLock();
        List<GameObject> snap;
        try { snap = AllObjects.Values.ToList(); }
        finally { AllLock.ExitReadLock(); }
        foreach (var o in snap)
            if (o is Node n && n.Coord.Equals(coord)) return n;
        return null;
    }

    public static List<GameObject> GetByTag(object tag, bool all = false)
    {
        // Port of objects.py:169 set(tag): non-string/non-iterable tags raise
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
        AllLock.EnterReadLock();
        try { return AllObjects.TryGetValue(id, out var o) ? [o] : []; }
        finally { AllLock.ExitReadLock(); }
    }
    public static List<GameObject> Get(IEnumerable<int> ids)
    {
        // materialize the caller enumerable BEFORE the read lock —
        // a lazy enumerable would execute arbitrary caller code under AllLock.
        var list = ids.ToList();
        AllLock.EnterReadLock();
        try { return list.Select(id => AllObjects.TryGetValue(id, out var o) ? o : null).Where(o => o != null).Cast<GameObject>().ToList(); }
        finally { AllLock.ExitReadLock(); }
    }

    public static void AddObject(GameObject obj)
    {
        AllLock.EnterWriteLock();
        try
        {
            IndexInsert(obj);
        }
        finally { AllLock.ExitWriteLock(); }
    }

    public static GameObject? GetEver(int id)
    {
        // Live-map only (F005): the old EverCreated strong-ref cache leaked memory and
        // resurrected ClearAll'd objects. Callers needing a stale object must hold their own ref.
        AllLock.EnterReadLock();
        try { return AllObjects.TryGetValue(id, out var o) ? o : null; }
        finally { AllLock.ExitReadLock(); }
    }

    public static void AddObjectUnique(GameObject obj, Func<GameObject, bool> predicate, string error)
    {
        // Single write lock covering check+insert (F005) — no read-check/write-recheck spin.
        // Safe: uniqueness predicates only read object properties (no registry re-entry).
        AllLock.EnterWriteLock();
        try
        {
            if (AllObjects.Values.Any(predicate)) throw new InvalidOperationException(error);
            IndexInsert(obj);
        }
        finally { AllLock.ExitWriteLock(); }
    }

    public static void RemoveObject(GameObject obj)
    {
        AllLock.EnterWriteLock();
        try
        {
            AllObjects.Remove(obj.Id);
            // Drop the reverse entry only if it points at the removed key —
            // AllObjects[obj.Id] may have been a different occupant.
            if (_keysByRef.TryGetValue(obj, out var k) && k == obj.Id) _keysByRef.Remove(obj);
        }
        finally { AllLock.ExitWriteLock(); }
    }

    public static void ClearAll()
    {
        // Hold both AllLock and IdGenerator lock to prevent duplicate Id race with concurrent GetUniqueId
        lock (IdGenerator.LockObj)
        {
            AllLock.EnterWriteLock();
            try
            {
                AllObjects.Clear();
                _keysByRef.Clear();
                // directly set without extra lock (already holding IdGenerator lock via LockObj)
                // Use SetId which will re-lock but recursion on same lock object is not allowed for Monitor; use field directly
                // So we set via reflection-safe direct field access under lock
                // Instead call SetId which uses same lock object — would deadlock (Monitor re-enter not allowed if same thread? Actually Monitor is re-entrant, so okay)
                IdGenerator.SetId(-1);
            }
            finally { AllLock.ExitWriteLock(); }
        }
        TempBannedIps.Clear();
        CreationCooldowns.Clear();
        FailedLoginAttempts.Clear();
    }

    public static int Count
    {
        get { AllLock.EnterReadLock(); try { return AllObjects.Count; } finally { AllLock.ExitReadLock(); } }
    }

    private static bool IsStillSaveable(GameObject obj, bool forSave = false, bool force = false)
    {
        var id = obj.Id;
        // lock coupling — the obj read lock is taken BEFORE AllLock
        // is released, so the registry-membership check and the flag reads
        // are atomic (no evict/delete/re-key can interleave). Read->read
        // nesting is safe: no path holds AllLock.Write while taking obj
        // locks, and callers never hold obj locks into this method.
        AllLock.EnterReadLock();
        try
        {
            if (!AllObjects.TryGetValue(id, out var cur) || !ReferenceEquals(cur, obj)) return false;
            obj.SyncRoot.EnterReadLock();
        }
        finally { AllLock.ExitReadLock(); }
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
        var objects = new Dictionary<int, GameObject>();
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
                if (dto == null)
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
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping load: {ex.Message}");
            AtherizLogger.LogWarning("database closed");
            return;
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping load: {ex.Message}");
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
            AllLock.EnterWriteLock();
            try
            {
                AllObjects.Clear();
                _keysByRef.Clear();
                foreach (var kv in objects) { AllObjects[kv.Key] = kv.Value; _keysByRef[kv.Value] = kv.Key; }
            }
            finally { AllLock.ExitWriteLock(); }

            if (maxId > IdGenerator.GetId())
                IdGenerator.SetId(maxId);
        }

        // second pass: resolve relations — port of objects.py:272-276 for obj in snapshot: obj.resolve_relations()
        List<GameObject> snap;
        AllLock.EnterReadLock();
        try { snap = AllObjects.Values.ToList(); }
        finally { AllLock.ExitReadLock(); }
        foreach (var o in snap)
        {
            try { o.ResolveRelations(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.LoadObjects: " + logEx.Message, "BoundedDictionary"); }
        }
    }

    // Closed-DB guard: the IsClosed flag decides (no message sniffing).
    // Rethrows anything raised while the DB is open; logs and swallows
    // only the closed-DB race.
    private static void RunClosedDbGuard(Exception ex)
    {
        if (!AtherizDbContext.IsClosed)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        AtherizLogger.LogWarning($"database closed; skipping load: {ex.Message}");
    }

    public static void LoadObjects(string savePath)
    {
        AtherizDbContext db;
        try { db = new AtherizDbContext(savePath); }
        catch (InvalidOperationException ex)
        {
            RunClosedDbGuard(ex);
            return;
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            RunClosedDbGuard(ex);
            return;
        }
        using (db)
        {
            try { db.Database.EnsureCreated(); }
            catch (InvalidOperationException ex)
            {
                RunClosedDbGuard(ex);
                return;
            }
            catch (Exception ex)
            {
                // Closed-DB races only (no message sniffing): anything else propagates.
                RunClosedDbGuard(ex);
                return;
            }
            try { LoadObjects(db); }
            catch (InvalidOperationException ex)
            {
                RunClosedDbGuard(ex);
                return;
            }
            catch (Exception ex)
            {
                // Closed-DB races only (no message sniffing): anything else propagates.
                RunClosedDbGuard(ex);
                return;
            }
        }
    }

    public static void SaveObjects(AtherizDbContext db, bool force = false)
    {
        List<GameObject> snapshot;
        AllLock.EnterReadLock();
        try { snapshot = AllObjects.Values.ToList(); }
        finally { AllLock.ExitReadLock(); }

        var filtered = snapshot.Where(o =>
        {
            o.SyncRoot.EnterReadLock();
            try { return !o.IsTemporary && !o.IsNode && !o.IsDeleted; }
            finally { o.SyncRoot.ExitReadLock(); }
        }).ToList();

        var pending = new List<(GameObject obj, string json)>();
        var cleared = new List<GameObject>();
        foreach (var obj in filtered)
        {
            if (!IsStillSaveable(obj, forSave: true, force: force)) continue;
            try
            {
                // atomic clear inside GetSaveOpsClearing
                var (_, parms) = obj.GetSaveOpsClearing();
                var json = (string)parms[1];
                pending.Add((obj, json));
                cleared.Add(obj);
            }
            catch (Exception)
            {
                // Port of objects.py:save_objects serialization phase: a poison
                // row aborts the whole checkpoint — nothing has been written yet,
                // so restore every cleared flag and rethrow (the transaction
                // below never runs).
                foreach (var c in cleared)
                {
                    try
                    {
                        c.SyncRoot.EnterWriteLock();
                        try { c.IsModified = true; }
                        finally { c.SyncRoot.ExitWriteLock(); }
                    }
                    catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.SaveObjects: " + logEx.Message, "BoundedDictionary"); }
                }
                try
                {
                    obj.SyncRoot.EnterWriteLock();
                    try { obj.IsModified = true; }
                    finally { obj.SyncRoot.ExitWriteLock(); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BoundedDictionary.SaveObjects: " + logEx.Message, "BoundedDictionary"); }
                throw;
            }
        }

        if (pending.Count == 0) return;

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
                        obj.SyncRoot.EnterWriteLock();
                        try { obj.IsModified = true; }
                        finally { obj.SyncRoot.ExitWriteLock(); }
                        continue;
                    }
                    DbTransactionHelper.UpsertJson(ctx.Objects, () => ctx.Objects.Find(obj.Id), () => new ObjectRow { Id = obj.Id, Version = 1 }, json, row =>
                    {
                        row.Type = obj.IsAccount ? "account" : obj.IsChannel ? "channel" : "object";
                    });
                }
            }, onRollback: () =>
            {
                foreach (var (obj, _) in pending)
                {
                    obj.SyncRoot.EnterWriteLock();
                    try { obj.IsModified = true; }
                    finally { obj.SyncRoot.ExitWriteLock(); }
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing).
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
            AtherizLogger.LogWarning("database closed");
            foreach (var (obj, _) in pending)
            {
                obj.SyncRoot.EnterWriteLock();
                try { obj.IsModified = true; }
                finally { obj.SyncRoot.ExitWriteLock(); }
            }
            return;
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
            AtherizLogger.LogWarning("database closed");
            foreach (var (obj, _) in pending)
            {
                obj.SyncRoot.EnterWriteLock();
                try { obj.IsModified = true; }
                finally { obj.SyncRoot.ExitWriteLock(); }
            }
            return;
        }
    }

    // Port of save_objects() default path: settings.SAVE_PATH, honoring the
    // ATHERIZ_SAVE_PATH env override (tests point it at a temp dir, like conftest).
    public static void SaveObjects(bool force = false)
    {
        var savePath = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? AtherizSettings.Global.SavePath;
        SaveObjects(savePath, force);
    }

    public static void SaveObjects(string savePath, bool force = false)
    {        AtherizDbContext db;
        try { db = new AtherizDbContext(savePath); }
        catch (InvalidOperationException ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing).
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
            AtherizLogger.LogWarning("database closed");
            // restore cleared flags? none yet, but ensure pending objects stay dirty
            // We haven't built pending yet, so nothing to restore; just log warning not exception
            return;
        }
        catch (Exception ex)
        {
            // Closed-DB races only (no message sniffing): anything else propagates.
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
            return;
        }
        using (db)
        {
            try { db.Database.EnsureCreated(); }
            catch (InvalidOperationException ex)
            {
                // Closed-DB guard is the IsClosed flag (no message sniffing).
                if (!AtherizDbContext.IsClosed) throw;
                AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
                AtherizLogger.LogWarning("database closed");
                return;
            }
            catch (Exception ex)
            {
                // Closed-DB races only (no message sniffing): anything else propagates.
                if (!AtherizDbContext.IsClosed) throw;
                AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
                return;
            }
            try { SaveObjects(db, force); }
            catch (InvalidOperationException ex)
            {
                // Closed-DB guard is the IsClosed flag (no message sniffing).
                if (!AtherizDbContext.IsClosed) throw;
                AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
                AtherizLogger.LogWarning("database closed");
                return;
            }
            catch (Exception ex)
            {
                // Closed-DB races only (no message sniffing): anything else propagates.
                if (!AtherizDbContext.IsClosed) throw;
                AtherizLogger.LogWarning($"database closed; skipping save: {ex.Message}");
                return;
            }
        }
    }

    public static void DeleteObjects(AtherizDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return;
        DbTransactionHelper.WithGateAndTransaction(db, ctx =>
        {
            foreach (var id in ids)
            {
                var row = ctx.Objects.Find(id);
                if (row != null) ctx.Objects.Remove(row);
            }
        });
    }
}
