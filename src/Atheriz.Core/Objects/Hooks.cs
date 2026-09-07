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
    /// arity mismatches surface as <see cref="TargetParameterCountException"/> so call
    /// sites keep their fallback behavior, and hook errors propagate unwrapped.
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

        var replaceHooks = hooksSnapshot!.Where(d => d.Method.GetCustomAttributes(typeof(ReplaceAttribute), false).Length > 0).ToList();
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

        var beforeHooks = hooksSnapshot!.Where(d => d.Method.GetCustomAttributes(typeof(BeforeAttribute), false).Length > 0).ToList();
        foreach (var h in beforeHooks)
        {
            // Advisory: return ignored. Hook errors propagate raw (previously
            // TIE-unwrapped — same observable, no reflection wrapper).
            DelegateInvoker.Invoke(h, args);
        }

        var result = original();

        var afterHooks = hooksSnapshot!.Where(d => d.Method.GetCustomAttributes(typeof(AfterAttribute), false).Length > 0).ToList();
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
            catch { invoked = true; }
            if (!invoked)
            {
                try { newResult = DelegateInvoker.Invoke(h, args); invoked = true; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Hookable: " + logEx.Message, "GameObject"); }
            }
            // Port of base_obj.py:64-66 — an after-hook replaces the result
            // unconditionally, including with null (reference types).
            if (invoked && (newResult is T t || (newResult == null && default(T) == null))) result = (T)newResult!;
            else if (invoked && newResult != null && typeof(T) == typeof(string) && newResult is string s) result = (T)(object)s;
        }
        // Hooks present but none marked before/after/replace: silently run original
        // (adaptation — Python raised ValueError; aborting here would break game code).
        return result;
    }
}
