using Atheriz.Core.Globals;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// All nine singleton holders clear together on Reset and on shutdown, the
// TryGet readers observe the clear, and Reset keeps its registry tail.
[Collection("Ported")]
public sealed class SingletonHolderTests
{
    [Fact]
    public void TryGet_BeforeInit_ReturnsNull()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Null(GlobalServices.TryGetTicker());
        Assert.Null(GlobalServices.TryGetPool());
        Assert.Null(GlobalServices.TryGetGameTime());
        Assert.Null(GlobalServices.TryGetMapHandler());
        Assert.Null(GlobalServices.TryGetNodeHandler());
        Assert.Null(GlobalServices.TryGetConnectionManager());
    }

    [Fact]
    public void Reset_ClearsAllHoldersAndRebuildsCommandSets()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.GetNodeHandler();
        GlobalServices.GetMapHandler();
        GlobalServices.GetAsyncTicker();
        GlobalServices.GetLoggedInCmdSet();
        GlobalServices.GetUnloggedInCmdSet();
        GlobalServices.GetConnectionManager();
        Assert.NotNull(GlobalServices.TryGetNodeHandler());
        Assert.NotNull(GlobalServices.TryGetMapHandler());
        Assert.NotNull(GlobalServices.TryGetTicker());

        GlobalServices.Reset();

        Assert.Null(GlobalServices.TryGetTicker());
        Assert.Null(GlobalServices.TryGetPool());
        Assert.Null(GlobalServices.TryGetGameTime());
        Assert.Null(GlobalServices.TryGetMapHandler());
        Assert.Null(GlobalServices.TryGetNodeHandler());
        Assert.Null(GlobalServices.TryGetConnectionManager());

        Assert.NotNull(GlobalServices.GetLoggedInCmdSet());
        Assert.NotNull(GlobalServices.GetUnloggedInCmdSet());
    }

    [Fact]
    public void ClearForShutdown_ClearsAllHolders()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.GetNodeHandler();
        GlobalServices.GetMapHandler();
        GlobalServices.GetAsyncTicker();
        GlobalServices.GetConnectionManager();
        Assert.NotNull(GlobalServices.TryGetNodeHandler());
        Assert.NotNull(GlobalServices.TryGetConnectionManager());

        GlobalServices.ClearForShutdown();

        Assert.Null(GlobalServices.TryGetTicker());
        Assert.Null(GlobalServices.TryGetPool());
        Assert.Null(GlobalServices.TryGetGameTime());
        Assert.Null(GlobalServices.TryGetMapHandler());
        Assert.Null(GlobalServices.TryGetNodeHandler());
        Assert.Null(GlobalServices.TryGetConnectionManager());
    }
}
