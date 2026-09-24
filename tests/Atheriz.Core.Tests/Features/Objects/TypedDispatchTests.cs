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
        Assert.Equal("account_name", ParsedArgKeys.AccountName);
        Assert.Equal("args", ParsedArgKeys.Args);
        Assert.Equal("attribute", ParsedArgKeys.Attribute);
        Assert.Equal("auto", ParsedArgKeys.Auto);
        Assert.Equal("channel", ParsedArgKeys.Channel);
        Assert.Equal("command", ParsedArgKeys.Command);
        Assert.Equal("coord", ParsedArgKeys.Coord);
        Assert.Equal("count", ParsedArgKeys.Count);
        Assert.Equal("d", ParsedArgKeys.D);
        Assert.Equal("desc", ParsedArgKeys.Desc);
        Assert.Equal("double", ParsedArgKeys.Double);
        Assert.Equal("down", ParsedArgKeys.Down);
        Assert.Equal("e", ParsedArgKeys.E);
        Assert.Equal("east", ParsedArgKeys.East);
        Assert.Equal("ip", ParsedArgKeys.Ip);
        Assert.Equal("is_container", ParsedArgKeys.IsContainer);
        Assert.Equal("is_item", ParsedArgKeys.IsItem);
        Assert.Equal("is_mapable", ParsedArgKeys.IsMapable);
        Assert.Equal("is_npc", ParsedArgKeys.IsNpc);
        Assert.Equal("is_pc", ParsedArgKeys.IsPc);
        Assert.Equal("is_tickable", ParsedArgKeys.IsTickable);
        Assert.Equal("list", ParsedArgKeys.List);
        Assert.Equal("message", ParsedArgKeys.Message);
        Assert.Equal("n", ParsedArgKeys.N);
        Assert.Equal("name", ParsedArgKeys.Name);
        Assert.Equal("none", ParsedArgKeys.None);
        Assert.Equal("north", ParsedArgKeys.North);
        Assert.Equal("noun", ParsedArgKeys.Noun);
        Assert.Equal("object", ParsedArgKeys.Object);
        Assert.Equal("password", ParsedArgKeys.Password);
        Assert.Equal("path", ParsedArgKeys.Path);
        Assert.Equal("reason", ParsedArgKeys.Reason);
        Assert.Equal("recursive", ParsedArgKeys.Recursive);
        Assert.Equal("remove", ParsedArgKeys.Remove);
        Assert.Equal("replay", ParsedArgKeys.Replay);
        Assert.Equal("road", ParsedArgKeys.Road);
        Assert.Equal("room", ParsedArgKeys.Room);
        Assert.Equal("round", ParsedArgKeys.Round);
        Assert.Equal("s", ParsedArgKeys.S);
        Assert.Equal("single", ParsedArgKeys.Single);
        Assert.Equal("south", ParsedArgKeys.South);
        Assert.Equal("subscribe", ParsedArgKeys.Subscribe);
        Assert.Equal("target", ParsedArgKeys.Target);
        Assert.Equal("text", ParsedArgKeys.Text);
        Assert.Equal("u", ParsedArgKeys.U);
        Assert.Equal("unsubscribe", ParsedArgKeys.Unsubscribe);
        Assert.Equal("up", ParsedArgKeys.Up);
        Assert.Equal("value", ParsedArgKeys.Value);
        Assert.Equal("w", ParsedArgKeys.W);
        Assert.Equal("west", ParsedArgKeys.West);
        Assert.Equal("x", ParsedArgKeys.X);
        Assert.Equal(52, typeof(ParsedArgKeys).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length);
    }

    [Fact]
    public void ParsedArgCallSites_UseConstants()
    {
        string[] methods = ["AddArgument(\"", "GetString(\"", "GetList(\"", "GetBool(\"", "GetObjList(\"", "Has(\""];
        string[] keys = ["account", "account_name", "args", "attribute", "auto", "channel", "command",
            "coord", "count", "d", "desc", "double", "down", "e", "east", "ip",
            "is_container", "is_item", "is_mapable", "is_npc", "is_pc", "is_tickable",
            "list", "message", "n", "name", "none", "north", "noun", "object",
            "password", "path", "reason", "recursive", "remove", "replay", "road",
            "room", "round", "s", "single", "south", "subscribe", "target", "text",
            "u", "unsubscribe", "up", "value", "w", "west", "x"];
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
