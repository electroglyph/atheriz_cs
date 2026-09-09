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
    /// Hooks run through a compiled, statically-typed invoker (no DynamicInvoke):
    /// before/replace arity mismatches are skipped with a warning and after
    /// hooks fall back from args+result to args-only, so call sites keep their
    /// fallback behavior; genuine hook errors propagate unwrapped.
    /// </summary>
    public T Hookable<T>(string funcName, Func<T> original, params object?[] args)
    {
        HashSet<Delegate>? hooksSnapshot = null;
        bool hasHooks = false;
        _lock.EnterReadLock();
        try
        {
            if (_hooks.TryGetValue(funcName, out var hs) && hs.Count > 0)
            {
                hooksSnapshot = new HashSet<Delegate>(hs);
                hasHooks = true;
            }
        }
        finally { _lock.ExitReadLock(); }
        if (!hasHooks) return original();

        // marker classification cached per delegate (HookMarkerCache),
        // not reflected per dispatch.
        var replaceHooks = hooksSnapshot!.Where(d => (HookMarkerCache.KindOf(d) & HookKind.Replace) != 0).ToList();
        if (replaceHooks.Count > 0)
        {
            try
            {
                return (T)DelegateInvoker.Invoke(replaceHooks[0], args)!;
            }
            catch (TargetParameterCountException)
            {
                // Arity mismatch: ignore the bad replace hook, run original path.
            }
        }

        var beforeHooks = hooksSnapshot!.Where(d => (HookMarkerCache.KindOf(d) & HookKind.Before) != 0).ToList();
        foreach (var h in beforeHooks)
        {
            // Advisory: return ignored. Hook errors propagate raw (previously
            // TIE-unwrapped — same observable, no reflection wrapper).
            // Arity mismatches get the replace-hook treatment (skip + loud
            // log): a mis-signed before hook must not throw out of the entry
            // point (e.g. AtPreMove failing MoveTo with an exception instead
            // of a false return).
            try { DelegateInvoker.Invoke(h, args); }
            catch (TargetParameterCountException ex) { AtherizLogger.LogWarning($"Skipped GameObject.Hookable before-hook with mismatched signature: {ex.Message}", "GameObject"); }
        }

        var result = original();

        var afterHooks = hooksSnapshot!.Where(d => (HookMarkerCache.KindOf(d) & HookKind.After) != 0).ToList();
        foreach (var h in afterHooks)
        {
            // after hooks: try args+result then args only (faithful to Python where after hook receives same args, not extra result)
            object? newResult = null;
            bool invoked = false;
            try
            {
                newResult = DelegateInvoker.Invoke(h, args.Append((object?)result).ToArray());
                invoked = true;
            }
            catch (TargetParameterCountException) { }
            // A throwing after-hook propagates instead of nulling the
            // result — and the args-only fallback below is arity-only for the
            // same reason: genuine hook errors surface, only a second arity
            // mismatch falls through to the original result.
            if (!invoked)
            {
                try { newResult = DelegateInvoker.Invoke(h, args); invoked = true; }
                catch (TargetParameterCountException) { }
            }
            // Port of base_obj.py:64-66 — an after-hook replaces the result
            // unconditionally, including with null (reference types).
            if (invoked && (newResult is T t || (newResult is null && default(T) is null))) result = (T)newResult!;
        }
        // Hooks present but none marked before/after/replace: silently run original
        // (adaptation — Python raised ValueError; aborting here would break game code).
        return result;
    }
}
