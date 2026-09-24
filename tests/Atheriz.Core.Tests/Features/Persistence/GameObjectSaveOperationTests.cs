// Pins for the typed SaveOperation row: the checkpoint row carries the object
// id plus its DTO JSON payload (no SQL text, no positional params), and the
// flag truth table (non-clearing restores, clearing leaves cleared) is unchanged.
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Persistence;

[Collection("Ported")]
public sealed class GameObjectSaveOperationTests
{
    [Fact]
    public void GetSaveOperation_NonClearing_CarriesIdAndJson()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-plain");
        var op = obj.GetSaveOperation();
        Assert.Equal(obj.Id, op.Id);
        var dto = GameObjectDtoSerializer.FromJson(op.Json);
        Assert.Equal(obj.Id, dto.Id);
        Assert.Equal("saved-plain", dto.Name);
    }

    [Fact]
    public void GetSaveOperationClearing_CarriesIdAndJson()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-clearing");
        var op = obj.GetSaveOperationClearing();
        Assert.Equal(obj.Id, op.Id);
        var dto = GameObjectDtoSerializer.FromJson(op.Json);
        Assert.Equal(obj.Id, dto.Id);
        Assert.Equal("saved-clearing", dto.Name);
    }

    [Fact]
    public void GetSaveOperation_BothPaths_ShareOneCore()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-twin");
        var plain = obj.GetSaveOperation();
        var clearing = obj.GetSaveOperationClearing();
        Assert.Equal(plain.Id, clearing.Id);
        var a = GameObjectDtoSerializer.FromJson(plain.Json);
        var b = GameObjectDtoSerializer.FromJson(clearing.Json);
        Assert.Equal(a.Name, b.Name);
        // Both rows snapshot with the flag cleared (the non-clearing path
        // restores it on the live object afterwards, not in the row).
        Assert.False(a.IsModified);
        Assert.False(b.IsModified);
    }

    [Fact]
    public void GetSaveOperation_Success_RestoresModifiedFlag()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-dirty");
        Assert.True(obj.IsModified);
        obj.GetSaveOperation();
        Assert.True(obj.IsModified);
    }

    [Fact]
    public void GetSaveOperationClearing_Success_LeavesModifiedFlagCleared()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("saved-cleared");
        Assert.True(obj.IsModified);
        obj.GetSaveOperationClearing();
        Assert.False(obj.IsModified);
    }
}
