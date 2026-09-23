// Centralized lazy singleton getters with re-entrant double-checked locking.
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;

namespace Atheriz.Core.Globals;

public static class GlobalServices
{
    private static readonly ReaderWriterLockSlim _singletonLock = new(LockRecursionPolicy.SupportsRecursion);

    private static AsyncThreadPool? _asyncThreadPool;
    private static AsyncTicker? _asyncTicker;
    private static NodeHandler? _nodeHandler;
    private static MapHandler? _mapHandler;
    private static GameTime? _gameTime;
    private static GameObject? _serverChannel;
    private static CmdSet? _loggedInCmdSet;
    private static CmdSet? _unloggedInCmdSet;
    private static ConnectionManager? _connectionManager;

    public static int GetId() => IdGenerator.GetId();
    public static void SetId(int id) => IdGenerator.SetId(id);
    public static int GetUniqueId() => IdGenerator.GetUniqueId();

// double-checked locking with RWL (re-entrant)
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

    public static AsyncTicker GetAsyncTicker() => GetOrCreateSingleton(ref _asyncTicker, () =>
    {
        var pool = GetAsyncThreadPool();
        return new AsyncTicker(pool);
    });

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
    public static MapHandler GetMapHandler() => GetOrCreateSingleton(ref _mapHandler, () =>
    {
        var settings = AtherizSettings.Global;
        return CreateMapHandler(settings);
    });
    public static MapHandler GetMapHandler(AtherizSettings settings) => GetOrCreateSingleton(ref _mapHandler, () => CreateMapHandler(settings));

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
                var c = ObjectRegistry.FilterBy(o =>
                    o.IsChannel && (o.Name is not null && o.Name.ToLowerInvariant() == "server") && !o.IsDeleted);
                if (c.Count > 0)
                {
                    _serverChannel = c[0];
                }
                else
                {
                    AtherizLogger.LogWarning("Server channel not found.");
                    _serverChannel = null;
                }
                return _serverChannel;
            }
            finally { _singletonLock.ExitWriteLock(); }
        }
        finally { _singletonLock.ExitUpgradeableReadLock(); }
    }

    public static CmdSet GetLoggedInCmdSet() => GetOrCreateSingleton(ref _loggedInCmdSet, () => CommandRegistry.LoggedIn);

    public static CmdSet GetUnloggedInCmdSet() => GetOrCreateSingleton(ref _unloggedInCmdSet, () => CommandRegistry.UnloggedIn);

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
        // Publish the twin slot too: MapHandlerSingleton.Get caches the first
        // GlobalServices lookup forever, so setting only this slot forks the
        // two singletons (door paint/cleanup and move stamps would read the
        // stale world). Mirrors SetNodeHandler above.
        MapHandlerSingleton.Set(mh);
    }

    // Expose lock for StartStop faithful clearing (mirrors get_singleton._SINGLETON_LOCK)
    public static ReaderWriterLockSlim SingletonLock => _singletonLock;
}
