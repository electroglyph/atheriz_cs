// Port of atheriz/globals/get.py:176 — centralized lazy singleton getters with RWL double-checked locking.
// Mirrors _SINGLETON_LOCK=RLock, _ID_LOCK, get_* functions.
// In C# we use ReaderWriterLockSlim(SupportsRecursion) for re-entrancy (Python RLock allows getter calling another getter).
using Atheriz.Core.Commands;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Globals;

// Port of atheriz/globals/get.py:44 _SINGLETON_LOCK
public static class GlobalServices
{
    // Port of get.py:44 _SINGLETON_LOCK = RLock()
    private static readonly ReaderWriterLockSlim _singletonLock = new(LockRecursionPolicy.SupportsRecursion);

    // Port of get.py:19-28 lazy holders
    private static AsyncThreadPool? _asyncThreadPool; // Port of get.py:19 _ASYNC_THREAD_POOL
    private static AsyncTicker? _asyncTicker; // Port of get.py:25 _ASYNC_TICKER
    private static NodeHandler? _nodeHandler; // Port of get.py:22 _NODE_HANDLER
    private static MapHandler? _mapHandler; // Port of get.py:23 _MAP_HANDLER
    private static GameTime? _gameTime; // Port of get.py:27 _GAME_TIME
    private static GameObject? _serverChannel; // Port of get.py:24 _SERVER_CHANNEL
    private static CmdSet? _loggedInCmdSet; // Port of get.py:21 _LOGGEDIN_CMDSET
    private static CmdSet? _unloggedInCmdSet; // Port of get.py:20 _UNLOGGEDIN_CMDSET
    private static ConnectionManager? _connectionManager; // Port of get.py:26 _CONNECTION_MANAGER

    // Port of get.py:47-66 get_id/set_id/get_unique_id via IdGenerator (_ID_LOCK)
    public static int GetId() => IdGenerator.GetId(); // Port of get.py:47 get_id
    public static void SetId(int id) => IdGenerator.SetId(id); // Port of get.py:54 set_id
    public static int GetUniqueId() => IdGenerator.GetUniqueId(); // Port of get.py:61 get_unique_id

    // Port of get.py:44 helper — double-checked locking with RWL (re-entrant)
    private static T GetOrCreateSingleton<T>(ref T? field, Func<T> factory) where T : class
    {
        var snap = Volatile.Read(ref field);
        if (snap != null) return snap;
        _singletonLock.EnterUpgradeableReadLock();
        try
        {
            if (field != null) return field;
            _singletonLock.EnterWriteLock();
            try { if (field == null) field = factory(); return field!; }
            finally { _singletonLock.ExitWriteLock(); }
        }
        finally { _singletonLock.ExitUpgradeableReadLock(); }
    }

    // Port of get.py:149-156 get_async_threadpool
    public static AsyncThreadPool GetAsyncThreadPool() => GetOrCreateSingleton(ref _asyncThreadPool, () =>
    {
        var settings = AtherizSettings.Global;
        int limit = settings.ThreadpoolLimit ?? Environment.ProcessorCount;
        if (limit < 1) limit = 1;
        return new AsyncThreadPool(
            maxThreads: limit,
            queueLimit: settings.ThreadpoolQueueLimit,
            reliefLimit: settings.ThreadpoolReliefLimit,
            watchdogSeconds: TimeSpan.FromSeconds(settings.ThreadpoolWatchdogSeconds),
            watchdogInterval: TimeSpan.FromSeconds(settings.ThreadpoolWatchdogInterval));
    });

    // Port of get.py:89-96 get_async_ticker
    public static AsyncTicker GetAsyncTicker() => GetOrCreateSingleton(ref _asyncTicker, () =>
    {
        var pool = GetAsyncThreadPool();
        return new AsyncTicker(pool);
    });

    // Port of get.py:169-176 get_node_handler
    public static NodeHandler GetNodeHandler() => GetOrCreateSingleton(ref _nodeHandler, () => new NodeHandler(autoLoad: true));

    // Port of get.py:129-136 get_map_handler
    public static MapHandler GetMapHandler() => GetOrCreateSingleton(ref _mapHandler, () =>
    {
        var settings = AtherizSettings.Global;
        return new MapHandler(settings, autoLoad: true);
    });

    // Port of get.py:69-76 get_game_time
    public static GameTime GetGameTime() => GetOrCreateSingleton(ref _gameTime, () =>
    {
        var settings = AtherizSettings.Global;
        var ticker = _asyncTicker;
        var pool = _asyncThreadPool;
        if (ticker != null || pool != null)
            return new GameTime(settings, ticker, pool, autoLoad: true);
        return new GameTime(settings, autoLoad: true);
    });

    // Port of get.py:99-126 get_server_channel filtering is_channel && name=="server" && !is_deleted
    public static GameObject? GetServerChannel()
    {
        _singletonLock.EnterUpgradeableReadLock();
        try
        {
            if (_serverChannel != null)
            {
                bool isDel = false;
                string name = "";
                try { isDel = _serverChannel.IsDeleted; } catch { isDel = true; }
                try { name = _serverChannel.Name ?? ""; } catch { name = ""; }
                bool nameOk;
                try { nameOk = name.ToLowerInvariant() == "server"; } catch { nameOk = false; }
                if (isDel || !nameOk)
                {
                    _singletonLock.EnterWriteLock();
                    try { _serverChannel = null; }
                    finally { _singletonLock.ExitWriteLock(); }
                }
                else
                {
                    return _serverChannel;
                }
            }
            _singletonLock.EnterWriteLock();
            try
            {
                // Re-check after upgrade
                if (_serverChannel != null)
                {
                    bool isDel = false;
                    string name = "";
                    try { isDel = _serverChannel.IsDeleted; } catch { isDel = true; }
                    try { name = _serverChannel.Name ?? ""; } catch { name = ""; }
                    bool nameOk = false;
                    try { nameOk = name.ToLowerInvariant() == "server"; } catch { }
                    if (!isDel && nameOk) return _serverChannel;
                    _serverChannel = null;
                }
                // Port of get.py:117-126 filter_by lambda is_channel && name=="server" && not is_deleted
                var c = ObjectRegistry.FilterBy(o =>
                    o.IsChannel && (o.Name != null && o.Name.ToLowerInvariant() == "server") && !o.IsDeleted);
                if (c.Count > 0)
                {
                    _serverChannel = c[0];
                }
                else
                {
                    Console.Error.WriteLine("Server channel not found."); // Port of logger.error at get.py:125
                    _serverChannel = null;
                }
                return _serverChannel;
            }
            finally { _singletonLock.ExitWriteLock(); }
        }
        finally { _singletonLock.ExitUpgradeableReadLock(); }
    }

    // Port of get.py:139-146 get_loggedin_cmdset
    public static CmdSet GetLoggedInCmdSet() => GetOrCreateSingleton(ref _loggedInCmdSet, () => CommandRegistry.LoggedIn);

    // Port of get.py:159-166 get_unloggedin_cmdset
    public static CmdSet GetUnloggedInCmdSet() => GetOrCreateSingleton(ref _unloggedInCmdSet, () => CommandRegistry.UnloggedIn);

    // Port of get.py:79-86 get_connection_manager
    public static ConnectionManager GetConnectionManager() => GetOrCreateSingleton(ref _connectionManager, () =>
    {
        var cm = ConnectionManager.GlobalInstance ?? new ConnectionManager();
        ConnectionManager.GlobalInstance = cm;
        return cm;
    });

    // Overload allowing caller-provided pool/settings (for Startup wiring)
    public static ConnectionManager GetConnectionManager(AtherizSettings settings, AsyncThreadPool pool)
    {
        var snap = Volatile.Read(ref _connectionManager);
        if (snap != null) return snap;
        _singletonLock.EnterWriteLock();
        try
        {
            if (_connectionManager == null)
            {
                _connectionManager = ConnectionManager.GlobalInstance ?? new ConnectionManager(pool, settings);
                ConnectionManager.GlobalInstance = _connectionManager;
            }
            return _connectionManager;
        }
        finally { _singletonLock.ExitWriteLock(); }
    }

    // Port of startstop.py:78-81 clearing singletons on shutdown: _ASYNC_THREAD_POOL=None etc
    internal static void ClearForShutdown()
    {
        _singletonLock.EnterWriteLock();
        try
        {
            _asyncThreadPool = null;
            _asyncTicker = null;
            _connectionManager = null;
            // Note: Python clears only those three under _SINGLETON_LOCK; we also clear channel cache lazily on next call
            // For completeness also clear channel cache
            _serverChannel = null;
        }
        finally { _singletonLock.ExitWriteLock(); }
    }

    // For tests / reset — clears all holders
    public static void ResetForTesting()
    {
        _singletonLock.EnterWriteLock();
        try
        {
            _asyncThreadPool = null;
            _asyncTicker = null;
            _nodeHandler = null;
            _mapHandler = null;
            _gameTime = null;
            _serverChannel = null;
            _loggedInCmdSet = null;
            _unloggedInCmdSet = null;
            _connectionManager = null;
        }
        finally { _singletonLock.ExitWriteLock(); }
        // Also reset underlying registries that are not singletons but global
        try { CommandRegistry.ResetForTesting(); } catch { }
        try { ConnectionManager.GlobalInstance = null; } catch { }
    }

    public static AsyncTicker? TryGetTicker()
    {
        try { var snap = Volatile.Read(ref _asyncTicker); return snap; } catch { return null; }
    }
    public static AsyncThreadPool? TryGetPool()
    {
        try { var snap = Volatile.Read(ref _asyncThreadPool); return snap; } catch { return null; }
    }
    public static GameTime? TryGetGameTime()
    {
        try { var snap = Volatile.Read(ref _gameTime); return snap; } catch { return null; }
    }

    // Typed singleton override (F001: replaces GlobalServices._nodeHandler/_mapHandler
    // reflection writes in MazeCommand). Same-lock assignment, no behavior change.
    public static void SetNodeHandler(NodeHandler nh)
    {
        _singletonLock.EnterWriteLock();
        try { _nodeHandler = nh; }
        finally { _singletonLock.ExitWriteLock(); }
    }
    public static void SetMapHandler(MapHandler mh)
    {
        _singletonLock.EnterWriteLock();
        try { _mapHandler = mh; }
        finally { _singletonLock.ExitWriteLock(); }
    }

    // Expose lock for StartStop faithful clearing (mirrors get_singleton._SINGLETON_LOCK)
    public static ReaderWriterLockSlim SingletonLock => _singletonLock;
}
