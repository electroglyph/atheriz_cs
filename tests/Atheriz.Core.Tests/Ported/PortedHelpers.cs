using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

/// <summary>Shared helpers for Ported tests — deduplicates ~750 duplicate helper definitions.
/// Each defines MakeCaller/MakeManager/ClearChannelCache/Wait.
/// New tests should use this class instead of defining local helpers.
/// Remaining files that still define local helpers should migrate.
/// </summary>
public static class PortedHelpers
{
    private static GameObject CreateCaller(string name, Privilege priv, Node? nodeLocation, int id = -1)
    {
        var c = GameObject.Create(name);
        if (id >= 0) c.Id = id;
        c.PrivilegeLevel = priv;
        c.Quelled = false;
        if (nodeLocation != null)
            c.Location = new LocationRef.CoordLocation(nodeLocation.Coord);
        if (ObjectRegistry.Get(c.Id).Count == 0)
        {
            try { ObjectRegistry.AddObject(c); } catch { }
        }
        else
        {
            // Ensure object is registered even if id collision — replace for isolation
            try
            {
                var existing = ObjectRegistry.Get(c.Id).First();
                if (!ReferenceEquals(existing, c))
                {
                    try { ObjectRegistry.RemoveObject(existing); } catch { }
                    ObjectRegistry.AddObject(c);
                }
            }
            catch { }
        }
        c.ClearMessages();
        return c;
    }

    // Most common: string name + Privilege
    public static GameObject MakeCaller(string name = "Caller", Privilege priv = Privilege.Player)
        => CreateCaller(name, priv, null);

    public static GameObject MakeCaller(Privilege priv)
        => CreateCaller("Caller", priv, null);

    public static GameObject MakeCaller(string name, Privilege priv, Node? location)
        => CreateCaller(name, priv, location);

    // Bool builder/superuser variants (legacy Ported files use bool flags)
    public static GameObject MakeCaller(string name, bool builder, bool superuser = false, Node? location = null)
    {
        var p = superuser ? Privilege.Admin : (builder ? Privilege.Builder : Privilege.Player);
        return CreateCaller(name, p, location);
    }

    public static GameObject MakeCaller(string name, bool builder, bool superuser, GameObject? loc)
    {
        Node? node = null;
        if (loc != null) node = loc.ResolveLocationObject() as Node;
        return MakeCaller(name, builder, superuser, node);
    }

    // Node? location + bool isBuilder (Door tests)
    public static GameObject MakeCaller(Node? location, bool isBuilder = true)
        => CreateCaller("Caller", isBuilder ? Privilege.Builder : Privilege.Player, location);

    public static GameObject MakeCaller(string name, Node? location, bool isBuilder)
        => CreateCaller(name, isBuilder ? Privilege.Builder : Privilege.Player, location);

    public static GameObject MakeCallerWithLocation(Node? location, bool isBuilder = true)
        => MakeCaller(location, isBuilder);

    public static GameObject MakeCallerWithCoord(Coord coord, bool isBuilder = true)
    {
        var node = new Node(coord);
        return MakeCaller(node, isBuilder);
    }

    // Support id variant used by ChannelTests: MakeCaller(string name, int id)
    public static GameObject MakeCallerWithId(string name = "TestPlayer", int id = 1)
        => CreateCaller(name, Privilege.Player, null, id);

    public static ConnectionManager MakeManager(AtherizSettings? s = null, AsyncThreadPool? pool = null)
    {
        var settings = s ?? new AtherizSettings();
        var p = pool ?? new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        return new ConnectionManager(pool: p, settings: settings);
    }

    public static void ClearChannelCache() => ChannelCommand.ClearCache();

    public static async Task<bool> WaitAsync(Func<bool> cond, int timeoutMs = 1000, int pollMs = 10)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            await Task.Delay(pollMs);
        }
        return cond();
    }

    public static bool WaitFor(Func<bool> cond, int timeoutMs = 2000, int pollMs = 10)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(pollMs);
        }
        return cond();
    }

    // Alias for sync contexts per spec
    public static Task<bool> WaitForAsync(Func<bool> cond, int timeoutMs = 2000, int pollMs = 10)
        => WaitAsync(cond, timeoutMs, pollMs);
}