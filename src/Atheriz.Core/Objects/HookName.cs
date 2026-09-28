namespace Atheriz.Core.Objects;

// Typed hook dispatch: every engine hook has an enum member whose Name() is
// the exact runtime hook string (the HookNames constant). Dispatch and attach
// sites take HookName, so a typo is a compile error instead of a silently
// skipped hook; the string overloads stay for custom (game-defined) hooks.
// Attach-time arg-count check (InstallHook) uses DispatchArgCounts: the exact
// argument counts each hook's dispatch sites pass today.
//
// The enum plus the Name/TryParseName/DispatchArgCounts map are generated at
// compile time by Atheriz.HookGen from the HookNames constants (the single
// source of truth), so the three spellings cannot drift; only the
// attach-time check below is hand-written logic.
internal static partial class HookNameExtensions
{
    // Attach-time arg-count check mirroring DelegateInvoker: a delegate can take
    // a call of L args exactly when RequiredCount <= L <= Types.Length, using
    // the single required-parameter rule (DelegateInvoker.RequiredParameterCount).
    // Before/replace hooks run with the dispatch args; after hooks also run
    // with args+result, and unmarked delegates can land in either list, so
    // those accept one extra parameter.
    internal static bool AcceptsHookArgs(this HookName name, Delegate hook, HookKind kind)
    {
        var ps = hook.Method.GetParameters();
        int required = DelegateInvoker.RequiredParameterCount(ps);
        int total = ps.Length;
        bool afterStyle = kind == HookKind.None || (kind & HookKind.After) != 0;
        foreach (var n in name.DispatchArgCounts())
        {
            if (required <= n && n <= total) return true;
            if (afterStyle && required <= n + 1 && n + 1 <= total) return true;
        }
        return false;
    }
}
