using System.Linq.Expressions;
using System.Reflection;

namespace Atheriz.Core.Objects;

/// <summary>
/// Scripts attach to GameObjects and install hooks (before/after/replace) via reflection.
/// </summary>
public class Script : GameObject
{
    internal new static bool _is_thread_safe = true;
    private GameObject? _child;

    public Script()
    {
        IsScript = true;
    }
    // Load-path construction: skips the id draw (caller adopts the stored id
    // via SetIdRaw before publication). See GameObject.SkipIdDraw.
    internal Script(SkipIdDraw skip) : base(skip)
    {
        IsScript = true;
    }

    public GameObject? Child
    {
        // Locked read: every write takes SyncRoot, so a bare field read can
        // observe a half-published re-attach and detach hooks from the wrong
        // (previous) child.
        get { using (ReadScope()) return _child; }
    }

    public override IEnumerable<(string name, object? value, bool isProperty)> GetExamMembers()
    {
        foreach (var m in base.GetExamMembers()) yield return m;
        object? Safe(Func<object?> f) { try { return f(); } catch { return "<error>"; } }
        yield return ("Child", Safe(() => (object?)Child), true);
    }

    /// <summary>
/// called when script is installed on object.
    /// </summary>
    public virtual void AtInstall()
    {
        Hookable(HookNames.AtInstall, () => 0);
    }

    /// <summary>
    /// Scans methods of this Script for [Before]/[After]/[Replace] on at_* and installs onto child.
    /// Mirrors <c>Script.install_hooks</c> base_script.py:180-209.
    /// </summary>
    public void InstallHooks(GameObject child)
    {
        using (WriteScope())
        {
            if (_child is not null && !ReferenceEquals(_child, child))
                throw new InvalidOperationException($"Script {Id} already attached to {_child} cannot be attached to {child}");
            _child = child;
        }
        // marker classification cached per type (HookMarkerCache).
        var atFuncs = HookMarkerCache.ForType(GetType());

                    foreach (var (name, method, _) in atFuncs)
        {
            // Bind a closed delegate to this script instance. The delegate's
            // Method is the original method, so Hookable sees the
            // [Before]/[After]/[Replace] marker. A signature that cannot be
            // expressed as Action/Func (>16 params, byref-like types, open
            // generics) cannot be honored: log loudly and skip — never install
            // a marker-less wrapper that Hookable would silently ignore.
            Delegate? del;
            try
            {
                del = CreateHookDelegate(method);
            }
            catch (Exception ex)
            {
                AtherizLogger.LogError($"Script {Id}: cannot bind hook {GetType().Name}.{method.Name}: {ex.Message}; hook skipped.");
                continue;
            }
            if (del is null)
            {
                AtherizLogger.LogError($"Script {Id}: cannot bind hook {GetType().Name}.{method.Name} (signature not expressible as Action/Func); hook skipped.");
                continue;
            }
            child.InstallHook(name, del);
        }

        // Here we ensure child's Scripts set includes this script's Id (mirrors Python child.scripts.add(script.id))
        // Only mark modified if actually added (so resolve_relations after load does not dirty)
        child.AddScriptId(this.Id);

        AtInstall();
    }

    private Delegate? CreateHookDelegate(MethodInfo method)
    {
        // Create a closed delegate bound to this instance. delegate.Method is
        // the original method, preserving the marker attribute for Hookable.
        // Returns null when the signature cannot be expressed as Action/Func;
        // the caller logs loudly (a marker-less wrapper would be silently ignored).
        try
        {
            var parameters = method.GetParameters();
            Type? delegateType;
            var paramTypes = parameters.Select(p => p.ParameterType).ToArray();
            if (parameters.Length == 0)
            {
                delegateType = method.ReturnType == typeof(void)
                    ? typeof(Action)
                    : typeof(Func<>).MakeGenericType(method.ReturnType);
            }
            else if (method.ReturnType == typeof(void))
            {
                delegateType = GetActionType(paramTypes);
                if (delegateType is null) return null;
            }
            else
            {
                var all = paramTypes.Append(method.ReturnType).ToArray();
                delegateType = GetFuncType(all);
                if (delegateType is null) return null;
            }
            return method.CreateDelegate(delegateType!, this);
        }
        catch
        {
            return null;
        }
    }

    // Arity → Action<...>/Func<...> mapping via the BCL factories, which
    // produce the identical runtime types for arities 0-16. Past 16 params
    // the BCL throws while the old hand-rolled switch returned null — both
    // spellings funnel into the same outcome because CreateHookDelegate above
    // converts any failure into a loud-log-and-skip (null), so hook call
    // sites (max ~9 args at AtSayFull) never observe the difference.
    private static Type? GetActionType(Type[] paramTypes)
    {
        try { return Expression.GetActionType(paramTypes); }
        catch (ArgumentException) { return null; }
    }

    private static Type? GetFuncType(Type[] allTypes)
    {
        // last is return; the BCL counts params implicitly from the array.
        try { return Expression.GetFuncType(allTypes); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// </summary>
    public void RemoveHooks(GameObject? child = null)
    {
        if (child is null)
        {
            using (ReadScope()) child = _child;
        }
        if (child is null) return;
        // marker classification cached per type (HookMarkerCache).
        var atFuncs = HookMarkerCache.ForType(GetType());
        // under the child's write lock via the typed accessor (no reflection).
        {
            using (child.WriteScope())
            {
                // Fetch inside the lock: the live dict must not be grabbed
                // beforehand (a concurrent InstallHook could replace it).
                var hooksDict = child.HooksRawNoLock;
        foreach (var (name, _, _) in atFuncs)
                    {
                        if (hooksDict.TryGetValue(name, out var set))
                        {
                            // The old two-loop shape (exact method+target, then
                            // any remaining target) unions to "target is this",
                            // so one pass removes the same delegates. Identity
                            // must be ReferenceEquals: GameObject == is
                            // Id-equality, and same-Id distinct instances exist
                            // after hot-reload rewire — == would detach a
                            // replacement instance's hooks along with ours.
                            set.RemoveWhere(d => ReferenceEquals(d.Target, this));
                        }
                    }
            }
        }

        // Also remove from child's scripts set
        child.RemoveScriptId(this.Id);

        using (WriteScope())
        {
            if (ReferenceEquals(_child, child)) _child = null;
        }
    }
}
