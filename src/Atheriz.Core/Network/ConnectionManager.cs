using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using Atheriz.Core.Concurrency;
using InputHandler = System.Action<Atheriz.Core.Network.BaseConnection, System.Collections.Generic.List<object?>, System.Collections.Generic.Dictionary<string, object?>>;

namespace Atheriz.Core.Network;

// Manages all connections and orchestrates message handling across protocols.
// Replaces older WebSocketManager to be protocol-agnostic.
// Line-number comments reference manager.py original.

/// <summary>
/// </summary>
public class ConnectionManager
{
// now via ThrottleWindow.
    // Per-manager state: a static holder would share per-host suppression
    // across test and game worlds, so a burst in one silences another.
    private readonly ThrottledLog _malformedLog = new(MalformedWindow);
    private const double MalformedWindow = 5.0;

    private static string SummarizeRaw(string rawMessage, int limit = 80)
    {
        var sub = rawMessage.Length > limit ? rawMessage.Substring(0, limit) : rawMessage;
        // Approximation of Python repr(sub) — quoted string with escapes
        return JsonSerializer.Serialize(sub);
    }

    private bool ShouldLogMalformed(string host)
        => _malformedLog.ShouldLog(host);

    // for the shared HandleCommand size cap .
    private readonly ThrottledLog _oversizeLog = new(OversizeWindow);
    private const double OversizeWindow = 5.0;
    /// <summary>
    /// Per-host 5s oversize-log throttle for this manager's world. Public so
    /// the hosting layer (one static entry point, no instance of its own)
    /// shares this world's budget instead of a process-wide static.
    /// </summary>
    public bool ShouldLogOversize(string host)
        => _oversizeLog.ShouldLog(host);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, BaseConnection> _connections = new();
    // Reverse index keyed by reference: BaseConnection does not override
    // Equals, so the default comparer is already reference equality.
    private readonly ConcurrentDictionary<BaseConnection, string> _connToId = new();
    private readonly Dictionary<string, int> _perIpCounts = new();
    // orphan-sweep timer (started lazily on first registration).
    // Reaping rides a 60s wall-clock cadence, not registration traffic.
    private System.Threading.Timer? _orphanSweepTimer;
    private int _sweepStarted;
    private readonly Dictionary<string, InputHandler> _messageHandlers = new(StringComparer.OrdinalIgnoreCase);
    private int _connectionCounter;

    public AsyncThreadPool Atp { get; }
    public InputFuncs InputFuncs { get; }
    private readonly AtherizSettings _settings;

    // Global singleton — mirrors get_connection_manager() at globals/get.py:79-86
    private static ConnectionManager? _globalInstance;
    private static readonly Lock _globalLock = new();
    public static ConnectionManager? GlobalInstance
    {
        get { lock (_globalLock) return _globalInstance; }
        set { lock (_globalLock) _globalInstance = value; }
    }

    public ConnectionManager(AsyncThreadPool? pool = null, AtherizSettings? settings = null, InputFuncs? inputFuncs = null)
    {
        _settings = settings ?? AtherizSettings.Global;
        Atp = pool ?? new AsyncThreadPool(
            maxThreads: _settings.ThreadpoolLimit,
            queueLimit: _settings.ThreadpoolQueueLimit,
            reliefLimit: _settings.ThreadpoolReliefLimit,
            watchdogSeconds: TimeSpan.FromSeconds(_settings.ThreadpoolWatchdogSeconds),
            watchdogInterval: TimeSpan.FromSeconds(_settings.ThreadpoolWatchdogInterval));
        InputFuncs = inputFuncs ?? new InputFuncs();
        foreach (var kv in InputFuncs.GetHandlers())
            RegisterHandler(kv.Key, kv.Value);

        lock (_globalLock) _globalInstance ??= this;
    }

    // no manager lock: Interlocked owns the increment.
    public virtual string GenerateConnectionId()
    {
        var n = Interlocked.Increment(ref _connectionCounter);
        return $"conn_{n}";
    }

    // pre-spawn admission probe. Mirrors the RegisterConnection
    // gates (ban + per-IP + total) without creating a connection, so accept
    // loops can refuse floods before queueing handler tasks. Authoritative
    // enforcement stays in RegisterConnection (counts can shift between
    // probe and register); a refused probe only avoids the spawn.
    public bool ShouldRefusePreSpawn(string host)
    {
        if (ObjectRegistry.IsIpBanned(host)) return true;
        lock (_lock)
        {
            var limit = _settings.MaxConnectionsPerIp;
            if (limit > 0 && host != "?" && _perIpCounts.TryGetValue(host, out var cnt) && cnt >= limit)
                return true;
            if (_settings.MaxTotalConnections > 0 && _connections.Count >= _settings.MaxTotalConnections)
                return true;
            return false;
        }
    }

    // Registration-time host for per-IP accounting and disconnect: the
    // snapshot taken at register time, else the live value, else "?".
    private static string HostOf(BaseConnection c) => c.RegisteredHost ?? c.ClientHost ?? "?";

    // refusal teardown runs outside the manager write lock (see
    // RefuseConnection). Close() does task/socket work that used to stall
    // every register/disconnect/count op while the lock was held.
    public virtual bool RegisterConnection(string connId, BaseConnection connection)
    {
        var host = connection.ClientHost ?? "?";
        var limit = _settings.MaxConnectionsPerIp;
        string? refusal = null;
        lock (_lock)
        {
            // Same object re-registering under a new id: its stale id must be
            // evicted, or one socket holds two slots (double per-IP count and
            // an orphaned id on disconnect). Decided here, applied only on
            // admission below — a refused re-register must not destroy the
            // live previous registration. The stale slot's host is the
            // previously recorded one (RegisteredHost is overwritten after).
            string? evictId = null;
            if (_connToId.TryGetValue(connection, out var prevId) && prevId != connId
                && _connections.TryGetValue(prevId, out var prevStored) && ReferenceEquals(prevStored, connection))
                evictId = prevId;
            var evictHost = evictId is not null ? (connection.RegisteredHost ?? host) : null;
            if (ObjectRegistry.IsIpBanned(host))
                refusal = $"[Network] Refusing connection from banned host {host}";
            else if (limit > 0 && host != "?")
            {
                var sameHost = _perIpCounts.TryGetValue(host, out var cnt) ? cnt : 0;
                // if overwriting same conn_id, don't count itself twice
                if (_connections.TryGetValue(connId, out var existing) && HostOf(existing) == host)
                    sameHost--;
                // the pending eviction frees one same-host slot on admission
                if (evictHost == host)
                    sameHost--;
                if (sameHost >= limit)
                    refusal = $"[Network] Refusing connection from {host}: per-IP limit ({limit}) reached";
            }
            // Total-connection admission cap (0 = unlimited). Checked after the
            // per-IP gate so the refusal reason stays specific. The pending
            // eviction nets out on admission, so it is discounted here.
            if (refusal is null && _settings.MaxTotalConnections > 0 && _connections.Count - (evictId is not null ? 1 : 0) >= _settings.MaxTotalConnections)
                refusal = $"[Network] Refusing connection from {host}: total limit ({_settings.MaxTotalConnections}) reached";
            connection.RegisteredHost = host;
            if (refusal is null)
            {
            if (evictId is not null)
            {
                _connections.Remove(evictId);
                if (evictHost != "?" && evictHost is not null)
                {
                    var evictCnt = _perIpCounts.TryGetValue(evictHost, out var ev) ? ev - 1 : -1;
                    if (evictCnt <= 0) _perIpCounts.Remove(evictHost);
                    else _perIpCounts[evictHost] = evictCnt;
                }
                _connToId.TryRemove(connection, out _);
            }
            // Re-registering the same conn id from the same host replaces the same
            // registration, so it must not increment the per-IP counter again —
            // double-counting leaks the bucket toward a false limit refusal.
            var sameHostReregister = false;
            // handle overwrite: adjust old host count
            if (_connections.TryGetValue(connId, out var old))
            {
                var oldHost = HostOf(old);
                if (oldHost == host) sameHostReregister = true;
                else if (oldHost != "?" && oldHost != host)
                {
                    var cnt = _perIpCounts.TryGetValue(oldHost, out var c) ? c - 1 : -1;
                    if (cnt <= 0) _perIpCounts.Remove(oldHost);
                    else _perIpCounts[oldHost] = cnt;
                }
                _connToId.TryRemove(old, out _);
            }
            _connections[connId] = connection;
            _connToId[connection] = connId;
            if (host != "?" && !sameHostReregister)
                _perIpCounts[host] = _perIpCounts.TryGetValue(host, out var v) ? v + 1 : 1;
            }
        }
        if (refusal is not null)
        {
            RefuseConnection(connection, refusal);
            return false;
        }
        try { Atheriz.Core.AtherizLogger.LogInformation($"[Network] Connection opened: {connId} (total: {ConnectionCount})"); } catch { Console.Error.WriteLine($"[Network] Connection opened: {connId} (total: {ConnectionCount})"); }
        // timer-driven orphan sweep. The old every-50th-registration
        // amortization never reaped on a low-traffic server; the WS receive
        // path has no idle timeout of its own, so this 60s cadence covers
        // abandoned pre-login sockets on both transports. Starts lazily so
        // short-lived/test instances pay nothing.
        if (System.Threading.Interlocked.CompareExchange(ref _sweepStarted, 1, 0) == 0)
        {
            try { _orphanSweepTimer = new System.Threading.Timer(_ => { try { SweepOrphanedConnections(TimeSpan.FromMinutes(5)); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager orphan sweep: " + logEx.Message, "ConnectionManager"); } }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.RegisterConnection: " + logEx.Message, "ConnectionManager"); }
        }
        return true;
    }

    // refusal teardown. Runs after the manager write lock releases —
    // Close() does task/socket work that must not stall concurrent
    // register/disconnect/count ops. Messages mirror the old inline refuses.
    private static void RefuseConnection(BaseConnection connection, string reason)
    {
        try { Atheriz.Core.AtherizLogger.LogWarning(reason); } catch { Console.Error.WriteLine(reason); }
        try { connection.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.RefuseConnection: " + logEx.Message, "ConnectionManager"); }
    }

    /// <summary>
    /// Disconnects sockets that never attached an account/puppet and are older
    /// than <paramref name="maxPreLoginAge"/>. Returns the number swept.
    /// Same-account duplicate gating is intentionally absent (the login layer
    /// owns session replacement); this only reaps abandoned pre-login sockets.
    /// </summary>
    public int SweepOrphanedConnections(TimeSpan maxPreLoginAge)
    {
        var cutoff = DateTime.UtcNow - maxPreLoginAge;
        List<BaseConnection> stale = [];
        foreach (var c in ConnectionsSnapshot.Values)
        {
            try
            {
                var s = c.Session;
                // Read under session.Lock — the login/puppet path mutates
                // Puppet/Account under it, so an unlocked read can observe a
                // stale null and nominate a just-logged-in connection.
                if (s is not null)
                {
                    lock (s.Lock)
                    {
                        if (s.Puppet is not null || s.Account is not null) continue;
                    }
                }
                if (c.ConnectedAtUtc > cutoff) continue;
                stale.Add(c);
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.SweepOrphanedConnections: " + logEx.Message, "ConnectionManager"); }
        }
        int swept = 0;
        foreach (var c in stale)
        {
            // Re-validate under session.Lock immediately before
            // Disconnect — a login landing between the scan above and now
            // must not be reaped (TOCTOU).
            try
            {
                var s = c.Session;
                if (s is not null)
                {
                    lock (s.Lock)
                    {
                        if (s.Puppet is not null || s.Account is not null) continue;
                    }
                }
                Disconnect(c); swept++;
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.SweepOrphanedConnections: " + logEx.Message, "ConnectionManager"); }
        }
        return swept;
    }

    public virtual void Disconnect(BaseConnection connection)
    {
        string? connId = null;
        // Host snapshot belongs inside the manager lock: RegisteredHost is
        // rewritten by RegisterConnection under the same lock, so reading it
        // outside can pair this disconnect's counter decrement with the next
        // connection's host.
        string host = "?";
        lock (_lock)
        {
            host = HostOf(connection);
            if (_connToId.TryGetValue(connection, out var id))
            {
                connId = id;
                _connToId.TryRemove(connection, out _);
                if (_connections.TryGetValue(connId, out var stored) && ReferenceEquals(stored, connection))
                {
                    _connections.Remove(connId);
                    if (host != "?")
                    {
                        var cnt = _perIpCounts.TryGetValue(host, out var c) ? c - 1 : -1;
                        if (cnt <= 0) _perIpCounts.Remove(host);
                        else _perIpCounts[host] = cnt;
                    }
                }
            }
            // Orphan sweep: no other id may still point at this same object
            // (each such slot leaked a per-IP count toward a false limit
            // refusal). Normally impossible — Register evicts the stale id —
            // but a disconnect must leave no dangling slot behind.
            List<string>? orphans = null;
            foreach (var kv in _connections)
            {
                if (ReferenceEquals(kv.Value, connection))
                    (orphans ??= new()).Add(kv.Key);
            }
            if (orphans is not null)
            {
                foreach (var oid in orphans)
                {
                    _connections.Remove(oid);
                    if (host != "?")
                    {
                        var ocnt = _perIpCounts.TryGetValue(host, out var oc) ? oc - 1 : -1;
                        if (ocnt <= 0) _perIpCounts.Remove(host);
                        else _perIpCounts[host] = ocnt;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(connId)) return;

        // SetDisconnected locks internally; no outer connection lock needed.
        connection.SetDisconnected(true);
        connection.ClearPendingInput();
        var session = connection.Session;
        if (session is not null)
        {
            // Fire-and-forget by design: disconnect() executes on the network
            // event loop and must not block on teardown (pinned by
            // DisconnectDoesNotBlockOnSlowTeardown: 0.5s teardown, <0.5s return).
            // Pool saturated but alive: never run teardown inline —
            // re-schedule with a short delay (same pattern as GameTime
            // alarms); the delayed task re-queues onto the pool once it
            // drains. Pool STOPPED: the delay would drop the teardown
            // silently (Delay never fires on a stopped pool), losing
            // session/puppet cleanup with only a log line — run it inline
            // instead . No event-loop throughput is left to protect
            // on a stopped pool.
            bool queued = false;
            try { queued = Atp.AddTask(() => DoSessionDisconnect(session)); }
            catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown could not be queued during disconnect: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown could not be queued during disconnect: {e}"); } }
            if (!queued)
            {
                bool stopped = false;
                try { stopped = Atp.IsStopped; } catch { }
                if (stopped) DoSessionDisconnect(session);
                else
                {
                    try { Atp.Delay(0.05, () => DoSessionDisconnect(session)); }
                    catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown could not be deferred during disconnect: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown could not be deferred during disconnect: {e}"); } }
                }
            }
        }
        try { connection.Close(); }
        catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Connection cleanup failed: {e}"); } catch { Console.Error.WriteLine($"[Network] Connection cleanup failed: {e}"); } }
        try { (connection as IDisposable)?.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.Disconnect: " + logEx.Message, "ConnectionManager"); }
        try { Atheriz.Core.AtherizLogger.LogInformation($"[Network] Connection closed: {connId} (total: {ConnectionCount})"); } catch { Console.Error.WriteLine($"[Network] Connection closed: {connId} (total: {ConnectionCount})"); }
    }

    private void DoSessionDisconnect(Session session)
    {
        try { session.AtDisconnect(); }
        catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown failed: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown failed: {e}"); } }
    }

    public int ConnectionCount
    {
        get { lock (_lock) return _connections.Count; }
    }

    public List<BaseConnection> GetAllConnections()
    {
        lock (_lock)
        {
            return _connections.Values.ToList();
        }
    }

    public void Broadcast(string text)
    {
        var connections = GetAllConnections();
        foreach (var conn in connections)
        {
            try { conn.Msg(text); }
            catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Broadcast error: {e}"); } catch { Console.Error.WriteLine($"[Network] Broadcast error: {e}"); } }
        }
    }

    public void RegisterHandler(string messageType, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
// exact match only.
        if (handler is not InputHandler typed)
            throw new ArgumentException($"Input handler for '{messageType}' must be Action<BaseConnection, List<object?>, Dictionary<string, object?>>.", nameof(handler));
        lock (_lock)
        {
            _messageHandlers[messageType] = typed;
        }
    }

    private static object? StripInputValue(object? value)
    {
        if (value is string s) return GameUtils.StripTerminalEscapes(s);
        if (value is List<object?> lst)
        {
            var stripped = new List<object?>(lst.Count);
            foreach (var item in lst)
                stripped.Add(StripInputValue(item));
            return stripped;
        }
        if (value is Dictionary<string, object?> dict)
        {
            Dictionary<string, object?> res = [];
            foreach (var kv in dict) res[kv.Key] = StripInputValue(kv.Value);
            return res;
        }
        return value;
    }

    internal static void NetWarn(string message)
    { try { AtherizLogger.LogWarning(message); } catch { Console.Error.WriteLine(message); } }
    internal static void NetError(string message)
    { try { AtherizLogger.LogError(message); } catch { Console.Error.WriteLine(message); } }
    private void LogMalformed(string host, string rawMessage)
    { if (ShouldLogMalformed(host)) NetWarn($"[Network] Invalid message format from {host} ({System.Text.Encoding.UTF8.GetByteCount(rawMessage)} bytes): {SummarizeRaw(rawMessage)}"); }

    public virtual void HandleCommand(BaseConnection connection, string rawMessage)
    {
        try
        {
            // size cap precedes the parse. WebsocketMaxMessageSize was
            // enforced only at the WS edge (WebSocketProtocol); direct
            // callers of this shared entry point could force a large parse.
            int maxMessageSize = _settings.WebsocketMaxMessageSize;
            // Byte budget, not char count: Length is only a fast prefilter
            // (bytes always >= chars, so Length-over already refuses); a
            // short multibyte string can still carry 4x the nominal bytes.
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(rawMessage);
            if (rawMessage.Length > maxMessageSize || byteCount > maxMessageSize)
            {
                var oversizeHost = connection.ClientHost ?? "?";
                if (ShouldLogOversize(oversizeHost))
                    NetWarn($"[Network] Message too large from {oversizeHost} ({byteCount} bytes > {maxMessageSize} bytes)");
                return;
            }
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1)
            {
                var host = connection.ClientHost ?? "?";
                LogMalformed(host, rawMessage);
                return;
            }
            var cmdElement = root[0];
            // GetString() would throw for numbers/arrays, routing them to the
            // error path instead of the malformed path. Reject them cleanly.
            if (cmdElement.ValueKind != JsonValueKind.String)
            {
                LogMalformed(connection.ClientHost ?? "?", rawMessage);
                return;
            }
            var cmd = cmdElement.GetString()!;
            List<object?> args = new();
            Dictionary<string, object?> kwargs = new();
            if (root.GetArrayLength() > 1) args = JsonElementToObject(root[1]) as List<object?> ?? new();
            if (root.GetArrayLength() > 2) kwargs = JsonElementToObject(root[2]) as Dictionary<string, object?> ?? new();

            Dispatch(connection, cmd, args, kwargs);
        }
        catch (JsonException exc)
        {
            var host = connection.ClientHost ?? "?";
            if (ShouldLogMalformed(host))
                NetWarn($"[Network] Error decoding JSON from {host} ({rawMessage.Length} bytes): {exc.Message} at position {exc.BytePositionInLine}: {SummarizeRaw(rawMessage)}");
        }
        catch (Exception e)
        {
            NetError($"[Network] Error handling message: {e}");
        }
    }

    public void Dispatch(BaseConnection connection, string cmd, List<object?> args, Dictionary<string, object?> kwargs)
    {
        // Handlers run on game threadpool via connection's serialized input queue — manager.py:218-221
        if (_settings.StripInputEscapeSequences)
        {
            var strippedArgs = new List<object?>(args.Count);
            foreach (var item in args)
                strippedArgs.Add(StripInputValue(item));
            args = strippedArgs;
            var boxed = StripInputValue(kwargs);
            if (boxed is Dictionary<string, object?> d) kwargs = d;
        }
        InputHandler? handler = null;
        lock (_lock)
        {
            _messageHandlers.TryGetValue(cmd, out handler);
        }
        if (handler is not null)
        {
            connection.EnqueueInput(handler, args, kwargs);
        }
        else
        {
            try { Atheriz.Core.AtherizLogger.LogDebug($"Unknown command: {cmd}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.Dispatch: " + logEx.Message, "ConnectionManager"); }
        }
    }

    // Helpers to convert JsonElement to List/Dict of objects: single family below.

    internal static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => ToJsonNumber(el),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => ConvertArray(el),
            JsonValueKind.Object => ConvertDict(el),
            _ => null
        };

        // Array/object recursion lives here, inside the single converter family.
        static List<object?> ConvertArray(JsonElement a)
        {
            List<object?> list = [];
            foreach (var item in a.EnumerateArray()) list.Add(JsonElementToObject(item));
            return list;
        }

        static Dictionary<string, object?> ConvertDict(JsonElement o)
        {
            Dictionary<string, object?> dict = [];
            foreach (var prop in o.EnumerateObject()) dict[prop.Name] = JsonElementToObject(prop.Value);
            return dict;
        }
    }

    // Number conversion that preserves the narrowest fitting type: a chained
    // ternary would unify int/long/double to double and silently widen every
    // integer (breaking `is int` checks downstream), so plain if/returns box
    // each arm exactly.
    private static object ToJsonNumber(System.Text.Json.JsonElement el)
    {
        if (el.TryGetInt32(out var i)) return i;
        if (el.TryGetInt64(out var l)) return l;
        return el.GetDouble();
    }

    // For tests / introspection — expose internal state counts similar to Python's _connections
    public IReadOnlyDictionary<string, BaseConnection> ConnectionsSnapshot
    {
        get { lock (_lock) return new Dictionary<string, BaseConnection>(_connections); }
    }
}
