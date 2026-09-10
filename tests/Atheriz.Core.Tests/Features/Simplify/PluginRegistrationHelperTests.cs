using System.Reflection;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared EntityReplacement registration core: both scans validate, skip loudly,
// register, and count through one helper.
public class PluginRegistrationHelperTests
{
    private sealed class RegistrationProbeA : GameObject { }

    private sealed class RegistrationProbeB : GameObject { }

    private static bool TryRegister(PluginLoader loader, Type? baseType, Type? replacement, string source, string detail)
    {
        var method = typeof(PluginLoader).GetMethod("TryRegister", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(method);
        return (bool)method.Invoke(loader, new object?[] { baseType, replacement, source, detail })!;
    }

    [Fact]
    public void TryRegister_Valid_RegistersAndCountsOnce()
    {
        using var loader = new PluginLoader();
        Assert.True(TryRegister(loader, typeof(GameObject), typeof(RegistrationProbeA), "from Probe", ""));
        Assert.True(loader.Replacements.TryGetValue(typeof(GameObject), out var registered));
        Assert.Equal(typeof(RegistrationProbeA), registered);
        Assert.Single(loader.Replacements);
    }

    [Fact]
    public void TryRegister_SameBaseTwice_OverwritesWithoutDoubleCount()
    {
        using var loader = new PluginLoader();
        Assert.True(TryRegister(loader, typeof(GameObject), typeof(RegistrationProbeA), "from Probe", ""));
        Assert.True(TryRegister(loader, typeof(GameObject), typeof(RegistrationProbeB), "assembly", ""));
        Assert.Single(loader.Replacements);
        Assert.Equal(typeof(RegistrationProbeB), loader.Replacements[typeof(GameObject)]);
    }

    [Fact]
    public void TryRegister_Unassignable_SkipsWithoutRegistering()
    {
        using var loader = new PluginLoader();
        Assert.False(TryRegister(loader, typeof(GameObject), typeof(string), "from Probe", ""));
        Assert.Empty(loader.Replacements);
    }
}
