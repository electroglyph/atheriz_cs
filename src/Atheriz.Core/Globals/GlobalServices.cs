// Port of atheriz/globals/get.py:176 — centralized lazy singleton getters with RWL double-checked locking.
// Mirrors _SINGLETON_LOCK=RLock, _ID_LOCK, get_* functions.
// In C# we use ReaderWriterLockSlim(SupportsRecursion) for re-entrancy (Python RLock allows getter calling another getter).
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;

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
        if (snap is not null) return snap;
        _singletonLock.EnterUpgradeableReadLock();
        try
        {
            if (field is not null) return field;
            _singletonLock.EnterWriteLock();
            try { if (field is null) field = factory(); return field!; }
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
    // the singleton owner publishes itself as current explicitly
    // (the ctor no longer hijacks it).
    // Null settings means ambient: the parameterless overload keeps the
    // ambient Load() path instead of passing Global explicitly, preserving
    // the ambient-vs-explicit DB-resolution difference.
    private static NodeHandler CreateNodeHandler(AtherizSettings? settings)
    {
        var h = settings is null ? new NodeHandler(autoLoad: true) : new NodeHandler(settings, autoLoad: true);
        NodeHandler.SetCurrent(h);
        return h;
    }
    public static NodeHandler GetNodeHandler() => GetOrCreateSingleton(ref _nodeHandler, () => CreateNodeHandler(null));
    // Settings-pinned boot: first creation loads from settings.SavePath instead
    // of the ambient path, so DoStartup(settings) uses one database .
    public static NodeHandler GetNodeHandler(AtherizSettings settings) => GetOrCreateSingleton(ref _nodeHandler, () => CreateNodeHandler(settings));

    private static MapHandler CreateMapHandler(AtherizSettings settings) => new(settings, autoLoad: true);
    // Port of get.py:129-136 get_map_handler
    public static MapHandler GetMapHandler() => GetOrCreateSingleton(ref _mapHandler, () =>
    {
        var settings = AtherizSettings.Global;
        return CreateMapHandler(settings);
    });
    public static MapHandler GetMapHandler(AtherizSettings settings) => GetOrCreateSingleton(ref _mapHandler, () => CreateMapHandler(settings));

    // Port of get.py:69-76 get_game_time
    public static GameTime GetGameTime() => GetOrCreateSingleton(ref _gameTime, () =>
    {
        var settings = AtherizSettings.Global;
        // volatile reads — these fields are written under the
        // singleton lock by Reset/ClearForShutdown on other threads.
        var ticker = Volatile.Read(ref _asyncTicker);
        var pool = Volatile.Read(ref _asyncThreadPool);
        if (ticker is not null || pool is not null)
            return new GameTime(settings, ticker, pool, autoLoad: true);
        return new GameTime(settings, autoLoad: true);
    });
    public static GameTime GetGameTime(AtherizSettings settings) => GetOrCreateSingleton(ref _gameTime, () =>
    {
        var ticker = Volatile.Read(ref _asyncTicker);
        var pool = Volatile.Read(ref _asyncThreadPool);
        if (ticker is not null || pool is not null)
            return new GameTime(settings, ticker, pool, autoLoad: true);
        return new GameTime(settings, autoLoad: true);
    });

    // One choke point for the fast-path and post-upgrade server-channel
    // validation below: same IsDeleted + name checks with the same nested
    // try/catch (a throwing getter fails closed, never live).
    private static bool IsLiveServerChannel(GameObject? c)
    {
        if (c is null) return false;
        bool isDel = false;
        string name = "";
        try { isDel = c.IsDeleted; } catch { isDel = true; }
        try { name = c.Name ?? ""; } catch { name = ""; }
        bool nameOk;
        try { nameOk = name.ToLowerInvariant() == "server"; } catch { nameOk = false; }
        return !isDel && nameOk;
    }

    // Port of get.py:99-126 get_server_channel filtering is_channel && name=="server" && !is_deleted
    public static GameObject? GetServerChannel()
    {
        _singletonLock.EnterUpgradeableReadLock();
        try
        {
            if (_serverChannel is not null)
            {
                if (!IsLiveServerChannel(_serverChannel))
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
                if (_serverChannel is not null)
                {
                    if (IsLiveServerChannel(_serverChannel)) return _serverChannel;
                    _serverChannel = null;
                }
                // Port of get.py:117-126 filter_by lambda is_channel && name=="server" && not is_deleted
                var c = ObjectRegistry.FilterBy(o =>
                    o.IsChannel && (o.Name is not null && o.Name.ToLowerInvariant() == "server") && !o.IsDeleted);
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
        if (snap is not null) return snap;
        _singletonLock.EnterWriteLock();
        try
        {
            if (_connectionManager is null)
            {
                _connectionManager = ConnectionManager.GlobalInstance ?? new ConnectionManager(pool, settings);
                ConnectionManager.GlobalInstance = _connectionManager;
            }
            return _connectionManager;
        }
        finally { _singletonLock.ExitWriteLock(); }
    }

    // Port of startstop.py:78-81 clearing singletons on shutdown: _ASYNC_THREAD_POOL=None etc
    // Call with _singletonLock write held. Clears more than the three Python
    // names: world handlers must release so the next boot reloads instead of
    // resurrecting stale in-memory world, and command sets are rebuilt lazily
    // (reusing them across a world reload keeps references to discarded world).
    private static void ClearHoldersLocked()
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

    internal static void ClearForShutdown()
    {
        _singletonLock.EnterWriteLock();
        try
        {
            ClearHoldersLocked();
        }
        finally { _singletonLock.ExitWriteLock(); }
    }

    // For tests / reset — clears all holders
    public static void Reset()
    {
        _singletonLock.EnterWriteLock();
        try
        {
            ClearHoldersLocked();
        }
        finally { _singletonLock.ExitWriteLock(); }
        // Also reset underlying registries that are not singletons but global
        try { CommandRegistry.Reset(); } catch (Exception) { }
        try { ConnectionManager.GlobalInstance = null; } catch (Exception) { }
    }

    private static T? TryRead<T>(ref T? field) where T : class
    {
        try { return Volatile.Read(ref field); } catch { return null; }
    }

    public static AsyncTicker? TryGetTicker() => TryRead(ref _asyncTicker);
    public static AsyncThreadPool? TryGetPool() => TryRead(ref _asyncThreadPool);
    public static GameTime? TryGetGameTime() => TryRead(ref _gameTime);
    public static MapHandler? TryGetMapHandler() => TryRead(ref _mapHandler);
    public static NodeHandler? TryGetNodeHandler() => TryRead(ref _nodeHandler);
    public static ConnectionManager? TryGetConnectionManager() => TryRead(ref _connectionManager);

    // Typed singleton override (F001: replaces GlobalServices._nodeHandler/_mapHandler
    // reflection writes in MazeCommand). Same-lock assignment, no behavior change.
    public static void SetNodeHandler(NodeHandler nh)
    {
        _singletonLock.EnterWriteLock();
        try { _nodeHandler = nh; }
        finally { _singletonLock.ExitWriteLock(); }
        // Publish the twin slot too: NodeHandler.GetCurrent reads a separate
        // static, so setting only this slot would fork the two singletons.
        NodeHandler.SetCurrent(nh);
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
