using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify3;

// DoSetup resolves the Builder policy once and shares the predicate across the
// seeded get/send/view locks, so builders pass and non-builders are denied on
// every seeded lock.
[Collection("Ported")]
public class SeedWorldLockTests
{
    private static Func<GameObject, bool> ResolveBuilderPred()
    {
        Assert.True(LockPolicies.TryResolve(LockPolicies.Builder, out var pred));
        return pred;
    }

    [Fact]
    public void BuilderPredicate_NonBuilder_DeniedOnGetSendView()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var pred = ResolveBuilderPred();
            var item = GameObject.Create("seeded-button", isItem: true);
            item.AddLock("get", pred, LockPolicies.Builder);
            var chan = Channel.Create("SeedServerDeny");
            chan.AddLock("send", pred, LockPolicies.Builder);
            chan.AddLock("view", pred, LockPolicies.Builder);
            var pleb = GameObject.Create("pleb");

            Assert.False(item.Access(pleb, "get"));
            Assert.False(chan.Access(pleb, "send"));
            Assert.False(chan.Access(pleb, "view"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void BuilderPredicate_Builder_AllowedOnGetSendView()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var pred = ResolveBuilderPred();
            var item = GameObject.Create("seeded-button", isItem: true);
            item.AddLock("get", pred, LockPolicies.Builder);
            var chan = Channel.Create("SeedServerAllow");
            chan.AddLock("send", pred, LockPolicies.Builder);
            chan.AddLock("view", pred, LockPolicies.Builder);
            var builder = GameObject.Create("seed-builder", privilege: Privilege.Builder);

            Assert.True(builder.IsBuilder);
            Assert.True(item.Access(builder, "get"));
            Assert.True(chan.Access(builder, "send"));
            Assert.True(chan.Access(builder, "view"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void TryResolve_UnknownPolicy_ReturnsFalseWithDenyAll()
    {
        ObjectRegistry.ClearAll();
        try
        {
            Assert.False(LockPolicies.TryResolve("no-such-policy", out var pred));
            var pleb = GameObject.Create("pleb-miss");
            Assert.False(pred(pleb));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void DoSetup_ResolvesBuilderOnceAndSharesPredicate()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.Equal(1, SourceScan.Count(src, "LockPolicies.TryResolve(LockPolicies.Builder, out var builderPred)"));
        Assert.Equal(3, SourceScan.Count(src, "builderPred, LockPolicies.Builder"));
        Assert.Equal(0, SourceScan.Count(src, "(GameObject x) => x.IsBuilder"));
    }
}
