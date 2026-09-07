// Port of atheriz/reloader.py:14 _EXCLUDED_MODULES + 216 CLASS_INJECTIONS + 249 _apply_patch
// Port of atheriz/atheriz.py:103 setup_game_folder (injection scanning)
// Minimal faithful port using collectible AssemblyLoadContext — webclient sync off, no Windows ACL hardening.

using System.Reflection;
using System.Runtime.Loader;

namespace Atheriz.Core.Plugins;

/// <summary>
/// Mirrors Python <c>CLASS_INJECTIONS</c> tuple <c>(local_module, class_name, target_import_path)</c> at <c>new.py:312</c>.
/// In C#, mark your replacement type with this attribute.
/// Example: <c>[EntityReplacement(typeof(GameObject), typeof(CustomObject))]</c>
/// which maps to <c>("object","Object","atheriz.objects.base_obj")</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EntityReplacementAttribute : Attribute
{
    public Type BaseType { get; }
    public Type ReplacementType { get; }

    public EntityReplacementAttribute(Type baseType, Type replacementType)
    {
        BaseType = baseType;
        ReplacementType = replacementType;
    }
}

/// <summary>
/// Port of <c>reloader.py</c> hot-reload scanning + <c>setup_game_folder</c> CLASS_INJECTIONS re-application.
/// Uses <c>AssemblyLoadContext(isCollectible:true)</c> to mirror importlib.reload + _apply_patch semantics.
/// </summary>
public sealed class PluginLoader : IDisposable
{
    // Single exclusion source: PluginReloader.IsExcludedAssembly (port of _EXCLUDED_MODULES).
    // Never reload core/server state; mirrors Python excluded set comments.

    private AssemblyLoadContext? _alc;
    private Assembly? _loaded;

    /// <summary>Registered replacements: base type → replacement type.</summary>
    public Dictionary<Type, Type> Replacements { get; } = new();

    public bool IsLoaded => _loaded != null;

    /// <summary>
    /// Port of <c>reloader._discover_new_game_modules + _reload_game_folder_modules</c> + <c>setup_game_folder</c> injection loop.
    /// Creates collectible ALC, loads assembly, scans for <see cref="EntityReplacementAttribute"/>, registers.
    /// Logs via Console.Error mirroring <c>logger.info("[HotReload] ...")</c>.
    /// FULL-TRUST LOADER BY DESIGN (mirrors Python importlib): loading executes
    /// static constructors (on <c>LoadFromAssemblyPath</c> + <c>GetTypes</c> scan).
    /// There is deliberately NO allowlist/signature verification — load only
    /// assemblies from trusted paths (game <c>bin/</c>, <c>&lt;game&gt;/plugins</c>).
    /// A dropped-in dll auto-loads on next reload, same trust as a dropped-in .py.
    /// </summary>
    public void Load(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("assemblyPath must not be empty", nameof(assemblyPath));

        var full = Path.GetFullPath(assemblyPath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Plugin assembly not found at {full}", full);

        // Check excluded prefixes — mirrors _EXCLUDED_MODULES guard (skip entirely, like Python's continue).
        var name = Path.GetFileNameWithoutExtension(full);
        if (PluginReloader.IsExcludedAssembly(full))
        {
            Console.Error.WriteLine($"[PluginLoader] Skipping excluded assembly: {name}");
            return;
        }

        // Unload any previous ALC first — overwriting _alc without unloading leaks it.
        if (_loaded != null) Unload();

        // Create collectible ALC — mirrors importlib.reload isolation + Python's two-pass reload.
        // The Resolving handler serves plugin-local deps from beside the plugin dll.
        // Framework/core assemblies are deliberately NOT probed here: they must unify
        // with the default context (a second Atheriz.Core copy would cause Type-identity
        // mismatch, making Replacements silently miss live objects).
        _alc = new AssemblyLoadContext($"game-{Guid.NewGuid():N}", isCollectible: true);
        var pluginDir = Path.GetDirectoryName(full) ?? "";
        _alc.Resolving += (ctx, name) =>
        {
            if (PluginReloader.IsExcludedAssembly(name.Name + ".dll")) return null;
            var probe = Path.Combine(pluginDir, name.Name + ".dll");
            if (!File.Exists(probe)) return null;
            try { return ctx.LoadFromAssemblyPath(probe); }
            catch (Exception ex) { Console.Error.WriteLine($"[PluginLoader] Dep resolve {name.Name}: {ex.Message}"); return null; }
        };
        // Snapshot mtime before load; re-checked after (discovery→load TOCTOU guard).
        var tsBefore = File.GetLastWriteTimeUtc(full);
        try
        {
            // LoadFromAssemblyPath uses ALC's default load; we resolve dependencies via ALC.Resolving
            _loaded = _alc.LoadFromAssemblyPath(full);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PluginLoader] Failed to load {full}: {ex.Message}");
            // Unload the stillborn ALC — overwriting _alc without unloading leaks it.
            try { _alc.Unload(); } catch { }
            _alc = null;
            throw;
        }
        if (File.GetLastWriteTimeUtc(full) != tsBefore)
        {
            Console.Error.WriteLine($"[PluginLoader] {full} changed during load; unloading (TOCTOU).");
            Unload();
            throw new IOException($"Plugin assembly {full} changed during load; refusing to patch from a torn file.");
        }

        int found = 0;
        try
        {
            foreach (var type in _loaded.GetTypes())
            {
                // Scan class-level attributes
                var attrs = type.GetCustomAttributes<EntityReplacementAttribute>(inherit: false).ToList();
                // Also assembly-level attributes that reference this type as replacement
                // (handled below)
                foreach (var attr in attrs)
                {
                    if (!IsValidReplacement(attr.BaseType, attr.ReplacementType))
                    {
                        Console.Error.WriteLine($"[PluginLoader] Skipping {attr.ReplacementType.FullName} → {attr.BaseType.FullName} (from {type.Name}): replacement is not assignable to base and would never match live objects (e.g. plugin vendored its own Atheriz.Core copy).");
                        continue;
                    }
                    Replacements[attr.BaseType] = attr.ReplacementType;
                    found++;
                    Console.Error.WriteLine($"[PluginLoader] Injected {attr.ReplacementType.Name} → {attr.BaseType.Name} (from {type.Name})");
                }
            }
            // Assembly-level attributes
            foreach (var a in _loaded.GetCustomAttributes<EntityReplacementAttribute>())
            {
                if (!IsValidReplacement(a.BaseType, a.ReplacementType))
                {
                    Console.Error.WriteLine($"[PluginLoader] Skipping {a.ReplacementType.FullName} → {a.BaseType.FullName} (assembly): replacement is not assignable to base and would never match live objects.");
                    continue;
                }
                // Avoid double-count if already registered via class scan
                if (!Replacements.ContainsKey(a.BaseType))
                {
                    Replacements[a.BaseType] = a.ReplacementType;
                    found++;
                    Console.Error.WriteLine($"[PluginLoader] Injected {a.ReplacementType.Name} → {a.BaseType.Name} (assembly)");
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.Error.WriteLine($"[PluginLoader] Type load errors: {string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message))}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PluginLoader] Scan failed: {ex.Message}");
        }

        if (found == 0)
            Console.Error.WriteLine($"[PluginLoader] Loaded {Path.GetFileName(full)} — no [EntityReplacement] found (mirrors 'No CLASS_INJECTIONS').");
        else
            Console.Error.WriteLine($"[PluginLoader] Loaded {found} replacement(s) from {Path.GetFileName(full)}.");
    }

    /// <summary>
    /// Replacement contract: the replacement must be assignable to the base, else
    /// <c>PatchLiveObjects</c> could never match live objects (silent no-op patch).
    /// A vendored second <c>Atheriz.Core</c> copy fails this check — its types never
    /// unify with the live ones — so the mismatch is skipped LOUDLY, not silently.
    /// </summary>
    internal static bool IsValidReplacement(Type? baseType, Type? replacementType)
    {
        if (baseType == null || replacementType == null) return false;
        if (baseType == replacementType) return true;
        try { return baseType.IsAssignableFrom(replacementType); }
        catch { return false; }
    }

    /// <summary>
    /// Unload collectible ALC — mirrors Python reload dropping old modules.
    /// Call <c>GC.Collect()</c> after to finalize.
    /// </summary>
    public void Unload()
    {
        Replacements.Clear();
        _loaded = null;
        if (_alc != null)
        {
            try { _alc.Unload(); } catch (Exception ex) { Console.Error.WriteLine($"[PluginLoader] Unload failed: {ex.Message}"); }
            _alc = null;
        }
        Console.Error.WriteLine("[PluginLoader] Unloaded.");
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
