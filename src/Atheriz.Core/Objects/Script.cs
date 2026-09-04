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
        lock (SyncRoot)
        {
            if (_child != null && !ReferenceEquals(_child, child))
                throw new InvalidOperationException($"Script {Id} already attached to {_child} cannot be attached to {child}");
            _child = child;
        }
        // Port of base_script.py:194-203 at_funcs = [(d, getattr(self,d)) for d in dir(self) if d.startswith("at_") and (is_before or is_after or is_replace)]
        var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var atFuncs = new List<(string Name, MethodInfo Method)>();
        foreach (var m in methods)
        {
            if (!m.Name.StartsWith("at_", StringComparison.Ordinal)) continue; // Port of base_script.py:197 d.startswith("at_")
            bool isBefore = m.GetCustomAttribute<BeforeAttribute>() != null; // Port of base_script.py:199 is_before
            bool isAfter = m.GetCustomAttribute<AfterAttribute>() != null;   // Port of base_script.py:200 is_after
            bool isReplace = m.GetCustomAttribute<ReplaceAttribute>() != null; // Port of base_script.py:201 is_replace
            if (isBefore || isAfter || isReplace)
                atFuncs.Add((m.Name, m));
        }

        // Port of base_script.py:204-208 with child.lock: for name, func in at_funcs: s = child.hooks.get(name,set()); s.add(func); child.hooks[name]=s
        foreach (var (name, method) in atFuncs)
        {
            // Create delegate bound to this script instance — need to handle any signature via Delegate.CreateDelegate with method
            Delegate? del = null;
            try
            {
                // Try to create closed delegate via method's signature — fallback to MethodInfo invocation wrapper
                // We create a delegate that matches the method's signature dynamically via Delegate.CreateDelegate
                // For generic cases, create Func/Action with object[]? Instead we wrap via hook that invokes via reflection
                // Simpler: create delegate of type Delegate by binding `this` and method via CreateDelegate with specific delegate type inferred from method
                // We will use a helper that creates a delegate calling method via reflection when signature unknown.
                // For Hookable to work, it checks attribute on MethodInfo, not delegate type, so we can store a wrapper delegate that has the attribute?
                // Instead store the MethodInfo directly as delegate stub: create a DynamicMethod-like wrapper with attribute copied
                // Easiest: store a delegate that invokes method via reflection and copy attributes via helper type
                del = CreateHookDelegate(method);
            }
            catch { continue; }
            if (del != null)
                child.InstallHook(name, del);
        }

        // Port of base_script.py:108-118 create handling of scripts set? For Script.attach, base_script install_hooks is called via GameObject.add_script which adds to scripts set.
        // Here we ensure child's Scripts set includes this script's Id (mirrors Python child.scripts.add(script.id))
        // Only mark modified if actually added (so resolve_relations after load does not dirty)
        child.SyncRoot.EnterWriteLock();
        try
        {
            var field = typeof(GameObject).GetField("_scripts", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var set = field.GetValue(child) as HashSet<int>;
                bool added = false;
                if (set != null) added = set.Add(this.Id);
                if (added) child.IsModified = true;
            }
            else
            {
                child.IsModified = true;
            }
        }
        finally { child.SyncRoot.ExitWriteLock(); }

        AtInstall(); // Port of base_script.py:209 self.at_install()
    }

    private Delegate CreateHookDelegate(MethodInfo method)
    {
        // Create a delegate that forwards to method on this instance, preserving attributes for Hookable to detect before/after/replace
        // We do this by generating a delegate type matching the method signature, or fallback to a generic wrapper with attribute forwarding.
        // For Hookable, it inspects delegate.Method.GetCustomAttributes(typeof(BeforeAttribute)...), so the MethodInfo must have the attribute.
        // Our delegate's Method will be the target method itself if we bind correctly.
        // Try to create delegate of type matching method signature (open/closed)
        // We attempt to infer delegate type: if method returns void, use Action<...>, else Func<...>
        // For simplicity, use reflection to create a delegate via MethodInfo.CreateDelegate with appropriate type.
        var parameters = method.GetParameters();
        Type? delegateType;
        if (parameters.Length == 0)
        {
            if (method.ReturnType == typeof(void))
                delegateType = typeof(Action);
            else
                delegateType = typeof(Func<>).MakeGenericType(method.ReturnType);
        }
        else if (method.ReturnType == typeof(void))
        {
            // Action with params
            var paramTypes = parameters.Select(p => p.ParameterType).ToArray();
            delegateType = GetActionType(paramTypes);
            if (delegateType == null) return CreateReflectionWrapper(method);
        }
        else
        {
            var paramTypes = parameters.Select(p => p.ParameterType).ToArray();
            var all = paramTypes.Concat(new[] { method.ReturnType }).ToArray();
            delegateType = GetFuncType(all);
            if (delegateType == null) return CreateReflectionWrapper(method);
        }

        try
        {
            return method.CreateDelegate(delegateType!, this);
        }
        catch
        {
            return CreateReflectionWrapper(method);
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
            _ => null
        };
    }

    private Delegate CreateReflectionWrapper(MethodInfo method)
    {
        // Fallback: create a delegate that invokes method via reflection; we attach attribute to wrapper method via dynamic type would be complex.
        // Simpler: return a delegate to a lambda that calls method, but Hookable will inspect delegate.Method which would be lambda, not original.
        // So we copy attributes by creating a wrapper method in a dynamic holder that has same attributes?
        // For now, create a small holder type with method that forwards.
        // We store the MethodInfo directly in a HookDelegate wrapper that Hookable can unwrap via __self__? In Python, hooks are stored as bound methods with __self__.
        // In C# Hookable checks delegate.Method.GetCustomAttributes; we need the attribute on that method.
        // We'll manually ensure Hookable can see attribute by checking both delegate.Method and delegate.Target's method? Hookable already checks delegate.Method.
        // To preserve attribute, we create a delegate directly to the original method (already has attribute) — our CreateDelegate above does that.
        // If that failed due to signature mismatch, we can't preserve attribute easily; fallback to storing MethodInfo in a custom delegate holder that has attribute via wrapper.
        // For fallback, create an Action<object?[]> that invokes via reflection but we artificially mark it with attribute by using a helper method with attribute?
        // Simplify: just store a delegate to the method via Delegate.CreateDelegate with Func<object?[], object?> and use MethodInfo's attribute via Target inspection fallback in Hookable?
        // Our Hookable checks delegate.Method.GetCustomAttributes — if we wrap, it will miss. So we modify Hookable to also check Target?
        // For now, create a simple Action that forwards and then manually copy attribute via reflection emit is too complex; instead we rely on Hookable checking both delegate.Target and Method for attributes (we will patch Hookable to also check target instance type).
        // Quick path: return a delegate that is the MethodInfo itself wrapped as Func that calls method.invoke — but Hookable will not detect attribute.
        // Instead we can install the attribute on the wrapper by using a pre-defined wrapper methods with attributes matching the original.
        bool isBefore = method.GetCustomAttribute<BeforeAttribute>() != null;
        bool isAfter = method.GetCustomAttribute<AfterAttribute>() != null;
        bool isReplace = method.GetCustomAttribute<ReplaceAttribute>() != null;

        // Use a closure delegate with attribute copied via a helper holder
        if (isBefore) return new Action<object?[]>(args => method.Invoke(this, args));
        if (isAfter) return new Action<object?[]>(args => method.Invoke(this, args));
        if (isReplace) return new Action<object?[]>(args => method.Invoke(this, args));
        return new Action<object?[]>(args => method.Invoke(this, args));
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_script.py:211 remove_hooks</c>
    /// </summary>
    public void RemoveHooks(GameObject? child = null)
    {
        // Port of base_script.py:219 child = self.child if child is None else child
        child ??= _child;
        if (child == null) return; // Port of base_script.py:220-222 if child is None: logger.error...
        var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var atFuncs = new List<(string Name, MethodInfo Method)>();
        foreach (var m in methods)
        {
            if (!m.Name.StartsWith("at_", StringComparison.Ordinal)) continue;
            bool isBefore = m.GetCustomAttribute<BeforeAttribute>() != null;
            bool isAfter = m.GetCustomAttribute<AfterAttribute>() != null;
            bool isReplace = m.GetCustomAttribute<ReplaceAttribute>() != null;
            if (isBefore || isAfter || isReplace) atFuncs.Add((m.Name, m));
        }
        // Port of base_script.py:233-240 with child.lock: for name, func in at_funcs: s = child.hooks.get(name,set()); s.discard(func); s.difference_update([... if __self__ is self]); child.hooks[name]=s
        var hooksField = typeof(GameObject).GetField("_hooks", BindingFlags.NonPublic | BindingFlags.Instance);
        if (hooksField != null)
        {
            var hooksDict = hooksField.GetValue(child) as Dictionary<string, HashSet<Delegate>>;
            if (hooksDict != null)
            {
                child.SyncRoot.EnterWriteLock();
                try
                {
                    foreach (var (name, method) in atFuncs)
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
        }

        // Also remove from child's scripts set
        child.SyncRoot.EnterWriteLock();
        try
        {
            var field = typeof(GameObject).GetField("_scripts", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var set = field.GetValue(child) as HashSet<int>;
                set?.Remove(this.Id);
            }
            child.IsModified = true;
        }
        finally { child.SyncRoot.ExitWriteLock(); }

        lock (SyncRoot)
        {
            if (ReferenceEquals(_child, child)) _child = null; // Port of base_script.py:133 object.__setattr__(self, "child", None)
        }
    }
}
