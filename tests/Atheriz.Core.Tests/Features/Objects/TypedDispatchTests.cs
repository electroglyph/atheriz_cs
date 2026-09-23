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
        Assert.Equal(38, typeof(HookNames).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length);
    }

    [Theory]
    [InlineData(LockPolicies.LockPolicy.Builder, "builder")]
    [InlineData(LockPolicies.LockPolicy.PcView, "pc-view")]
    [InlineData(LockPolicies.LockPolicy.NotSelf, "not-self")]
    [InlineData(LockPolicies.LockPolicy.PuppetOwner, "puppet-owner")]
    [InlineData(LockPolicies.LockPolicy.Custom, "custom")]
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
    public void InputQueue_UsesNominalRecord()
    {
        var source = SourceScan.Read("src/Atheriz.Core/Network/BaseConnection.cs");
        Assert.Contains("Queue<QueuedInput>", source);
        Assert.Contains("new QueuedInput(handler, args, kwargs)", source);
    }
}
