using System.Collections.Concurrent;
using System.Reflection;

namespace Atheriz.Core.Objects;

// hook marker classification is reflected ONCE per method/type and
// cached. Previously InstallHooks/RemoveHooks ran GetMethods + 3x
// GetCustomAttribute per method on every attach/detach, and Hookable ran
// GetCustomAttributes 3x per dispatch. Same APIs, amortized to ~zero.
[Flags]
internal enum HookKind { None = 0, Before = 1, After = 2, Replace = 4 }

internal static class HookMarkerCache
{
    private static readonly ConcurrentDictionary<MethodInfo, HookKind> _byMethod = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(string Name, MethodInfo Method, HookKind Kind)>> _byType = new();

    public static HookKind KindOf(Delegate d) => _byMethod.GetOrAdd(d.Method, Classify);

    public static IReadOnlyList<(string Name, MethodInfo Method, HookKind Kind)> ForType(Type t) =>
        _byType.GetOrAdd(t, static type =>
        {
            List<(string, MethodInfo, HookKind)> list = [];
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!m.Name.StartsWith("at_", StringComparison.Ordinal)) continue;
                var kind = Classify(m);
                if (kind != HookKind.None) list.Add((m.Name, m, kind));
            }
            return list;
        });

    private static HookKind Classify(MethodInfo m)
    {
        var k = HookKind.None;
        if (m.GetCustomAttributes(typeof(ReplaceAttribute), false).Length > 0) k |= HookKind.Replace;
        if (m.GetCustomAttributes(typeof(BeforeAttribute), false).Length > 0) k |= HookKind.Before;
        if (m.GetCustomAttributes(typeof(AfterAttribute), false).Length > 0) k |= HookKind.After;
        return k;
    }
}
