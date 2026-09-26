using System.Collections;
using System.Reflection;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;

namespace Atheriz.Core.Tests.Features.Persistence;

// Audit 10 finding 10: re-registering a subtype name must prune the
// superseded Type key. Holding a Type roots its AssemblyLoadContext, so
// without the prune every hot reload pins the previous plugin generation
// forever. Neuter (drop the prune loop) leaves 2 keys for 1 factory.
[Collection("Ported")]
public sealed class SubtypePruneTests
{
    private static IDictionary SubtypeNames()
    {
        var field = typeof(GameObjectDtoConverter).GetField("_subtypeNames", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (IDictionary)field.GetValue(null)!;
    }

    [Fact]
    public void RegisterSubtype_SameNameNewType_PrunesOldTypeKey()
    {
        using var _ = GlobalTestEnv.Enter();
        const string name = "audit10probe10a";
        GameObjectDtoConverter.RegisterSubtype(name, typeof(GameObject), () => GameObject.Create("probe-a"));
        GameObjectDtoConverter.RegisterSubtype(name, typeof(Account), () => GameObject.Create("probe-b"));
        var dict = SubtypeNames();
        int ours = dict.Keys.Cast<object>().Count(k => k is Type && dict[k] is string s && s == name);
        Assert.Equal(1, ours);
        // The factory still points at the latest generation.
        Assert.True(GameObjectDtoConverter.TryCreateSubtype(name, out var inst));
        Assert.NotNull(inst);
    }

    [Fact]
    public void RegisterSubtype_DistinctNames_KeepBothKeys()
    {
        using var _ = GlobalTestEnv.Enter();
        GameObjectDtoConverter.RegisterSubtype("audit10probe10b", typeof(GameObject), () => GameObject.Create("a"));
        GameObjectDtoConverter.RegisterSubtype("audit10probe10c", typeof(Account), () => GameObject.Create("b"));
        var dict = SubtypeNames();
        int ours = dict.Keys.Cast<object>().Count(k => k is Type && dict[k] is string s &&
            (s == "audit10probe10b" || s == "audit10probe10c"));
        Assert.Equal(2, ours);
    }
}
