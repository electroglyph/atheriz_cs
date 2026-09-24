using System.Reflection;
using System.Runtime.Loader;

namespace Atheriz.Core.Plugins;

// Collectible load context for one plugin assembly. Framework and engine
// assemblies unify with the already-loaded copies — returning null falls
// back to the default context, so a plugin-vendored second Atheriz.Core
// can never shadow the live one and silently miss replacements.
// Plugin-local managed deps, natives, and resources resolve from beside
// the plugin dll through the BCL AssemblyDependencyResolver (deps.json +
// directory probe), not a hand path combine.
internal sealed class GamePluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public GamePluginLoadContext(string pluginPath)
        : base($"game-{Guid.NewGuid():N}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginPath);
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && PluginReloader.IsExcludedAssembly(assemblyName.Name + ".dll"))
            return null;
        string? path;
        try { path = _resolver.ResolveAssemblyToPath(assemblyName); }
        catch { return null; }
        if (path is null) return null;
        try { return LoadFromAssemblyPath(path); }
        catch (Exception ex) { AtherizLogger.LogError($"[PluginLoader] Dep resolve {assemblyName.Name}: {ex.Message}", "PluginLoader"); return null; }
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path;
        try { path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName); }
        catch { return IntPtr.Zero; }
        if (path is null) return IntPtr.Zero;
        try { return LoadUnmanagedDllFromPath(path); }
        catch { return IntPtr.Zero; }
    }
}
