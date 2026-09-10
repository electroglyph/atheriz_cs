using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Lock restore shares the AddLock insert core: a DTO round-trip rebuilds
// working predicates from the persisted policy names (no silent weakening).
[Collection("Ported")]
public class LockRestoreTests
{
    private static GameObject RoundTrip(GameObject o)
    {
        string json = GameObjectDtoSerializer.ToJson(o.ToDto());
        return GameObject.FromDto(GameObjectDtoSerializer.FromJson(json));
    }

    [Fact]
    public void BuilderPolicy_SurvivesRoundTrip()
    {
        var o = GameObject.Create("gated");
        o.AddLock("get", accessing => accessing.IsBuilder, LockPolicies.Builder);
        var back = RoundTrip(o);
        var builder = GameObject.Create("bob", privilege: Privilege.Builder);
        var plain = GameObject.Create("hal");
        Assert.True(back.Access(builder, "get"));
        Assert.False(back.Access(plain, "get"));
    }

    [Fact]
    public void CustomLambda_DroppedLoudly_NotRestored()
    {
        // Ad-hoc lambdas cannot survive a round-trip (persisted as "custom",
        // dropped on load): the restored object falls back to default-allow
        // instead of resurrecting a stale closure.
        var o = GameObject.Create("gated");
        o.AddLock("get", _ => false);
        var plain = GameObject.Create("hal");
        Assert.False(o.Access(plain, "get"));
        var back = RoundTrip(o);
        Assert.True(back.Access(plain, "get"));
    }
}
