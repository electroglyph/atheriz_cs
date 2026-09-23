using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Objects;

// Section 2.7 pins: stringly dispatch replaced by compiler-checked names.
// HookNames constants carry the exact runtime hook strings (compile proves
// resolution; hook dispatch tests prove the values still match), the input
// queue item is a nominal record, and lock policies have an enum front door
// over the unchanged persisted names.
[Collection("Ported")]
public class TypedDispatchTests
{
    [Fact]
    public void HookableCallSites_UseHookNamesConstants()
    {
        int literals = 0;
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(SourceScan.RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            literals += SourceScan.Count(File.ReadAllText(file), "Hookable(\"");
        }
        Assert.True(literals == 0, $"found {literals} string-literal Hookable call sites");
    }

    [Fact]
    public void HookNames_MatchRuntimeHookStrings()
    {
        Assert.Equal("at_desc", HookNames.AtDesc);
        Assert.Equal("at_delete", HookNames.AtDelete);
        Assert.Equal("at_tick", HookNames.AtTick);
        Assert.Equal("at_post_puppet", HookNames.AtPostPuppet);
        Assert.Equal("return_appearance", HookNames.ReturnAppearance);
        Assert.Equal("at_emit_sound", HookNames.AtEmitSound);
        Assert.Equal("at_pre_emit_sound", HookNames.AtPreEmitSound);
        Assert.Equal("at_pre_hear", HookNames.AtPreHear);
        Assert.Equal(41, typeof(HookNames).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length);
    }

    [Theory]
    [InlineData(LockPolicies.LockPolicy.Builder, "builder")]
    [InlineData(LockPolicies.LockPolicy.PcView, "pc-view")]
    [InlineData(LockPolicies.LockPolicy.NotSelf, "not-self")]
    [InlineData(LockPolicies.LockPolicy.PuppetOwner, "puppet-owner")]
    [InlineData(LockPolicies.LockPolicy.Custom, "custom")]
    [InlineData(LockPolicies.LockPolicy.Denied, "denied")]
    public void LockPolicy_Name_MapsToPersistedSpelling(LockPolicies.LockPolicy policy, string expected)
    {
        Assert.Equal(expected, LockPolicies.Name(policy));
        Assert.True(LockPolicies.TryParseName(expected, out var parsed));
        Assert.Equal(policy, parsed);
    }

    [Fact]
    public void LockPolicy_TryParseName_RejectsUnknown()
    {
        Assert.False(LockPolicies.TryParseName("owner", out _));
        Assert.False(LockPolicies.TryParseName("", out _));
        Assert.False(LockPolicies.TryParseName(null, out _));
    }

    [Fact]
    public void LockPolicy_EnumOverload_ResolvesLikeStringOverload()
    {
        Assert.True(LockPolicies.TryResolve(LockPolicies.LockPolicy.Builder, out _));
        Assert.False(LockPolicies.TryResolve((LockPolicies.LockPolicy)999, out _));
    }

    [Fact]
    public void LockPolicy_Denied_ResolvesToDeny()
    {
        // The fail-closed marker resolves (both arities) to a predicate that
        // denies everyone, so persisted deny entries decide identically.
        var accessor = GameObject.Create("denied_accessor");
        try
        {
            Assert.True(LockPolicies.TryResolve(LockPolicies.LockPolicy.Denied, out var p1));
            Assert.False(p1(accessor));
            var target = GameObject.Create("denied_target");
            try
            {
                Assert.True(LockPolicies.TryResolve(LockPolicies.LockPolicy.Denied, target, out var p2));
                Assert.False(p2(accessor));
            }
            finally { ObjectRegistry.ClearAll(); }
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void InputQueue_UsesNominalRecord()
    {
        var source = SourceScan.Read("src/Atheriz.Core/Network/BaseConnection.cs");
        Assert.Contains("Queue<QueuedInput>", source);
        Assert.Contains("new QueuedInput(handler, args, kwargs)", source);
    }

    [Fact]
    public void ParsedArgKeys_MatchRuntimeKeys()
    {
        Assert.Equal("account", ParsedArgKeys.Account);
        Assert.Equal("args", ParsedArgKeys.Args);
        Assert.Equal("attribute", ParsedArgKeys.Attribute);
        Assert.Equal("command", ParsedArgKeys.Command);
        Assert.Equal("coord", ParsedArgKeys.Coord);
        Assert.Equal("desc", ParsedArgKeys.Desc);
        Assert.Equal("message", ParsedArgKeys.Message);
        Assert.Equal("none", ParsedArgKeys.None);
        Assert.Equal("noun", ParsedArgKeys.Noun);
        Assert.Equal("target", ParsedArgKeys.Target);
        Assert.Equal("text", ParsedArgKeys.Text);
        Assert.Equal(11, typeof(ParsedArgKeys).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length);
    }

    [Fact]
    public void ParsedArgCallSites_UseConstants()
    {
        string[] methods = ["AddArgument(\"", "GetString(\"", "GetList(\"", "GetBool(\"", "GetObjList(\"", "Has(\""];
        string[] keys = ["account", "args", "attribute", "command", "coord", "desc", "message", "none", "noun", "target", "text"];
        int literals = 0;
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(SourceScan.RepoRoot(), "src", "Atheriz.Core", "Commands"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var m in methods)
                foreach (var k in keys)
                    literals += SourceScan.Count(text, m + k + "\"");
        }
        Assert.True(literals == 0, $"found {literals} string-literal parsed-arg key call sites");
    }
}
