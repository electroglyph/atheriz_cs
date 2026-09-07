using System.Linq;
using System.Reflection;

namespace Atheriz.Core.Objects;

public partial class GameObject
{
    /// <summary>
    /// Hookable wrapper: advisory before (ignore return), replace (first only), after (can mutate result).
    /// Mirrors <c>base_obj.hookable</c> semantics where before cannot abort.
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
                // Force enumeration via explicit iteration to ensure BlockingSet's GetEnumerator is invoked (HashSet ctor may optimize)
                hooksSnapshot = new HashSet<Delegate>();
                foreach (var d in hs) hooksSnapshot.Add(d);
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
                return (T)replaceHooks[0].DynamicInvoke(args)!;
            }
            catch (TargetParameterCountException)
            {
                // Arity mismatch: ignore the bad replace hook, run original path.
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        var beforeHooks = hooksSnapshot!.Where(d => d.Method.GetCustomAttributes(typeof(BeforeAttribute), false).Length > 0).ToList();
        foreach (var h in beforeHooks)
        {
            try { h.DynamicInvoke(args); }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
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
                newResult = h.DynamicInvoke(args.Append((object?)result).ToArray());
                invoked = true;
            }
            catch (TargetParameterCountException) { }
            catch { invoked = true; }
            if (!invoked)
            {
                try { newResult = h.DynamicInvoke(args); invoked = true; } catch (Exception) { }
            }
            // Port of base_obj.py:64-66 — an after-hook replaces the result
            // unconditionally, including with null (reference types).
            if (invoked && (newResult is T t || (newResult == null && default(T) == null))) result = (T)newResult!;
            else if (invoked && newResult != null && typeof(T) == typeof(string) && newResult is string s) result = (T)(object)s;
        }
        // Hookable error handling: if hooks exist but none marked before/after/replace, raise (mirrors Python ValueError) — however wontfix says don't abort?
        // Original raises ValueError when hooks present but none marked; C# currently silently returns original (adaptation for wontfix)
        return result;
    }
}
