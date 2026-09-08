using System.Reflection;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/base_script.py:Script</c> (240 LOC).
/// Scripts attach to GameObjects and install hooks (before/after/replace) via reflection.
/// </summary>
public class Script : GameObject
{
    public new static bool _is_thread_safe = true;
    private GameObject? _child; // Port of base_script.py:76 child: Object | None

    public Script()
    {
        IsScript = true;
    }

    public GameObject? Child => _child;

    public override IEnumerable<(string name, object? value, bool isProperty)> GetExamMembers()
    {
        foreach (var m in base.GetExamMembers()) yield return m;
        object? Safe(Func<object?> f) { try { return f(); } catch { return "<error>"; } }
        yield return ("Child", Safe(() => (object?)Child), true);
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_script.py:170 at_install</c> — called when script is installed on object.
    /// </summary>
    public virtual void AtInstall() // Port of base_script.py:170 at_install
    {
        Hookable("at_install", () => 0);
    }

    /// <summary>
    /// Scans methods of this Script for [Before]/[After]/[Replace] on at_* and installs onto child.
    /// Mirrors <c>Script.install_hooks</c> base_script.py:180-209.
    /// </summary>
    public void InstallHooks(GameObject child)
    {
        // Port of base_script.py:191-193 with self.lock: if self.child is not None and self.child is not child: raise ValueError
        SyncRoot.EnterWriteLock();
        try
        {
            if (_child != null && !ReferenceEquals(_child, child))
                throw new InvalidOperationException($"Script {Id} already attached to {_child} cannot be attached to {child}");
            _child = child;
        }
        finally { SyncRoot.ExitWriteLock(); }
        // Port of base_script.py:194-203 at_funcs = [(d, getattr(self,d)) for d in dir(self) if d.startswith("at_") and (is_before or is_after or is_replace)]
        // marker classification cached per type (HookMarkerCache).
        var atFuncs = HookMarkerCache.ForType(GetType());

        // Port of base_script.py:204-208 with child.lock: for name, func in at_funcs: s = child.hooks.get(name,set()); s.add(func); child.hooks[name]=s
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
            if (del == null)
            {
                AtherizLogger.LogError($"Script {Id}: cannot bind hook {GetType().Name}.{method.Name} (signature not expressible as Action/Func); hook skipped.");
                continue;
            }
            child.InstallHook(name, del);
        }

        // Port of base_script.py:108-118 create handling of scripts set? For Script.attach, base_script install_hooks is called via GameObject.add_script which adds to scripts set.
        // Here we ensure child's Scripts set includes this script's Id (mirrors Python child.scripts.add(script.id))
        // Only mark modified if actually added (so resolve_relations after load does not dirty)
        child.AddScriptId(this.Id);

        AtInstall(); // Port of base_script.py:209 self.at_install()
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
            if (parameters.Length == 0)
            {
                delegateType = method.ReturnType == typeof(void)
                    ? typeof(Action)
                    : typeof(Func<>).MakeGenericType(method.ReturnType);
            }
            else if (method.ReturnType == typeof(void))
            {
                var paramTypes = parameters.Select(p => p.ParameterType).ToArray();
                delegateType = GetActionType(paramTypes);
                if (delegateType == null) return null;
            }
            else
            {
                var paramTypes = parameters.Select(p => p.ParameterType).ToArray();
                var all = paramTypes.Concat(new[] { method.ReturnType }).ToArray();
                delegateType = GetFuncType(all);
                if (delegateType == null) return null;
            }
            return method.CreateDelegate(delegateType!, this);
        }
        catch
        {
            return null;
        }
    }

    private static Type? GetActionType(Type[] paramTypes)
    {
        return paramTypes.Length switch
        {
            0 => typeof(Action),
            1 => typeof(Action<>).MakeGenericType(paramTypes),
            2 => typeof(Action<,>).MakeGenericType(paramTypes),
            3 => typeof(Action<,,>).MakeGenericType(paramTypes),
            4 => typeof(Action<,,,>).MakeGenericType(paramTypes),
            5 => typeof(Action<,,,,>).MakeGenericType(paramTypes),
            6 => typeof(Action<,,,,,>).MakeGenericType(paramTypes),
            7 => typeof(Action<,,,,,,>).MakeGenericType(paramTypes),
            8 => typeof(Action<,,,,,,,>).MakeGenericType(paramTypes),
            9 => typeof(Action<,,,,,,,,>).MakeGenericType(paramTypes),
            10 => typeof(Action<,,,,,,,,,>).MakeGenericType(paramTypes),
            11 => typeof(Action<,,,,,,,,,,>).MakeGenericType(paramTypes),
            12 => typeof(Action<,,,,,,,,,,,>).MakeGenericType(paramTypes),
            13 => typeof(Action<,,,,,,,,,,,,>).MakeGenericType(paramTypes),
            14 => typeof(Action<,,,,,,,,,,,,,>).MakeGenericType(paramTypes),
            15 => typeof(Action<,,,,,,,,,,,,,,>).MakeGenericType(paramTypes),
            16 => typeof(Action<,,,,,,,,,,,,,,,>).MakeGenericType(paramTypes),
            _ => null
        };
    }

    private static Type? GetFuncType(Type[] allTypes)
    {
        // last is return; n = number of params
        int n = allTypes.Length - 1;
        return n switch
        {
            0 => typeof(Func<>).MakeGenericType(allTypes),
            1 => typeof(Func<,>).MakeGenericType(allTypes),
            2 => typeof(Func<,,>).MakeGenericType(allTypes),
            3 => typeof(Func<,,,>).MakeGenericType(allTypes),
            4 => typeof(Func<,,,,>).MakeGenericType(allTypes),
            5 => typeof(Func<,,,,,>).MakeGenericType(allTypes),
            6 => typeof(Func<,,,,,,>).MakeGenericType(allTypes),
            7 => typeof(Func<,,,,,,,>).MakeGenericType(allTypes),
            8 => typeof(Func<,,,,,,,,>).MakeGenericType(allTypes),
            9 => typeof(Func<,,,,,,,,,>).MakeGenericType(allTypes),
            10 => typeof(Func<,,,,,,,,,,>).MakeGenericType(allTypes),
            11 => typeof(Func<,,,,,,,,,,,>).MakeGenericType(allTypes),
            12 => typeof(Func<,,,,,,,,,,,,>).MakeGenericType(allTypes),
            13 => typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            14 => typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            15 => typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            _ => null
        };
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_script.py:211 remove_hooks</c>
    /// </summary>
    public void RemoveHooks(GameObject? child = null)
    {
        // Port of base_script.py:219 child = self.child if child is None else child
        child ??= _child;
        if (child == null) return; // Port of base_script.py:220-222 if child is None: logger.error...
        // marker classification cached per type (HookMarkerCache).
        var atFuncs = HookMarkerCache.ForType(GetType());
        // Port of base_script.py:233-240 with child.lock: mutate the hook sets
        // under the child's write lock via the typed accessor (no reflection).
        {
            child.SyncRoot.EnterWriteLock();
            try
            {
                // Fetch inside the lock: the live dict must not be grabbed
                // beforehand (a concurrent InstallHook could replace it).
                var hooksDict = child.HooksRawNoLock;
        foreach (var (name, method, _) in atFuncs)
                    {
                        if (hooksDict.TryGetValue(name, out var set))
                        {
                            // Remove exact delegates matching this script's methods
                            var toRemove = set.Where(d => d.Method == method && ReferenceEquals(d.Target, this)).ToList();
                            foreach (var d in toRemove) set.Remove(d);
                            // Also clear any remaining hooks whose __self__ (Target) is this script — Port of base_script.py:237-239 s.difference_update([hook for hook in s if getattr(hook,"__self__",None) is self])
                            var extra = set.Where(d => ReferenceEquals(d.Target, this)).ToList();
                            foreach (var d in extra) set.Remove(d);
                            hooksDict[name] = set;
                        }
                    }
                }
                finally { child.SyncRoot.ExitWriteLock(); }
        }

        // Also remove from child's scripts set
        child.RemoveScriptId(this.Id);

        SyncRoot.EnterWriteLock();
        try
        {
            if (ReferenceEquals(_child, child)) _child = null; // Port of base_script.py:133 object.__setattr__(self, "child", None)
        }
        finally { SyncRoot.ExitWriteLock(); }
    }
}
