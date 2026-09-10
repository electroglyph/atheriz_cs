using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Simplify;

// The five custom-file emitters share one shape (header + ctor + hooks +
// closing brace) via one generator method, one kind table, and one
// ref/out/in/params rule. Emitted text is unchanged.
[Collection("Ported")]
public class CustomEmitterKindTableTests
{
    private static string Invoke(string method, string ns)
    {
        var m = typeof(GameTemplateGenerator).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)m.Invoke(null, [ns])!;
    }

    [Fact]
    public void Emitters_ShareGenCustom_AndKindTable()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static string GenCustom("));
        foreach (var emitter in new[] { "CO(", "CN(", "CA(", "CC(", "CS(" })
            Assert.True(SourceScan.Count(src, emitter) >= 1, emitter);
        foreach (var name in new[] { "private static string CO(", "private static string CN(", "private static string CA(", "private static string CC(", "private static string CS(" })
        {
            var region = SourceScan.Region(src, name);
            Assert.Contains("GenCustom(", region);
            Assert.DoesNotContain("GenerateHooksFor(typeof(", region);
        }
        Assert.Equal(2, SourceScan.Count(src, "CustomKinds")); // table def + Scaffold walk
        Assert.Contains("(\"CustomObject.cs\", CO)", src);
        Assert.Contains("(\"CustomScript.cs\", CS)", src);
    }

    [Fact]
    public void ModifierRule_LivesInOneHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static string ModifierPrefix("));
        Assert.Contains("ModifierPrefix(p)", src);
        // The declaration builder keeps its signature and core (escape +
        // invariant-culture pins in CliSettingsTemplateTests still apply).
        var region = SourceScan.Region(src, "private static string BuildParamList(");
        Assert.Contains("IsByRef", region);
        Assert.Contains("InvariantCulture", region);
    }

    [Fact]
    public void EmittedFiles_KeepShape_ClassCtorHooksClose()
    {
        string co = Invoke("CO", "G1");
        Assert.Contains("public class CustomObject : GameObject", co);
        Assert.Contains("public CustomObject(string name, bool isPc = false)", co);
        Assert.Contains("public override", co);
        Assert.EndsWith("}\n", co);
        string cs = Invoke("CS", "G1");
        Assert.Contains("public class CustomScript : Script", cs);
        Assert.EndsWith("}\n", cs);
    }

    private void RefOutSample(ref int a, out int b, in int c, params int[] rest) { b = a + c + rest.Length; }

    [Fact]
    public void ArgList_MirrorsModifiers()
    {
        var m = typeof(GameTemplateGenerator).GetMethod("BuildArgList", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sample = typeof(CustomEmitterKindTableTests).GetMethod("RefOutSample", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = (string)m.Invoke(null, [sample])!;
        Assert.Contains("ref a", args);
        Assert.Contains("out b", args);
        Assert.Contains("in c", args);
        Assert.Contains("rest", args);
    }
}
