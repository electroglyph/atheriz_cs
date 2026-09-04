using System.Collections.Concurrent;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
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
        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                var isNew = !_dict.ContainsKey(key);
                _dict[key] = value;
                if (isNew) _order.Enqueue(key);
                if (isNew && _dict.Count > Limit)
                {
                    var oldest = _order.Dequeue();
                    // if oldest == key (re-enqueued same key that was oldest) need next
                    if (EqualityComparer<TKey>.Default.Equals(oldest, key) && _order.Count > 0)
                        oldest = _order.Dequeue();
                    _dict.Remove(oldest);
                }
            }
        }
        public TValue? Get(TKey key)
        {
            lock (_lock) return _dict.TryGetValue(key, out var v) ? v : default;
        }
        public bool Contains(TKey key) { lock (_lock) return _dict.ContainsKey(key); }
        public void Remove(TKey key) { lock (_lock) _dict.Remove(key); }
        public void Clear() { lock (_lock) { _dict.Clear(); _order.Clear(); } }
        public Dictionary<TKey, TValue> Snapshot() { lock (_lock) return new Dictionary<TKey, TValue>(_dict); }
        public int Count { get { lock (_lock) return _dict.Count; } }
    }

    // --- state ---
    internal static readonly ReaderWriterLockSlim AllLock = new(LockRecursionPolicy.SupportsRecursion);
    private static readonly Dictionary<int, GameObject> AllObjects = new();
    // Keep strong refs to ever-created objects to allow MoveTo to find oldLoc even after concurrent ClearAll (flaky test fix)
    private static readonly ConcurrentDictionary<int, GameObject> EverCreated = new();

    private static readonly BoundedDictionary<string, double> TempBannedIps = new();
    private static readonly object TempBannedLock = new();
    private static readonly BoundedDictionary<string, double> CreationCooldowns = new();
    private static readonly BoundedDictionary<string, int> FailedLoginAttempts = new();
    private static readonly object CooldownLock = new();
    private static readonly object FailedLoginLock = new();

    public static bool AlwaysSaveAll { get; set; } = false;

    // --- bans ---
    public static bool IsIpBanned(string host, double? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (TempBannedLock)
        {
            var expires = TempBannedIps.Get(host);
            // BoundedDictionary returns default(double)=0 if missing — need distinction
            // Use snapshot check
            var snap = TempBannedIps.Snapshot();
            if (!snap.TryGetValue(host, out var exp)) return false;
            if (t < exp) return true;
            TempBannedIps.Remove(host);
            return false;
        }
    }
    public static void BanIp(string host, double? expires = null)
    {
        var exp = expires ?? double.PositiveInfinity;
        TempBannedIps.Set(host, exp);
    }
    public static void UnbanIp(string host) => TempBannedIps.Remove(host);

    // --- creation cooldowns (unified per host, ? bypass) ---
    private static string CooldownKey(string host) => host;
    public static bool CreationCooldownActive(string op, string host, double now)
    {
        if (host == "?") return false;
        var key = CooldownKey(host);
        var snap = CreationCooldowns.Snapshot();
        if (!snap.TryGetValue(key, out var exp)) return false;
        if (exp > now) return true;
        CreationCooldowns.Remove(key);
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
        lock (CooldownLock)
        {
            var snap = CreationCooldowns.Snapshot();
            if (snap.TryGetValue(key, out var exp) && exp > now) return false;
            if (cooldown > 0) CreationCooldowns.Set(key, now + cooldown);
            return true;
        }
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

    public static List<GameObject> GetByTag(object tag, bool all = false)
    {
        HashSet<string> tags = tag switch
        {
            string s => [s],
            IEnumerable<string> e => new HashSet<string>(e),
            _ => []
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
        AllLock.EnterReadLock();
        try { return ids.Select(id => AllObjects.TryGetValue(id, out var o) ? o : null).Where(o => o != null).Cast<GameObject>().ToList(); }
        finally { AllLock.ExitReadLock(); }
    }

    public static void AddObject(GameObject obj)
    {
        AllLock.EnterWriteLock();
        try
        {
            var stale = AllObjects.Where(kv => ReferenceEquals(kv.Value, obj) && kv.Key != obj.Id).Select(kv => kv.Key).ToList();
            foreach (var k in stale) AllObjects.Remove(k);
            AllObjects[obj.Id] = obj;
        }
        finally { AllLock.ExitWriteLock(); }
        EverCreated[obj.Id] = obj;
    }

    public static GameObject? GetEver(int id)
    {
        AllLock.EnterReadLock();
        try { if (AllObjects.TryGetValue(id, out var o)) return o; }
        finally { AllLock.ExitReadLock(); }
        if (EverCreated.TryGetValue(id, out var t) && !t.IsDeleted) return t;
        return null;
    }

    public static void AddObjectUnique(GameObject obj, Func<GameObject, bool> predicate, string error)
    {
        while (true)
        {
            List<GameObject> snap;
            AllLock.EnterReadLock();
            try { snap = AllObjects.Values.ToList(); }
            finally { AllLock.ExitReadLock(); }
            if (snap.Any(predicate)) throw new InvalidOperationException(error);
            AllLock.EnterWriteLock();
            try
            {
                var current = AllObjects.Values.ToList();
                // compare by reference sequence equality — if same count and same set, no race
                if (current.Count == snap.Count && current.All(c => snap.Contains(c)))
                {
                    var stale = AllObjects.Where(kv => ReferenceEquals(kv.Value, obj) && kv.Key != obj.Id).Select(kv => kv.Key).ToList();
                    foreach (var k in stale) AllObjects.Remove(k);
                    AllObjects[obj.Id] = obj;
                    return;
                }
            }
            finally { AllLock.ExitWriteLock(); }
        }
    }

    public static void RemoveObject(GameObject obj)
    {
        AllLock.EnterWriteLock();
        try { AllObjects.Remove(obj.Id); }
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
        AllLock.EnterReadLock();
        try { if (!AllObjects.TryGetValue(id, out var cur) || !ReferenceEquals(cur, obj)) return false; }
        finally { AllLock.ExitReadLock(); }
        // need obj lock
        obj.SyncRoot.EnterReadLock();
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
            JsonTableLoader.LoadList(db.Objects, json =>
            {
                try
                {
                    var dto = GameObjectDtoSerializer.FromJson(json);
                    return GameObjectDtoSerializer.Migrate(dto);
                }
                catch { return null; }
            }, (dto, row) =>
            {
                try
                {
                    var obj = GameObject.FromDto(dto!);
                    objects[row.Id] = obj;
                    if (row.Id > maxId) maxId = row.Id;
                }
                catch { }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
            Console.Error.WriteLine("database closed");
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
            return;
        }
        if (objects.Count == 0 && maxId == -1)
        {
            // empty table: still clear? mimic original early return but ensure cleared
            // If no rows, keep existing clear behaviour
        }
        AllLock.EnterWriteLock();
        try
        {
            AllObjects.Clear();
            foreach (var kv in objects) AllObjects[kv.Key] = kv.Value;
        }
        finally { AllLock.ExitWriteLock(); }

        if (maxId > IdGenerator.GetId())
            IdGenerator.SetId(maxId);

        // second pass: resolve relations — port of objects.py:272-276 for obj in snapshot: obj.resolve_relations()
        List<GameObject> snap;
        AllLock.EnterReadLock();
        try { snap = AllObjects.Values.ToList(); }
        finally { AllLock.ExitReadLock(); }
        foreach (var o in snap)
        {
            try { o.ResolveRelations(); } catch { }
        }
    }

    public static void LoadObjects(string savePath)
    {
        AtherizDbContext db;
        try { db = new AtherizDbContext(savePath); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
            Console.Error.WriteLine("database closed");
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
            return;
        }
        using (db)
        {
            try { db.Database.EnsureCreated(); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
                return;
            }
            try { LoadObjects(db); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
                Console.Error.WriteLine("database closed");
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping load: {ex.Message}");
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
            catch
            {
                foreach (var c in cleared)
                {
                    c.SyncRoot.EnterWriteLock();
                    try { c.IsModified = true; }
                    finally { c.SyncRoot.ExitWriteLock(); }
                }
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
                    if (!IsStillSaveable(obj, forSave: false, force: force)) continue;
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
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
            Console.Error.WriteLine("database closed");
            foreach (var (obj, _) in pending)
            {
                obj.SyncRoot.EnterWriteLock();
                try { obj.IsModified = true; }
                finally { obj.SyncRoot.ExitWriteLock(); }
            }
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
            Console.Error.WriteLine("database closed");
            foreach (var (obj, _) in pending)
            {
                obj.SyncRoot.EnterWriteLock();
                try { obj.IsModified = true; }
                finally { obj.SyncRoot.ExitWriteLock(); }
            }
            return;
        }
    }

    public static void SaveObjects(string savePath, bool force = false)
    {
        AtherizDbContext db;
        try { db = new AtherizDbContext(savePath); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
            Console.Error.WriteLine("database closed");
            // restore cleared flags? none yet, but ensure pending objects stay dirty
            // We haven't built pending yet, so nothing to restore; just log warning not exception
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
            return;
        }
        using (db)
        {
            try { db.Database.EnsureCreated(); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
                Console.Error.WriteLine("database closed");
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
                return;
            }
            try { SaveObjects(db, force); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
                Console.Error.WriteLine("database closed");
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"database closed; skipping save: {ex.Message}");
                return;
            }
        }
    }

    public static void DeleteObjects(AtherizDbContext db, List<(string Sql, object[] Params)> ops)
    {
        if (ops.Count == 0) return;
        DbTransactionHelper.WithGateAndTransaction(db, ctx =>
        {
            foreach (var op in ops)
            {
                var id = Convert.ToInt32(op.Params[0]);
                var row = ctx.Objects.Find(id);
                if (row != null) ctx.Objects.Remove(row);
            }
        });
    }
}
