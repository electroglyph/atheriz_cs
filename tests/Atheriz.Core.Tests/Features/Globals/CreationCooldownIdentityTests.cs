using Atheriz.Core.Globals;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The cooldown key is the host verbatim (no normalization), scoped per op:
// account, character, and guest budgets are independent on one host.
[Collection("Ported")]
public sealed class CreationCooldownIdentityTests
{
    [Fact]
    public void TryReserveCreationCooldown_DifferentOps_UseIndependentKeys()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "10.9.9.1";
        CreationCooldownStore.Clear();
        try
        {
            var t1 = ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60);
            Assert.NotNull(t1);
            Assert.True(ObjectRegistry.CreationCooldownActive("guest", host, 1001));
            // A guest reservation must not block the account budget: create
            // followed by new on one host is the normal onboarding flow.
            Assert.False(ObjectRegistry.CreationCooldownActive("account", host, 1001));
            Assert.NotNull(ObjectRegistry.TryReserveCreationCooldown("account", host, 1001, 60));
            // Same-op repeats are still throttled.
            Assert.Null(ObjectRegistry.TryReserveCreationCooldown("guest", host, 1001, 60));
            ObjectRegistry.ClearCreationCooldown("guest", host, t1.Value);
            Assert.False(ObjectRegistry.CreationCooldownActive("guest", host, 1001));
            Assert.True(ObjectRegistry.CreationCooldownActive("account", host, 1001));
        }
        finally
        {
            CreationCooldownStore.Clear();
        }
    }

    [Fact]
    public void CreationCooldownActive_HostKeyIsVerbatim()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "CaseHost-Verbatim";
        CreationCooldownStore.Clear();
        try
        {
            var t1 = ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60);
            Assert.NotNull(t1);
            Assert.True(ObjectRegistry.CreationCooldownActive("guest", host, 1001));
            Assert.False(ObjectRegistry.CreationCooldownActive("guest", host.ToLowerInvariant(), 1001));
        }
        finally
        {
            CreationCooldownStore.Clear();
        }
    }

    [Fact]
    public void ClearCreationCooldown_VerbatimKey_RemovesOnlyExactHost()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "CaseHost-10.8.8.2";
        CreationCooldownStore.Clear();
        try
        {
            var t1 = ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60);
            Assert.NotNull(t1);
            ObjectRegistry.ClearCreationCooldown("guest", host.ToUpperInvariant(), Guid.NewGuid());
            Assert.True(ObjectRegistry.CreationCooldownActive("guest", host, 1001));
            // Exact host but a foreign token also clears nothing.
            ObjectRegistry.ClearCreationCooldown("guest", host, Guid.NewGuid());
            Assert.True(ObjectRegistry.CreationCooldownActive("guest", host, 1001));
            ObjectRegistry.ClearCreationCooldown("guest", host, t1.Value);
            Assert.False(ObjectRegistry.CreationCooldownActive("guest", host, 1001));
        }
        finally
        {
            CreationCooldownStore.Clear();
        }
    }

    [Fact]
    public void ApplyCreationCooldown_KeepsOwnerToken_SoPostApplyClearReleases()
    {
        // Apply reuses the reserving token: a failure after Apply (cap race,
        // failed attach) can still release the entry with the same token
        // instead of leaking a cooldown for a creation that never happened.
        using var env = GlobalTestEnv.Enter();
        const string host = "10.9.9.7";
        CreationCooldownStore.Clear();
        try
        {
            var t1 = ObjectRegistry.TryReserveCreationCooldown("character", host, 1000, 60);
            Assert.NotNull(t1);
            ObjectRegistry.ApplyCreationCooldown("character", host, 1001, 60, t1.Value);
            Assert.True(ObjectRegistry.CreationCooldownActive("character", host, 1002));
            ObjectRegistry.ClearCreationCooldown("character", host, t1.Value);
            Assert.False(ObjectRegistry.CreationCooldownActive("character", host, 1002));
            Assert.NotNull(ObjectRegistry.TryReserveCreationCooldown("character", host, 1002, 60));
        }
        finally
        {
            CreationCooldownStore.Clear();
        }
    }
}
