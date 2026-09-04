// Port of atheriz/globals/mapedit.py — MapEdit chain grant/consume with cap 256.
// Faithful to _chains/_previous, _evict, grant(issue key), consume(seq validation), retry/replay/gap.
// No time-based expiry: a chain is valid while its owning session is open
// (discarded on disconnect via DiscardSession); only the cap evicts.
// Also satisfies spec: sealed class MapEditChain with DateTime CreatedAt, List<Coord> chain,
// static MapEdit with Dictionary<string,MapEditChain> chains, ReaderWriterLockSlim Lock, MaxChains=256,
// AddChain/GetChain/ValidateChain/ClearStale/RemoveChain/DiscardSession.

using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Globals;

// Port of mapedit.py:9-12 constants
public static class MapEditStatus
{
    public const string Processed = "processed"; // Port of mapedit.py:9 PROCESSED
    public const string Retry = "retry"; // Port of mapedit.py:10 RETRY
    public const string Reject = "reject"; // Port of mapedit.py:11 REJECT
}

// Port of mapedit.py:14-23 MapEditChain
public sealed class MapEditChain
{
    // Port of mapedit.py:15 key
    public string Key { get; set; }
    // Port of mapedit.py:16 previous_key
    public string PreviousKey { get; set; } = "";
    // Port of mapedit.py:17 seq = -1
    public int Seq { get; set; } = -1;
    // Port of mapedit.py:18 ip
    public string Ip { get; set; }
    // Port of mapedit.py:19 area
    public string Area { get; set; }
    // Port of mapedit.py:20 z
    public int Z { get; set; }
    // Port of mapedit.py:21 validation: list[int] | None
    public List<int>? Validation { get; set; }
    // Port of mapedit.py:23 created = time.monotonic()
    // In C# we keep both monotonic seconds and wall DateTime for spec's CreatedAt
    public double CreatedMonotonic { get; set; }
    // Port of spec: DateTime CreatedAt
    public DateTime CreatedAt { get; set; }
    // Owning game session. A chain is valid for as long as this session is
    // open (see DiscardSession); null (e.g. tests) lives until cap eviction.
    public Session? Session { get; set; }
    // Port of spec: List<Coord> chain
    public List<Coord> Chain { get; set; } = new();

    public MapEditChain(string key, string ip, string area, int z, Session? session = null)
    {
        Key = key;
        Ip = ip;
        Area = area;
        Z = z;
        Session = session;
        CreatedAt = DateTime.UtcNow;
        CreatedMonotonic = MapEdit.GetMonotonic();
        Chain = new List<Coord>();
    }
}

// Port of mapedit.py:26-33 MapEditResult
public sealed class MapEditResult
{
    public const string Processed = MapEditStatus.Processed;
    public const string Retry = MapEditStatus.Retry;
    public const string Reject = MapEditStatus.Reject;

    public string Status { get; }
    public string Reason { get; }
    public string? NewKey { get; }
    public MapEditChain? Chain { get; }

    public MapEditResult(string status, string reason = "", string? newKey = null, MapEditChain? chain = null)
    {
        Status = status;
        Reason = reason;
        NewKey = newKey;
        Chain = chain;
    }
}

// Port of mapedit.py:36-108 module-level _chains/_previous/_lock + grant/consume
public static class MapEdit
{
    // Port of settings.MAPEDIT_MAX_CHAINS = 256
    public const int MaxChains = 256; // Port of mapedit.py cap = MAPEDIT_MAX_CHAINS

    // Port of mapedit.py:36 _chains: dict[str, MapEditChain]
    private static readonly Dictionary<string, MapEditChain> _chains = new();
    // Port of mapedit.py:37 _previous: dict[str,str]
    private static readonly Dictionary<string, string> _previous = new();
    // Port of mapedit.py:38 _lock = RLock()
    public static readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);

    // Port of spec: Dictionary<string,MapEditChain> chains — expose for inspection (thread-safe snapshot via property, direct via field for compatibility)
    public static Dictionary<string, MapEditChain> chains => _chains;
    // Also provide capitalized alias per spec naming
    public static IReadOnlyDictionary<string, MapEditChain> ChainsSnapshot
    {
        get
        {
            Lock.EnterReadLock();
            try { return new Dictionary<string, MapEditChain>(_chains); }
            finally { Lock.ExitReadLock(); }
        }
    }
    public static IReadOnlyDictionary<string, string> PreviousSnapshot
    {
        get
        {
            Lock.EnterReadLock();
            try { return new Dictionary<string, string>(_previous); }
            finally { Lock.ExitReadLock(); }
        }
    }

    internal static double GetMonotonic() => global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();

    [Obsolete("Use global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds()")]
    internal static double GetMonotonicObsolete() => global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();

    private static string GenerateToken()
    {
        // Port of mapedit.py:66 secrets.token_urlsafe(32) — 32 bytes => 43 char urlsafe base64
        return CryptoRandom.UrlSafeToken(32);
    }

    // Port of mapedit.py cap = MAPEDIT_MAX_CHAINS: read the ambient
    // global settings like Python reads module-level settings.
    internal static int EffectiveCap()
    {
        try
        {
            int cap = AtherizSettings.Global.MapeditMaxChains;
            if (cap <= 0) cap = MaxChains;
            return cap;
        }
        catch
        {
            return MaxChains;
        }
    }

    // Port of mapedit.py _evict — no time-based expiry (valid while session
    // open); only drops stale previous-key mappings and enforces the cap.
    private static void EvictLocked(double nowMonotonic)
    {
        int cap = EffectiveCap();
        // Port of mapedit.py:50 stale = [p for p,cur in _previous if cur not in _chains]
        var stale = new List<string>();
        foreach (var kv in _previous)
            if (!_chains.ContainsKey(kv.Value)) stale.Add(kv.Key);
        foreach (var k in stale) _previous.Remove(k);

        // Port of mapedit.py:53-60 while len(_chains) > cap: oldest = min by created
        while (_chains.Count > cap)
        {
            string? oldest = null;
            double oldestCreated = double.MaxValue;
            foreach (var kv in _chains)
            {
                double c = kv.Value.CreatedMonotonic;
                if (c == 0) c = (kv.Value.CreatedAt - DateTime.UnixEpoch).TotalSeconds;
                if (c < oldestCreated) { oldestCreated = c; oldest = kv.Key; }
            }
            if (oldest == null) break;
            var removed = _chains[oldest];
            _chains.Remove(oldest);
            if (removed != null && !string.IsNullOrEmpty(removed.PreviousKey))
                _previous.Remove(removed.PreviousKey);
            // Port of mapedit.py:58-60 stale after eviction
            var stale2 = new List<string>();
            foreach (var kv in _previous) if (!_chains.ContainsKey(kv.Value)) stale2.Add(kv.Key);
            foreach (var k in stale2) _previous.Remove(k);
        }
    }

    // Port of mapedit.py:63-70 grant(ip,area,z) -> key
    public static string Grant(string ip, string area, int z, Session? session = null)
    {
        Lock.EnterWriteLock();
        try
        {
            double now = GetMonotonic();
            EvictLocked(now);
            string key;
            int attempts = 0;
            do
            {
                key = GenerateToken();
                attempts++;
                if (attempts > 100) throw new InvalidOperationException("Failed to generate unique mapedit key");
            } while (_chains.ContainsKey(key) || _previous.ContainsKey(key));
            var chain = new MapEditChain(key, ip, area, z, session);
            _chains[key] = chain;
            if (_chains.Count > EffectiveCap())
            {
                // Port of mapedit.py:68 if len > cap: _evict
                EvictLocked(GetMonotonic());
            }
            // Also cap via settings cap (Evict already enforces)
            return key;
        }
        finally { Lock.ExitWriteLock(); }
    }

    // Spec wrapper: AddChain(ip,area,z) => Grant
    public static string AddChain(string ip, string area, int z, Session? session = null) => Grant(ip, area, z, session);

    // Spec overload: AddChain(key, List<Coord> chain) — coordinate chain version
    public static void AddChain(string key, List<Coord> chain, Session? session = null)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (chain == null) throw new ArgumentNullException(nameof(chain));
        Lock.EnterWriteLock();
        try
        {
            EvictLocked(GetMonotonic());
            if (_chains.TryGetValue(key, out var existing))
            {
                existing.Chain = new List<Coord>(chain);
                existing.CreatedAt = DateTime.UtcNow;
                existing.CreatedMonotonic = GetMonotonic();
            }
            else
            {
                var c = new MapEditChain(key, "?", "?", 0, session)
                {
                    Chain = new List<Coord>(chain),
                    CreatedAt = DateTime.UtcNow,
                    CreatedMonotonic = GetMonotonic()
                };
                _chains[key] = c;
            }
            if (_chains.Count > EffectiveCap()) EvictLocked(GetMonotonic());
        }
        finally { Lock.ExitWriteLock(); }
    }

    // Port of mapedit.py:73-108 consume(key,ip,seq) -> MapEditResult
    public static MapEditResult Consume(string key, string ip, int seq)
    {
        Lock.EnterWriteLock();
        try
        {
            double now = GetMonotonic();
            EvictLocked(now);
            MapEditChain? chain = null;
            bool previousHit = false;
            if (!_chains.TryGetValue(key, out chain))
            {
                if (_previous.TryGetValue(key, out var cur))
                {
                    if (_chains.TryGetValue(cur, out var c2))
                    {
                        chain = c2;
                        previousHit = true;
                    }
                    else
                    {
                        _previous.Remove(key);
                    }
                }
            }
            if (chain == null)
                return new MapEditResult(MapEditStatus.Reject, reason: "unknown_key");
            if (chain.Ip != ip)
                return new MapEditResult(MapEditStatus.Reject, reason: "ip");
            if (previousHit)
            {
                if (seq == chain.Seq)
                    return new MapEditResult(MapEditStatus.Retry, newKey: chain.Key, chain: chain);
                return new MapEditResult(MapEditStatus.Reject, reason: "replay");
            }
            if (seq == chain.Seq + 1)
            {
                // Port of mapedit.py:96-105 rotate key
                string newKey;
                int attempts = 0;
                do
                {
                    newKey = GenerateToken();
                    attempts++;
                    if (attempts > 100) throw new InvalidOperationException("Failed to generate new key");
                } while (_chains.ContainsKey(newKey) || _previous.ContainsKey(newKey));
                string oldKey = chain.Key;
                _chains.Remove(oldKey);
                _previous.Remove(oldKey);
                chain.PreviousKey = oldKey;
                chain.Key = newKey;
                chain.Seq = seq;
                chain.CreatedAt = DateTime.UtcNow;
                chain.CreatedMonotonic = GetMonotonic();
                _chains[newKey] = chain;
                _previous[oldKey] = newKey;
                return new MapEditResult(MapEditStatus.Processed, newKey: newKey, chain: chain);
            }
            if (seq <= chain.Seq)
                return new MapEditResult(MapEditStatus.Reject, reason: "replay");
            return new MapEditResult(MapEditStatus.Reject, reason: "gap");
        }
        finally { Lock.ExitWriteLock(); }
    }

    // Port of spec: GetChain
    public static MapEditChain? GetChain(string key)
    {
        Lock.EnterReadLock();
        try
        {
            if (_chains.TryGetValue(key, out var c)) return c;
            if (_previous.TryGetValue(key, out var cur) && _chains.TryGetValue(cur, out var c2)) return c2;
            return null;
        }
        finally { Lock.ExitReadLock(); }
    }

    // Port of mapedit.discard_session — drop all chains owned by a closed session.
    public static void DiscardSession(Session? session)
    {
        if (session == null) return;
        Lock.EnterWriteLock();
        try
        {
            var dead = new List<string>();
            foreach (var kv in _chains)
                if (ReferenceEquals(kv.Value.Session, session)) dead.Add(kv.Key);
            foreach (var k in dead)
            {
                var c = _chains[k];
                _chains.Remove(k);
                if (c != null && !string.IsNullOrEmpty(c.PreviousKey))
                    _previous.Remove(c.PreviousKey);
            }
            var stale = new List<string>();
            foreach (var kv in _previous) if (!_chains.ContainsKey(kv.Value)) stale.Add(kv.Key);
            foreach (var k in stale) _previous.Remove(k);
        }
        finally { Lock.ExitWriteLock(); }
    }

    // Port of spec: ValidateChain — faithful boolean check (does not consume)
    public static bool ValidateChain(string key)
    {
        var c = GetChain(key);
        if (c == null) return false;
        return true;
    }

    // Overload validating ip/seq without rotating (advisory)
    public static bool ValidateChain(string key, string ip, int seq)
    {
        Lock.EnterReadLock();
        try
        {
            MapEditChain? chain = null;
            bool previousHit = false;
            if (!_chains.TryGetValue(key, out chain))
            {
                if (_previous.TryGetValue(key, out var cur) && _chains.TryGetValue(cur, out var c2))
                {
                    chain = c2;
                    previousHit = true;
                }
                else return false;
            }
            if (chain == null) return false;
            if (chain.Ip != ip) return false;
            if (previousHit) return seq == chain.Seq;
            if (seq == chain.Seq + 1) return true;
            return false;
        }
        finally { Lock.ExitReadLock(); }
    }

    // Clear stale previous-key mappings and enforce the cap (no expiry).
    public static void ClearStale()
    {
        Lock.EnterWriteLock();
        try { EvictLocked(GetMonotonic()); }
        finally { Lock.ExitWriteLock(); }
    }

    // Port of spec: RemoveChain
    public static bool RemoveChain(string key)
    {
        Lock.EnterWriteLock();
        try
        {
            bool removed = false;
            if (_chains.Remove(key)) removed = true;
            // Remove previous mapping where value == key
            var toRemove = new List<string>();
            foreach (var kv in _previous)
                if (kv.Value == key) toRemove.Add(kv.Key);
            foreach (var k in toRemove) { _previous.Remove(k); removed = true; }
            // If key itself is a previous key
            if (_previous.Remove(key)) removed = true;
            // Clean stale
            var stale = new List<string>();
            foreach (var kv in _previous) if (!_chains.ContainsKey(kv.Value)) stale.Add(kv.Key);
            foreach (var k in stale) _previous.Remove(k);
            return removed;
        }
        finally { Lock.ExitWriteLock(); }
    }

    // For tests / reset
    public static void ResetForTesting()
    {
        Lock.EnterWriteLock();
        try
        {
            _chains.Clear();
            _previous.Clear();
        }
        finally { Lock.ExitWriteLock(); }
    }

    // Helpers for direct spec's List<Coord> chain validation
    public static bool ValidateCoordChain(string key, List<Coord> expected)
    {
        var c = GetChain(key);
        if (c == null) return false;
        if (c.Chain.Count != expected.Count) return false;
        for (int i = 0; i < expected.Count; i++) if (!c.Chain[i].Equals(expected[i])) return false;
        return true;
    }
}
