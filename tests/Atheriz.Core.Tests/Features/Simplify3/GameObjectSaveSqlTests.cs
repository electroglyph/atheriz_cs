// Pins for the GetSaveOps/GetSaveOpsClearing dedup: both paths emit one shared
// INSERT statement spelling with their own id + JSON payload, and the flag
// truth table (non-clearing restores, clearing leaves cleared) is unchanged.
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class GameObjectSaveSqlTests
{
    private const string ExpectedSaveSql = "INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)";

    [Fact]
    public void GetSaveOps_NonClearing_SqlMatchesSaveStatement()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-plain");
        var (sql, ps) = obj.GetSaveOps();
        Assert.Equal(ExpectedSaveSql, sql);
        Assert.Equal(obj.Id, Assert.IsType<int>(ps[0]));
        var dto = GameObjectDtoSerializer.FromJson(Assert.IsType<string>(ps[1]));
        Assert.Equal(obj.Id, dto.Id);
        Assert.Equal("saved-plain", dto.Name);
    }

    [Fact]
    public void GetSaveOpsClearing_Clearing_SqlMatchesSaveStatement()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-clearing");
        var (sql, ps) = obj.GetSaveOpsClearing();
        Assert.Equal(ExpectedSaveSql, sql);
        Assert.Equal(obj.Id, Assert.IsType<int>(ps[0]));
        var dto = GameObjectDtoSerializer.FromJson(Assert.IsType<string>(ps[1]));
        Assert.Equal(obj.Id, dto.Id);
        Assert.Equal("saved-clearing", dto.Name);
    }

    [Fact]
    public void GetSaveOps_BothPaths_ShareIdenticalSql()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-twin");
        Assert.Equal(obj.GetSaveOps().Sql, obj.GetSaveOpsClearing().Sql);
    }

    [Fact]
    public void GetSaveOps_Success_RestoresModifiedFlag()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-dirty");
        Assert.True(obj.IsModified);
        obj.GetSaveOps();
        Assert.True(obj.IsModified);
    }

    [Fact]
    public void GetSaveOpsClearing_Success_LeavesModifiedFlagCleared()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-cleared");
        Assert.True(obj.IsModified);
        obj.GetSaveOpsClearing();
        Assert.False(obj.IsModified);
    }
}
