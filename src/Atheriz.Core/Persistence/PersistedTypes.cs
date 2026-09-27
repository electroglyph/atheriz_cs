using Atheriz.Core.Objects;

namespace Atheriz.Core.Persistence;

/// <summary>
/// One registration call for game-defined persisted types.
/// Facade over the two registries — <c>GameObject</c> factories
/// (<c>Func&lt;GameObject&gt;</c>) and <c>Node</c> factories
/// (<c>Func&lt;Coord, Node&gt;</c>) keep their own shapes and lifetimes; this
/// picks the right one by assignability. The key defaults to the type full
/// name; pass an explicit key for stability. <c>persist: false</c> opts out of
/// DTO persistence (code-only type): nothing is registered, but the loud
/// unregistered-subtype downgrade log is suppressed for the type.
/// </summary>
public static class PersistedTypes
{
    public static void Register<T>(string? key = null, bool persist = true) where T : GameObject, new()
        => Register<T>(() => new T(), key, persist);

    public static void Register<T>(Func<T> factory, string? key = null, bool persist = true) where T : GameObject
    {
        ArgumentNullException.ThrowIfNull(factory);
        string name = key ?? typeof(T).FullName ?? typeof(T).Name;
        if (!persist)
        {
            Converters.GameObjectDtoConverter.MarkTransient(typeof(T));
            return;
        }
        if (typeof(Node).IsAssignableFrom(typeof(T)))
        {
            Node.RegisterPersistedSubtype(name, typeof(T), c =>
            {
                T n = factory();
                ((Node)(object)n).Coord = c;
                return (Node)(object)n;
            });
            CheckCrossRegistryDuplicate(name, typeof(T), isNodeKey: true);
        }
        else
        {
            GameObject.RegisterPersistedSubtype(name, typeof(T), () => factory());
            CheckCrossRegistryDuplicate(name, typeof(T), isNodeKey: false);
        }
    }

    public static void RegisterNode<T>(Func<Coord, T> factory, string? key = null) where T : Node
    {
        ArgumentNullException.ThrowIfNull(factory);
        string name = key ?? typeof(T).FullName ?? typeof(T).Name;
        Node.RegisterPersistedSubtype(name, typeof(T), c => factory(c));
        CheckCrossRegistryDuplicate(name, typeof(T), isNodeKey: true);
    }

    private static void CheckCrossRegistryDuplicate(string key, Type type, bool isNodeKey)
    {
        // The two registries have separate locks and shapes; the same key in
        // both would load ambiguously (node path checks its own registry
        // first, object path only its own). Same key for a different type is
        // a game wiring bug and must show in the log.
        var other = isNodeKey
            ? Converters.GameObjectDtoConverter.SnapshotSubtypeNames()
            : Node.SnapshotSubtypeNames();
        if (other.TryGetValue(key, out var otherType) && !string.Equals(otherType, type.FullName ?? type.Name, StringComparison.Ordinal))
            AtherizLogger.LogError($"Persisted subtype key '{key}' registered in both object and node registries ({otherType} vs {type.FullName}); loads resolve per-path and diverge.");
    }

    /// <summary>
    /// Full registered set across both registries, for the boot log.
    /// </summary>
    public static Dictionary<string, string> RegisteredTypes()
    {
        var all = Converters.GameObjectDtoConverter.SnapshotSubtypeNames();
        foreach (var kv in Node.SnapshotSubtypeNames())
        {
            if (all.TryGetValue(kv.Key, out var existing) && !string.Equals(existing, kv.Value, StringComparison.Ordinal))
                AtherizLogger.LogError($"Persisted subtype key '{kv.Key}' present in both object and node registries ({existing} vs {kv.Value}); node entry replaced the object entry in the merged view.");
            all[kv.Key] = kv.Value;
        }
        return all;
    }

    /// <summary>Logs the full persisted-subtype set; call once game assemblies are loaded.</summary>
    public static void LogRegisteredTypes()
    {
        var all = RegisteredTypes();
        AtherizLogger.LogInformation($"Persisted subtypes ({all.Count}): {string.Join(", ", all.Keys.Order(StringComparer.Ordinal))}");
    }
}
