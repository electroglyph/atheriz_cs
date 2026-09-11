// Pins for the dead same-id recheck removal (GameObject.Puppet.cs): the
// reference-or-same-id `==` guard alone refuses self-puppeting and
// same-id reload instances, and the operator keeps its same-id semantics.
using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class PuppetSameIdGuardTests
{
    private static void SetId(GameObject o, int id)
    {
        var f = typeof(GameObject).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        f!.SetValue(o, id);
    }

    [Fact]
    public void Puppet_SelfPuppet_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("hero", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(caller);
        var session = new Session();

        Assert.False(caller.Puppet(session, caller));
    }

    [Fact]
    public void Puppet_SameIdReloadInstance_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("hero", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(caller);
        var twin = GameObject.Create("hero-twin");
        SetId(twin, caller.Id);
        var session = new Session();

        Assert.False(caller.Puppet(session, twin));
    }

    [Fact]
    public void Operator_SameIdInstances_CompareEqual()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("hero", isPc: true, privilege: Privilege.Admin);
        var twin = GameObject.Create("hero-twin");
        SetId(twin, caller.Id);
        var other = GameObject.Create("other");

        Assert.True(twin == caller);
        Assert.False(twin != caller);
        Assert.True(caller.Equals(twin));
        Assert.Equal(caller.GetHashCode(), twin.GetHashCode());
        Assert.False(caller == other);
        Assert.True(caller != other);
    }
}
