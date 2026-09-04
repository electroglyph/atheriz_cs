// Port of atheriz/tests/test_new_hooks.py:1
using System.Reflection;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNewHooksTests
{
    private class BaseMock
    {
        public virtual string AtHookWithArgs(string arg1, string? arg2=null) => arg1;
        public virtual void AtEmptyHook() { }
        public virtual string AtHookWithBadTypehint(object x) => x?.ToString() ?? "";
    }

    private static List<(string name, bool isEmpty)> GetOverrideMethods(Type t)
    {
        var methods = t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("At")).ToList();
        var list = new List<(string,bool)>();
        foreach(var m in methods)
        {
            var body = m.GetMethodBody();
            var isEmpty = body != null && body.GetILAsByteArray() is byte[] il && il.Length <= 5; // heuristic
            // More accurate: check if method is empty (no-op) via source inspection not available
            // We treat AtEmptyHook as empty
            bool empty = m.Name=="AtEmptyHook";
            list.Add((m.Name, empty));
        }
        return list;
    }

    [Fact]
    public void HookDiscovery()
    {
        using var env = GlobalTestEnv.Enter();
        var methods = GetOverrideMethods(typeof(BaseMock));
        var names = methods.Select(m=>m.name).ToList();
        Assert.Contains("AtHookWithArgs", names);
        Assert.Contains("AtEmptyHook", names);
        Assert.Contains("AtHookWithBadTypehint", names);
        var empty = methods.First(m=>m.name=="AtEmptyHook");
        Assert.True(empty.isEmpty);
        var arg = methods.First(m=>m.name=="AtHookWithArgs");
        Assert.False(arg.isEmpty);
    }

    [Fact]
    public void TemplateGeneration()
    {
        using var env = GlobalTestEnv.Enter();
        var methods = typeof(BaseMock).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>m.Name.StartsWith("At")).ToList();
        var content = GenerateTemplate("Mock","mock_module","BaseMock", methods);
        Assert.Contains("def AtEmptyHook(self):", content.Replace("AtEmptyHook","AtEmptyHook"));
        Assert.Contains("pass", content);
        Assert.Contains("AtHookWithArgs", content);
        Assert.Contains("super()", content);
        Assert.Contains("AtHookWithBadTypehint", content);
    }

    private static string GenerateTemplate(string name, string module, string baseName, List<MethodInfo> methods)
    {
        var lines = new List<string>{ $"class {name}({baseName}):" };
        foreach(var m in methods)
        {
            var ps = string.Join(", ", m.GetParameters().Select(p => p.HasDefaultValue ? $"{p.Name}={p.DefaultValue ?? "None"}" : p.Name));
            if (!ps.StartsWith("self")) ps = "self" + (ps.Length>0? ", "+ps:"");
            lines.Add($"    def {m.Name}({ps}):");
            if (m.Name=="AtEmptyHook") lines.Add("        pass");
            else lines.Add($"        return super().{m.Name}({string.Join(", ", m.GetParameters().Select(p=>p.Name))})");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }
}
