using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

// Batch A removal pins: stringly-typed front doors deleted in favor of typed
// members. Each pin fails if the removed shape is reintroduced.
[Collection("Ported")]
public class Section3BRemovalPinsTests
{
    [Fact]
    public void Flags_TrySet_Removed()
    {
        Assert.Null(typeof(Flags).GetMethod("TrySet"));
    }

    [Fact]
    public void NodeArea_GetOrCreateGrid_Removed()
    {
        Assert.Null(typeof(NodeArea).GetMethod("GetOrCreateGrid"));
        Assert.NotNull(typeof(NodeArea).GetMethod("GetOrAddGrid"));
    }

    [Fact]
    public void Door_IsClosedIsLocked_Removed()
    {
        var props = typeof(Door).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("IsClosed", props);
        Assert.DoesNotContain("IsLocked", props);
        Assert.Contains("Closed", props);
        Assert.Contains("Locked", props);
    }

    [Fact]
    public void Account_Delete_VirtualDispatch()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("DispatchAccount", "password");
        GameObject asBase = acc;
        var res = asBase.Delete(null);
        Assert.NotNull(res);
        Assert.True(acc.IsDeleted);
        Assert.DoesNotContain(acc.Id, ObjectRegistry.FilterBy(_ => true).Select(o => o.Id));
    }
}
