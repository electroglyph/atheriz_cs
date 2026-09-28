using System.Linq;
using System.Reflection;

namespace Atheriz.Core.Objects;

[AttributeUsage(AttributeTargets.Method)]
public sealed class BeforeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class AfterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class ReplaceAttribute : Attribute { }

public partial class GameObject
{
    /// <summary>
    /// Hookable wrapper: advisory before (ignore return), replace (first only), after (can mutate result).
    /// Mirrors <c>base_obj.hookable</c> semantics where before cannot abort.
    /// Subclass At* overrides must call base (or route through Hookable) to
    /// keep installed script hooks firing; an override that replaces the body
    /// drops them.
    /// Hooks run through a compiled, statically-typed invoker (no DynamicInvoke):
    /// before/replace arg-count mismatches are skipped with a warning and after
    /// hooks fall back from args+result to args-only, so call sites keep their
    /// fallback behavior; genuine hook errors propagate unwrapped.
    /// </summary>
    /// <summary>
    /// Typed dispatch: enum member instead of the hook-name string. The
    /// string overload stays for custom (game-defined) hooks.
    /// </summary>
    public T Hookable<T>(HookName hookName, Func<T> original, params object?[] args)
        => Hookable(hookName.Name(), original, args);

    public T Hookable<T>(string funcName, Func<T> original, params object?[] args)
    {
        List<Delegate>? hooksSnapshot;
        _lock.EnterReadLock();
        try
        {
            hooksSnapshot = _hookRegistry.TrySnapshot(funcName);
        }
        finally { _lock.ExitReadLock(); }
        if (hooksSnapshot is null) return original();

        // marker classification cached per delegate (HookMarkerCache),
        // not reflected per dispatch. Single partition pass: each delegate is
        // classified once and appended to every kind-list it belongs to, which
        // preserves each kind's snapshot order while dropping two iterations.
        // (Only the kind filters merge — the per-hook after-args allocation
        // below stays per hook because result is reassigned per hook.)
        var replaceHooks = new List<Delegate>();
        var beforeHooks = new List<Delegate>();
        var afterHooks = new List<Delegate>();
        foreach (var d in hooksSnapshot)
        {
            var kind = HookMarkerCache.KindOf(d);
            if ((kind & HookKind.Replace) != 0) replaceHooks.Add(d);
            if ((kind & HookKind.Before) != 0) beforeHooks.Add(d);
            if ((kind & HookKind.After) != 0) afterHooks.Add(d);
        }
        if (replaceHooks.Count > 0)
        {
            try
            {
                return (T)DelegateInvoker.Invoke(replaceHooks[0], args)!;
            }
            catch (TargetParameterCountException)
            {
                // Arg-count mismatch: ignore the bad replace hook, run original path.
            }
        }

        foreach (var h in beforeHooks)
        {
            // Advisory: return ignored. Hook errors propagate raw (previously
            // TIE-unwrapped — same observable, no reflection wrapper).
            // Arg-count mismatches get the replace-hook treatment (skip + loud
            // log): a mis-signed before hook must not throw out of the entry
            // point (e.g. AtPreMove failing MoveTo with an exception instead
            // of a false return).
            try { DelegateInvoker.Invoke(h, args); }
            catch (TargetParameterCountException ex) { AtherizLogger.LogWarning($"Skipped GameObject.Hookable before-hook with mismatched signature: {ex.Message}", "GameObject"); }
        }

        var result = original();

        foreach (var h in afterHooks)
        {
            // after hooks: args+result first, then args-only (faithful to
            // Python where after hook receives same args, not extra result).
            // The branch reads the cached arity — no exception probing: a
            // throwing after-hook propagates instead of nulling the result,
            // and only an arity miss on both shapes falls through to the
            // original result.
            object? newResult = null;
            bool invoked = false;
            if (DelegateInvoker.AcceptsArgCount(h, args.Length + 1))
            {
                newResult = DelegateInvoker.Invoke(h, args.Append((object?)result).ToArray());
                invoked = true;
            }
            else if (DelegateInvoker.AcceptsArgCount(h, args.Length))
            {
                newResult = DelegateInvoker.Invoke(h, args);
                invoked = true;
            }
            // an after-hook replaces the result unconditionally, including with
            // null for reference types. The ! covers the generic null case
            // the compiler cannot see: newResult is null and default(T) is
            // null means the null assignment is intended.
            if (!invoked) continue;
            if (newResult is T t) { result = t; continue; }
            if (newResult is null && default(T) is null) { result = default!; continue; }
        }
        // Hooks present but none marked before/after/replace: silently run original
        // (adaptation — Python raised ValueError; aborting here would break game code).
        return result;
    }
}
