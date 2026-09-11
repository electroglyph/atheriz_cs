using Atheriz.Core.Globals;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The cooldown key is the host verbatim: no normalization, no per-op split.
[Collection("Ported")]
public sealed class CreationCooldownIdentityTests
{
    [Fact]
    public void TryReserveCreationCooldown_DifferentOps_ShareHostKey()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "10.9.9.1";
        ObjectRegistry.ClearCreationCooldown(host);
        try
        {
            Assert.True(ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60));
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            Assert.False(ObjectRegistry.TryReserveCreationCooldown("account", host, 1001, 60));
        }
        finally
        {
            ObjectRegistry.ClearCreationCooldown(host);
        }
    }

    [Fact]
    public void CreationCooldownActive_HostKeyIsVerbatim()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "CaseHost-Verbatim";
        ObjectRegistry.ClearCreationCooldown(host);
        ObjectRegistry.ClearCreationCooldown(host.ToLowerInvariant());
        try
        {
            Assert.True(ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60));
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            Assert.False(ObjectRegistry.CreationCooldownActive(host.ToLowerInvariant(), 1001));
        }
        finally
        {
            ObjectRegistry.ClearCreationCooldown(host);
            ObjectRegistry.ClearCreationCooldown(host.ToLowerInvariant());
        }
    }

    [Fact]
    public void ClearCreationCooldown_VerbatimKey_RemovesOnlyExactHost()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "CaseHost-10.8.8.2";
        ObjectRegistry.ClearCreationCooldown(host);
        try
        {
            Assert.True(ObjectRegistry.TryReserveCreationCooldown("guest", host, 1000, 60));
            ObjectRegistry.ClearCreationCooldown(host.ToUpperInvariant());
            Assert.True(ObjectRegistry.CreationCooldownActive(host, 1001));
            ObjectRegistry.ClearCreationCooldown(host);
            Assert.False(ObjectRegistry.CreationCooldownActive(host, 1001));
        }
        finally
        {
            ObjectRegistry.ClearCreationCooldown(host);
        }
    }
}
