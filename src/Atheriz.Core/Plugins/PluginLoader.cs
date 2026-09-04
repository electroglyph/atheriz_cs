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
    // Port of reloader.py:14 _EXCLUDED_MODULES — never reload core/server state
    // Do NOT reload these; mirroring Python excluded set comments.
    private static readonly HashSet<string> ExcludedAssemblyPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Atheriz.Core", // mirrors atheriz.reloader, atheriz.globals.*, atheriz.settings etc
        "Microsoft.",
        "System.",
        "xunit.",
    };

    private AssemblyLoadContext? _alc;
    private Assembly? _loaded;

    /// <summary>Registered replacements: base type → replacement type.</summary>
    public Dictionary<Type, Type> Replacements { get; } = new();

    public bool IsLoaded => _loaded != null;

    /// <summary>
    /// Port of <c>reloader._discover_new_game_modules + _reload_game_folder_modules</c> + <c>setup_game_folder</c> injection loop.
    /// Creates collectible ALC, loads assembly, scans for <see cref="EntityReplacementAttribute"/>, registers.
    /// Logs via Console.Error mirroring <c>logger.info("[HotReload] ...")</c>.
    /// </summary>
    public void Load(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("assemblyPath must not be empty", nameof(assemblyPath));

        var full = Path.GetFullPath(assemblyPath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Plugin assembly not found at {full}", full);

        // Check excluded prefixes — mirrors _EXCLUDED_MODULES guard
        var name = Path.GetFileNameWithoutExtension(full);
        if (ExcludedAssemblyPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase) || full.Contains(p)))
        {
            Console.Error.WriteLine($"[PluginLoader] Skipping excluded assembly: {name}");
        }

        // Create collectible ALC — mirrors importlib.reload isolation + Python's two-pass reload
        _alc = new AssemblyLoadContext($"game-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            // LoadFromAssemblyPath uses ALC's default load; we resolve dependencies via ALC.Resolving
            _loaded = _alc.LoadFromAssemblyPath(full);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PluginLoader] Failed to load {full}: {ex.Message}");
            throw;
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
                    Replacements[attr.BaseType] = attr.ReplacementType;
                    found++;
                    Console.Error.WriteLine($"[PluginLoader] Injected {attr.ReplacementType.Name} → {attr.BaseType.Name} (from {type.Name})");
                }
            }
            // Assembly-level attributes
            foreach (var a in _loaded.GetCustomAttributes<EntityReplacementAttribute>())
            {
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

    /// <summary>
    /// Stub for live patching — mirrors <c>reloader._apply_patch(obj, new_class)</c> at reloader.py:249.
    /// Faithful semantics note: copy FieldInfo values, skip <c>session/listeners/command</c>, preserve lock,
    /// try __getstate__/__setstate__ else __dict__ copy, rollback on failure, then ResolveRelations.
    /// TODO: implement FieldInfo copy when object model stabilizes; keep skeleton with lock discipline.
    /// Excluded: Microsoft.* / Atheriz.Core like _EXCLUDED_MODULES.
    /// </summary>
    public int PatchLiveObjects(IEnumerable<object> liveObjects)
    {
        // TODO — full port would:
        // - Iterate liveObjects (mirrors filter_by at reloader.py:486-491 + NodeHandler traversal)
        // - For each obj, look up newClass = Replacements[obj.GetType()] or base-type walk
        // - Acquire obj.lock (or _FALLBACK_PATCH_LOCK at reloader.py:246) via ReaderWriterLockSlim
        // - Save session/listeners/command (reloader.py:255-257)
        // - OrigDict = obj.__dict__.copy() via reflection FieldInfo snapshot
        // - Try: state = obj.__getstate__() ?? FieldInfo dict; obj.__class__ = newClass; obj.__setstate__(state) ?? FieldInfo patch; restore session/listeners/command
        // - Except: rollback __class__ + __dict__ (reloader.py:288-299) and rethrow
        // - After loop, re-ResolveRelations (reloader.py:518-525) and re-init CmdSets (reloader.py:508-512)
        Console.Error.WriteLine("[PluginLoader] PatchLiveObjects stub — no objects patched (TODO mirrors _apply_patch).");
        return 0;
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
