using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests.Features.Regression;

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

    private static GameObject MakePlayer(string name)
    {
        var c = GameObject.Create(name, "", isPc: false, privilege: Privilege.Player);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    [Fact]
    public void ApplyDtoFields_UnknownPolicy_DeniesAccess()
    {
        // An unresolvable lock policy must deny (fail closed), never vanish.
        // Strings are gone from the DTO: an out-of-range numeric value stands
        // in for save data naming a policy the engine does not know.
        using var env = GlobalTestEnv.Enter();
        var obj = MakePlayer("locktest");
        var caller = MakePlayer("caller");
        var dto = new GameObjectDto
        {
            Id = obj.Id,
            Name = "locktest",
            Locks = [new LockDefDto { Name = "view", Policies = [(LockPolicies.LockPolicy)999] }],
        };
        GameObject.ApplyDtoFields(obj, dto, null);
        Assert.False(obj.Access(caller, "view"));
    }

    [Fact]
    public void ApplyDtoFields_CustomPolicy_DroppedWithAllow()
    {
        // The ad-hoc 'custom' lambda was never persistable — still dropped
        // (allowed), loudly.
        using var env = GlobalTestEnv.Enter();
        var obj = MakePlayer("locktest2");
        var caller = MakePlayer("caller2");
        var dto = new GameObjectDto
        {
            Id = obj.Id,
            Name = "locktest2",
            Locks = [new LockDefDto { Name = "view", Policies = [LockPolicies.LockPolicy.Custom] }],
        };
        GameObject.ApplyDtoFields(obj, dto, null);
        Assert.True(obj.Access(caller, "view"));
    }

    [Fact]
    public void ApplyDtoFields_DeniedMarker_DeniesAndResavesDenied()
    {
        // The fail-closed marker survives a load and re-saves as itself, so a
        // deny entry never washes out into allow across save/load cycles.
        using var env = GlobalTestEnv.Enter();
        var obj = MakePlayer("locktest3");
        var caller = MakePlayer("caller3");
        var dto = new GameObjectDto
        {
            Id = obj.Id,
            Name = "locktest3",
            Locks = [new LockDefDto { Name = "view", Policies = [LockPolicies.LockPolicy.Denied] }],
        };
        GameObject.ApplyDtoFields(obj, dto, null);
        Assert.False(obj.Access(caller, "view"));
        Assert.Equal([LockPolicies.LockPolicy.Denied],
            obj.ToDto().Locks.Single(d => d.Name == "view").Policies);
    }

    [Fact]
    public void NoParallelPolicyTable_RemainsSingleCollection()
    {
        // One lock collection only: the parallel policy table is gone from
        // both holders, so predicates and policies cannot drift out of sync.
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        Assert.DoesNotContain("_lockPolicies", src);
        var door = SourceScan.Read("src", "Atheriz.Core", "Objects", "Door.cs");
        Assert.DoesNotContain("_lockPolicies", door);
    }
}
