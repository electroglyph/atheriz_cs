using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Builder policy: the 1-arg and 2-arg overloads share one target-independent
// leaf, so both resolve identically for builders, non-builders, and quelled
// builders (quell suppresses the builder bit in both).
[Collection("Ported")]
public class BuilderPolicyParityTests
{
    [Fact]
    public void BuilderPolicy_BothOverloads_Agree()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var target = GameObject.Create("target");
            var builder = GameObject.Create("builder", privilege: Privilege.Builder);
            var player = GameObject.Create("player", privilege: Privilege.Player);

            Assert.True(LockPolicies.TryResolve(LockPolicies.Builder, out var p1));
            Assert.True(LockPolicies.TryResolve(LockPolicies.Builder, target, out var p2));
            Assert.True(p1(builder));
            Assert.True(p2(builder));
            Assert.False(p1(player));
            Assert.False(p2(player));

            builder.Quelled = true;
            Assert.False(p1(builder));
            Assert.False(p2(builder));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void BuilderPolicy_Unknown_ReturnsFalse()
    {
        var target = GameObject.Create("target");
        Assert.False(LockPolicies.TryResolve("no-such-policy", out _));
        Assert.False(LockPolicies.TryResolve("no-such-policy", target, out _));
    }
}
