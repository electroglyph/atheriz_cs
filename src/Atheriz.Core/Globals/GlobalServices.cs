// Centralized lazy singletons via Lazy<T> (execution-and-publication).
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;

namespace Atheriz.Core.Globals;

public static class GlobalServices
{
    // Publication without a manual RWL: each slot is a Lazy<T> with
    // ExecutionAndPublication (exactly-once factory, lock-free hot path).
    // The reset gate is taken only to swap in fresh instances on
    // Reset/ClearForShutdown and to arbitrate the settings-pinned
    // first-creation race. Factories never run under the gate, so the old
    // GetTicker-while-holding-the-lock nesting is gone by construction.
    private static readonly Lock _resetGate = new();

    private static Lazy<AsyncThreadPool> _asyncThreadPool = FreshPool();
    private static Lazy<AsyncTicker> _asyncTicker = FreshTicker();
    private static Lazy<NodeHandler> _nodeHandler = FreshNodeHandler();
    private static Lazy<MapHandler> _mapHandler = FreshMapHandler();
    private static Lazy<GameTime> _gameTime = FreshGameTime();
    private static Lazy<CmdSet> _loggedInCmdSet = FreshLoggedInCmdSet();
    private static Lazy<CmdSet> _unloggedInCmdSet = FreshUnloggedInCmdSet();
    private static Lazy<ConnectionManager> _connectionManager = FreshConnectionManager();

    // Ambient makers: the canonical uncreated slot for each singleton. The
    // field initializers, Reset, the parameterless getters' fault-restore,
    // and the pinned overloads' fault-restore all share these, so a faulted
    // slot always falls back to the ambient factory — never to a foreign
    // settings-pinned one and never to a cached exception.
    private static Lazy<AsyncThreadPool> FreshPool() =>
        new(CreatePool, LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<AsyncTicker> FreshTicker() =>
        new(() => new AsyncTicker(GetAsyncThreadPool()), LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<NodeHandler> FreshNodeHandler() =>
        new(() => CreateNodeHandler(null), LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<MapHandler> FreshMapHandler() =>
        new(() => CreateMapHandler(AtherizSettings.Global), LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<GameTime> FreshGameTime() =>
        new(() => CreateGameTime(AtherizSettings.Global), LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<CmdSet> FreshLoggedInCmdSet() =>
        new(() => CommandRegistry.LoggedIn, LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<CmdSet> FreshUnloggedInCmdSet() =>
        new(() => CommandRegistry.UnloggedIn, LazyThreadSafetyMode.ExecutionAndPublication);
    private static Lazy<ConnectionManager> FreshConnectionManager() =>
        new(CreateConnectionManager, LazyThreadSafetyMode.ExecutionAndPublication);

    // The server channel needs live-validation (a deleted/renamed channel
    // must not be returned), so it stays a plain slot under the reset gate
    // instead of a Lazy.
    private static GameObject? _serverChannel;

    public static int GetId() => IdGenerator.GetId();
    public static void SetId(int id) => IdGenerator.SetId(id);
    public static int GetUniqueId() => IdGenerator.GetUniqueId();

    private static AsyncThreadPool CreatePool()
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
    }

    public static AsyncThreadPool GetAsyncThreadPool() => ReadSlot(ref _asyncThreadPool, FreshPool);

    public static AsyncTicker GetAsyncTicker() => ReadSlot(ref _asyncTicker, FreshTicker);

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
    public static NodeHandler GetNodeHandler() => ReadSlot(ref _nodeHandler, FreshNodeHandler);
    // Settings-pinned boot: first creation loads from settings.SavePath instead
    // of the ambient path, so DoStartup(settings) uses one database .
    // First creation wins: a concurrent parameterless creation keeps the
    // ambient slot, exactly like the old double-checked path.
    public static NodeHandler GetNodeHandler(AtherizSettings settings) =>
        GetOrCreatePinned(ref _nodeHandler, () => CreateNodeHandler(settings), FreshNodeHandler);

    private static MapHandler CreateMapHandler(AtherizSettings settings) => new(settings, autoLoad: true);
    public static MapHandler GetMapHandler() => ReadSlot(ref _mapHandler, FreshMapHandler);
    public static MapHandler GetMapHandler(AtherizSettings settings) =>
        GetOrCreatePinned(ref _mapHandler, () => CreateMapHandler(settings), FreshMapHandler);

    // Best-effort map handler for paint/move paths: created on demand, but a
    // creation failure yields null instead of throwing out of the paint path.
    public static MapHandler? GetMapHandlerOrDefault()
    {
        try { return TryGetMapHandler() ?? GetMapHandler(); }
        catch { return null; }
    }

    private static GameTime CreateGameTime(AtherizSettings settings)
    {
        // volatile reads — these slots are swapped by Reset/ClearForShutdown on other threads.
        var ticker = Volatile.Read(ref _asyncTicker);
        var pool = Volatile.Read(ref _asyncThreadPool);
        if (ticker.IsValueCreated || pool.IsValueCreated)
            return new GameTime(settings, ticker.Value, pool.Value, autoLoad: true);
        return new GameTime(settings, autoLoad: true);
    }
    public static GameTime GetGameTime() => ReadSlot(ref _gameTime, FreshGameTime);
    public static GameTime GetGameTime(AtherizSettings settings) =>
        GetOrCreatePinned(ref _gameTime, () => CreateGameTime(settings), FreshGameTime);

    // Fault-tolerant slot read. Lazy<T> caches factory exceptions, but the
    // pre-Lazy manual singletons stored nothing on failure so later callers
    // retried — DoStartup leans on that: it logs boot failures and carries
    // on (a node handler whose database is briefly unavailable must stay
    // retryable, not wedged for the life of the process). A faulted read
    // therefore swaps in a fresh Lazy (only if nobody beat us to it) and
    // rethrows the original failure.
    private static T ReadSlot<T>(ref Lazy<T> slot, Func<Lazy<T>> fresh)
    {
        var snap = Volatile.Read(ref slot);
        try { return snap.Value; }
        catch
        {
            Interlocked.CompareExchange(ref slot, fresh(), snap);
            throw;
        }
    }

    // First-creation-wins for the settings-pinned overloads. The gate is
    // only taken while the slot is still uncreated; the created hot path
    // stays a lock-free Lazy read. A failed pinned creation wins nothing:
    // the slot falls back to the ambient maker so the next caller retries
    // with its own factory instead of inheriting the cached exception or
    // re-running the failed pinned factory (same contract as ReadSlot).
    private static T GetOrCreatePinned<T>(ref Lazy<T> slot, Func<T> factory, Func<Lazy<T>> ambientFresh)
    {
        var snap = Volatile.Read(ref slot);
        if (snap.IsValueCreated) return snap.Value;
        lock (_resetGate)
        {
            if (!slot.IsValueCreated)
                slot = new Lazy<T>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
            try { return slot.Value; }
            catch
            {
                slot = ambientFresh();
                throw;
            }
        }
    }

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
        lock (_resetGate)
        {
            if (IsLiveServerChannel(_serverChannel)) return _serverChannel;
            _serverChannel = null;
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
    }

    public static CmdSet GetLoggedInCmdSet() => ReadSlot(ref _loggedInCmdSet, FreshLoggedInCmdSet);

    public static CmdSet GetUnloggedInCmdSet() => ReadSlot(ref _unloggedInCmdSet, FreshUnloggedInCmdSet);

    private static ConnectionManager CreateConnectionManager()
    {
        var cm = ConnectionManager.GlobalInstance ?? new ConnectionManager();
        ConnectionManager.GlobalInstance = cm;
        return cm;
    }
    public static ConnectionManager GetConnectionManager() => ReadSlot(ref _connectionManager, FreshConnectionManager);

    // Overload allowing caller-provided pool/settings (for Startup wiring)
    public static ConnectionManager GetConnectionManager(AtherizSettings settings, AsyncThreadPool pool) =>
        GetOrCreatePinned(ref _connectionManager, () =>
        {
            var cm = ConnectionManager.GlobalInstance ?? new ConnectionManager(pool, settings);
            ConnectionManager.GlobalInstance = cm;
            return cm;
        }, FreshConnectionManager);

    // Call with _resetGate held. Clears more than the three Python
    // names: world handlers must release so the next boot reloads instead of
    // resurrecting stale in-memory world, and command sets are rebuilt lazily
    // (reusing them across a world reload keeps references to discarded world).
    private static void ClearHoldersLocked()
    {
        _asyncThreadPool = FreshPool();
        _asyncTicker = FreshTicker();
        _nodeHandler = FreshNodeHandler();
        _mapHandler = FreshMapHandler();
        _gameTime = FreshGameTime();
        _serverChannel = null;
        _loggedInCmdSet = FreshLoggedInCmdSet();
        _unloggedInCmdSet = FreshUnloggedInCmdSet();
        _connectionManager = FreshConnectionManager();
    }

    internal static void ClearForShutdown()
    {
        lock (_resetGate)
        {
            ClearHoldersLocked();
        }
    }

    // For tests / reset — clears all holders
    public static void Reset()
    {
        lock (_resetGate)
        {
            ClearHoldersLocked();
        }
        // Also reset underlying registries that are not singletons but global
        try { CommandRegistry.Reset(); } catch (Exception) { }
        try { ConnectionManager.GlobalInstance = null; } catch (Exception) { }
    }

    private static T? TryRead<T>(ref Lazy<T> slot) where T : class
    {
        try
        {
            var snap = Volatile.Read(ref slot);
            return snap.IsValueCreated ? snap.Value : null;
        }
        catch { return null; }
    }

    public static AsyncTicker? TryGetTicker() => TryRead(ref _asyncTicker);
    public static AsyncThreadPool? TryGetPool() => TryRead(ref _asyncThreadPool);
    public static GameTime? TryGetGameTime() => TryRead(ref _gameTime);
    public static MapHandler? TryGetMapHandler() => TryRead(ref _mapHandler);
    public static NodeHandler? TryGetNodeHandler() => TryRead(ref _nodeHandler);
    public static ConnectionManager? TryGetConnectionManager() => TryRead(ref _connectionManager);

    // Swap core for the Set overrides below. The replacement is forced to
    // created before publishing: TryGet paths treat a Set value as live
    // (IsValueCreated), exactly like the old non-null field check.
    // Call with _resetGate held.
    private static void SwapCreated<T>(ref Lazy<T> slot, T value)
    {
        var fresh = new Lazy<T>(() => value, LazyThreadSafetyMode.ExecutionAndPublication);
        var _ = fresh.Value;
        Volatile.Write(ref slot, fresh);
    }

    // Typed singleton overrides. Same-gate swap, no behavior change.
    public static void SetAsyncTicker(AsyncTicker ticker)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        lock (_resetGate)
        {
            SwapCreated(ref _asyncTicker, ticker);
        }
    }
    public static void SetAsyncThreadPool(AsyncThreadPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        lock (_resetGate)
        {
            SwapCreated(ref _asyncThreadPool, pool);
        }
    }
    // Typed singleton override (replaces the old reflection writes in
    // MazeCommand). Same-gate swap, no behavior change.
    public static void SetNodeHandler(NodeHandler nh)
    {
        lock (_resetGate)
        {
            SwapCreated(ref _nodeHandler, nh);
        }
        // Publish the twin slot too: NodeHandler.GetCurrent reads a separate
        // static, so setting only this slot would fork the two singletons.
        NodeHandler.SetCurrent(nh);
    }
    public static void SetMapHandler(MapHandler mh)
    {
        lock (_resetGate)
        {
            SwapCreated(ref _mapHandler, mh);
        }
    }
}
