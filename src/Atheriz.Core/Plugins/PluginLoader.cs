// Minimal faithful port using collectible AssemblyLoadContext — webclient sync off, no Windows ACL hardening.
//
// Reflection is confined to plugin discovery here (see SourceHygieneTests
// exclusions): there is no non-reflection mechanism to enumerate a plugin
// assembly's [EntityReplacement] marks, and self-registration would break
// test isolation (module initializers run at assembly load) plus the plugin
// SDK contract Python establishes (import + scan). Everything else in prod
// stays reflection-free.

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
/// Uses <c>AssemblyLoadContext(isCollectible:true)</c> to mirror importlib.reload + _apply_patch semantics.
/// </summary>
public sealed class PluginLoader : IDisposable
{
    // Never reload core/server state; mirrors Python excluded set comments.

    private AssemblyLoadContext? _alc;
    private Assembly? _loaded;

    // weak handle to the most recently unloaded ALC. Replacements +
    // live patched instances root the collectible ALC, so Unload() alone can
    // silently leak one ALC+assembly per reload; the weak ref lets callers
    // VERIFY the unload actually completed (see WaitForUnload).
    private WeakReference? _unloadedAlcRef;

    /// <summary>Registered replacements: base type → replacement type.</summary>
    public Dictionary<Type, Type> Replacements { get; } = new();

    public bool IsLoaded => _loaded is not null;

    /// <summary>
    /// The currently loaded plugin assembly (null after <see cref="Unload"/>).
    /// Lets reload callers identify instances rooted in a replaced load —
    /// e.g. evicting stale command registrations whose types died with it.
    /// </summary>
    public Assembly? LoadedAssembly => _loaded;

    /// <summary>
    /// Creates collectible ALC, loads assembly, scans for <see cref="EntityReplacementAttribute"/>, registers.
    /// Logs via AtherizLogger mirroring <c>logger.info("[HotReload] ...")</c>.
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
            AtherizLogger.LogInformation($"[PluginLoader] Skipping excluded assembly: {name}", "PluginLoader");
            return;
        }

        // Unload any previous ALC first — overwriting _alc without unloading leaks it.
        if (_loaded is not null) Unload();

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
            catch (Exception ex) { AtherizLogger.LogError($"[PluginLoader] Dep resolve {name.Name}: {ex.Message}", "PluginLoader"); return null; }
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
            AtherizLogger.LogError($"[PluginLoader] Failed to load {full}: {ex.Message}", "PluginLoader");
            // Unload the stillborn ALC — overwriting _alc without unloading leaks it.
            try { _alc.Unload(); } catch { }
            _alc = null;
            throw;
        }
        if (File.GetLastWriteTimeUtc(full) != tsBefore)
        {
            AtherizLogger.LogWarning($"[PluginLoader] {full} changed during load; unloading (TOCTOU).", "PluginLoader");
            Unload();
            throw new IOException($"Plugin assembly {full} changed during load; refusing to patch from a torn file.");
        }

        int found = 0;
        Type? gameSetupType = null;
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
                    if (TryRegister(attr.BaseType, attr.ReplacementType, $"from {type.Name}", " (e.g. plugin vendored its own Atheriz.Core copy)")) found++;
                }
                // Game-setup entry for CLI world creation (reset/new): a public
                // non-abstract class implementing IGameSetup. Collected in the
                // same discovery pass, instantiated once below. Visibility is
                // enforced here: without it a private nested IGameSetup helper
                // would win the scan by declaration order and get instantiated
                // through its implicit constructor, hijacking world setup.
                if (gameSetupType is null && type.IsClass && !type.IsAbstract && (type.IsPublic || type.IsNestedPublic) && typeof(IGameSetup).IsAssignableFrom(type))
                    gameSetupType = type;
            }
            // Assembly-level attributes
            foreach (var a in _loaded.GetCustomAttributes<EntityReplacementAttribute>())
            {
                // Double-count guard stays at this call site (not in the helper):
                // an attribute seen in both scans registers and counts once.
                if (Replacements.ContainsKey(a.BaseType)) continue;
                if (TryRegister(a.BaseType, a.ReplacementType, "assembly", "")) found++;
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            AtherizLogger.LogError($"[PluginLoader] Type load errors: {string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message))}", "PluginLoader");
        }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"[PluginLoader] Scan failed: {ex.Message}", "PluginLoader");
        }

        if (found == 0)
            AtherizLogger.LogInformation($"[PluginLoader] Loaded {Path.GetFileName(full)} — no [EntityReplacement] found (mirrors 'No CLASS_INJECTIONS').", "PluginLoader");
        else
            AtherizLogger.LogInformation($"[PluginLoader] Loaded {found} replacement(s) from {Path.GetFileName(full)}.", "PluginLoader");
        // Instantiate the discovered game-setup entry, if any. This is plugin
        // discovery completing (same exemption as the scan above): module
        // initializers do NOT run eagerly on collectible-ALC loads, so
        // self-registration never fires and the CLI would silently fall back
        // to the template world. Constrained to public IGameSetup classes
        // with a public parameterless ctor; a single instance per load.
        if (gameSetupType is not null)
        {
            try
            {
                if (Activator.CreateInstance(gameSetupType) is IGameSetup setup)
                {
                    InitialSetup.GameSetup = setup;
                    AtherizLogger.LogInformation($"[PluginLoader] Game setup: {gameSetupType.FullName}.", "PluginLoader");
                }
                else AtherizLogger.LogWarning($"[PluginLoader] Game setup {gameSetupType.FullName} has no public parameterless ctor; template setup will run.", "PluginLoader");
            }
            catch (Exception ex) { AtherizLogger.LogWarning($"[PluginLoader] Game setup {gameSetupType.FullName} failed: {ex.Message}; template setup will run.", "PluginLoader"); }
        }
    }

    /// <summary>
    /// Replacement contract: the replacement must be assignable to the base, else
    /// <c>PatchLiveObjects</c> could never match live objects (silent no-op patch).
    /// A vendored second <c>Atheriz.Core</c> copy fails this check — its types never
    /// unify with the live ones — so the mismatch is skipped LOUDLY, not silently.
    /// </summary>
    internal static bool IsValidReplacement(Type? baseType, Type? replacementType)
    {
        if (baseType is null || replacementType is null) return false;
        if (baseType == replacementType) return true;
        try { return baseType.IsAssignableFrom(replacementType); }
        catch { return false; }
    }

    // Shared validate → skip-log → register core for the class-level and
    // assembly-level scans above (discovery itself is untouched). The source
    // label and the skip-detail suffix ride along so both messages keep their
    // exact wording. Returns true when the replacement registered (count it).
    private bool TryRegister(Type baseType, Type replacementType, string source, string skipDetail)
    {
        if (!IsValidReplacement(baseType, replacementType))
        {
            AtherizLogger.LogWarning($"[PluginLoader] Skipping {replacementType.FullName} → {baseType.FullName} ({source}): replacement is not assignable to base and would never match live objects{skipDetail}.", "PluginLoader");
            return false;
        }
        Replacements[baseType] = replacementType;
        AtherizLogger.LogInformation($"[PluginLoader] Injected {replacementType.Name} → {baseType.Name} ({source})", "PluginLoader");
        return true;
    }

    /// <summary>
    /// Unload collectible ALC — mirrors Python reload dropping old modules.
    /// Call <c>GC.Collect()</c> after to finalize.
    /// </summary>
    public void Unload()
    {
        Replacements.Clear();
        _loaded = null;
        if (_alc is not null)
        {
            // keep a weak handle so the unload can be VERIFIED.
            // Anything still rooting the ALC (a live patched instance, a
            // leaked Type ref) keeps the weak ref alive — surfacing the leak
            // instead of silently accumulating one assembly per reload.
            _unloadedAlcRef = new WeakReference(_alc);
            try { _alc.Unload(); } catch (Exception ex) { AtherizLogger.LogError($"[PluginLoader] Unload failed: {ex.Message}", "PluginLoader"); }
            _alc = null;
        }
        AtherizLogger.LogInformation("[PluginLoader] Unloaded.", "PluginLoader");
    }

    /// <summary>
    /// True when the most recently unloaded ALC has actually been collected
    /// (no live roots remain). False means something still pins it — treat a
    /// persistent false as a plugin-assembly leak, not a clean unload.
    /// </summary>
    public bool IsUnloaded => _unloadedAlcRef is null || !_unloadedAlcRef.IsAlive;

    /// <summary>
    /// Unloads then pumps GC/finalizers (bounded) until the ALC dies.
    /// Returns true on verified unload; false names a leak, not success.
    /// </summary>
    public bool WaitForUnload(int maxPasses = 10)
    {
        Unload();
        for (int i = 0; i < maxPasses && !IsUnloaded; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
        return IsUnloaded;
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
