// Pins for U5 (Create dual get-lock merge): pc+npc objects persist a single
// get policy entry (not a doubled one), decide get/view identically, and
// legacy doubled rows still load with identical decisions.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class CreateGetLockTests
{
    private static string GetPolicy(GameObject obj)
        => Assert.Single(obj.ToDto().Locks, d => d.Name == "get").Policy;

    [Fact]
    public void Create_DualPcNpc_PersistsSingleBuilderPolicy()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Dual", isPc: true, isNpc: true);
        Assert.Equal(LockPolicies.Builder, GetPolicy(obj));
    }

    [Fact]
    public void Create_DualPcNpc_SaveRoundTrip_DecidesIdentically()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Dual", isPc: true, isNpc: true);
        ObjectRegistry.AddObject(obj);
        var builder = GameObject.Create("Builder", isPc: true, privilege: Privilege.Builder);
        var guest = GameObject.Create("Guest");
        ObjectRegistry.AddObject(builder);
        ObjectRegistry.AddObject(guest);
        bool beforeBuilder = obj.Access(builder, "get");
        bool beforeGuest = obj.Access(guest, "get");

        var restored = GameObject.FromDto(obj.ToDto());

        Assert.Equal(beforeBuilder, restored.Access(builder, "get"));
        Assert.Equal(beforeGuest, restored.Access(guest, "get"));
        Assert.True(restored.Access(builder, "get"));
        Assert.False(restored.Access(guest, "get"));
        Assert.Equal(LockPolicies.Builder, GetPolicy(restored));
    }

    [Fact]
    public void Create_PcOnly_And_NpcOnly_SingleGet()
    {
        using var env = GlobalTestEnv.Enter();
        var pc = GameObject.Create("PcOnly", isPc: true);
        Assert.Equal(LockPolicies.Builder, GetPolicy(pc));
        var npc = GameObject.Create("NpcOnly", isNpc: true);
        Assert.Equal(LockPolicies.Builder, GetPolicy(npc));
    }

    [Fact]
    public void FromDto_LegacyDoubledBuilder_DecidesIdentically()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Dual", isPc: true, isNpc: true);
        var dto = obj.ToDto();
        dto.Locks.Single(d => d.Name == "get").Policy =
            LockPolicies.Builder + "|" + LockPolicies.Builder;
        var legacy = GameObject.FromDto(dto);
        var builder = GameObject.Create("Builder", isPc: true, privilege: Privilege.Builder);
        var guest = GameObject.Create("Guest");
        Assert.True(legacy.Access(builder, "get"));
        Assert.False(legacy.Access(guest, "get"));
    }
}
