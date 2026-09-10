using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Script attach/detach/query through one fetch core: missing ids and
// wrong-typed ids both mean "absent", and type matching keeps its lowered
// substring comparison.
[Collection("Ported")]
public class ScriptLookupTests
{
    private sealed class LookupProbeScript : Script
    {
        public bool Fired;
        [Before]
        public void at_lookup_probe() => Fired = true;
    }

    [Fact]
    public void AddRemove_ById_AttachesAndDetaches()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new LookupProbeScript();
            ObjectRegistry.AddObject(child);
            ObjectRegistry.AddObject(script);
            child.AddScript(script.Id);
            Assert.True(child.HasScriptType("LookupProbe"));
            Assert.True(child.HasScriptType("lookupprobe"));
            Assert.Single(child.GetScriptsByType("probe"));
            child.RemoveScript(script.Id);
            Assert.False(child.HasScriptType("LookupProbe"));
            Assert.Empty(child.GetScriptsByType("probe"));
            Assert.False(child.HasHook("at_lookup_probe"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void WrongTypeOrMissingId_MeansAbsent()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var plain = GameObject.Create("plain");
            ObjectRegistry.AddObject(child);
            ObjectRegistry.AddObject(plain);
            // Neither installs hooks nor throws: both spellings of absent.
            child.AddScript(plain.Id);
            child.AddScript(999999);
            Assert.False(child.HasHook("at_lookup_probe"));
            Assert.Empty(child.GetScriptsByType("LookupProbe"));
            child.RemoveScript(plain.Id);
            child.RemoveScript(999999);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ResolveRelations_ReinstallsHooks()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new LookupProbeScript();
            ObjectRegistry.AddObject(child);
            ObjectRegistry.AddObject(script);
            child.AddScript(script.Id);
            Assert.True(child.HasHook("at_lookup_probe"));
            child.ResolveRelations();
            Assert.True(child.HasHook("at_lookup_probe"));
            Assert.Equal(1, child.Hookable<int>("at_lookup_probe", () => 1));
            Assert.True(script.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
