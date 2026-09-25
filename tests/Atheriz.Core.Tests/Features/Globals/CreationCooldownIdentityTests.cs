using Atheriz.Core.Globals;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The cooldown key is the host verbatim: no normalization, no per-op split.
[Collection("Ported")]
public sealed class CreationCooldownIdentityTests
{
    [Fact]
    public void TryReserveCreationCooldown_DifferentOps_ShareHostKey()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "10.9.9.1";
        CreationCooldownStore.Clear();
        try
        {
            var t1 = ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60);
            Assert.NotNull(t1);
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            Assert.Null(ObjectRegistry.TryReserveCreationCooldown("account", host, 1001, 60));
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
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            Assert.False(ObjectRegistry.CreationCooldownActive(host.ToLowerInvariant(), 1001));
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
            ObjectRegistry.ClearCreationCooldown(host.ToUpperInvariant(), Guid.NewGuid());
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            // Exact host but a foreign token also clears nothing.
            ObjectRegistry.ClearCreationCooldown(host, Guid.NewGuid());
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            ObjectRegistry.ClearCreationCooldown(host, t1.Value);
            Assert.False(ObjectRegistry.CreationCooldownActive(host, 1001));
        }
        finally
        {
            CreationCooldownStore.Clear();
        }
    }
}
