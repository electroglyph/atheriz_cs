// Pins for the BuildSaveJson catch removal: a throwing snapshot still restores
// the modified flag through the finally (which already covers every failure
// via snapshotOk == false) in both clearing modes, rethrows the original
// error unwrapped, and always releases the write lock.
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Persistence;

[Collection("Ported")]
public sealed class BuildSaveJsonFlagRestoreTests
{
    private static GameObjectDto ThrowingSnapshot() =>
        throw new InvalidOperationException("snapshot boom");

    [Fact]
    public void BuildSaveJson_SnapshotThrowsNonClearing_RestoresModifiedFlagAndReleasesLock()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("save-fails-plain");
        Assert.True(obj.IsModified);
        var ex = Assert.Throws<InvalidOperationException>(
            () => GameObjectDtoConverter.BuildSaveJson(obj, ThrowingSnapshot, clearing: false));
        Assert.Equal("snapshot boom", ex.Message);
        Assert.True(obj.IsModified);
        Assert.False(obj.SyncRoot.IsWriteLockHeld);
    }

    [Fact]
    public void BuildSaveJson_SnapshotThrowsClearing_RestoresModifiedFlagAndReleasesLock()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("save-fails-clearing");
        Assert.True(obj.IsModified);
        var ex = Assert.Throws<InvalidOperationException>(
            () => GameObjectDtoConverter.BuildSaveJson(obj, ThrowingSnapshot, clearing: true));
        Assert.Equal("snapshot boom", ex.Message);
        Assert.True(obj.IsModified);
        Assert.False(obj.SyncRoot.IsWriteLockHeld);
    }

    [Fact]
    public void BuildSaveJson_EncodeHookThrowing_PreservesLiveDirtyFlag()
    {
        // A mutation that dirties the object during the unlocked encode
        // window must survive a failed encode (live flag OR stale snapshot).
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("save-fails-hook");
        obj.IsModified = false;
        GameObjectDtoSerializer.ToJsonHook = _ => { obj.IsModified = true; throw new InvalidOperationException("boom"); };
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                GameObjectDtoConverter.BuildSaveJson(obj, () => GameObjectDto.Create(obj.Id, "save-fails-hook"), false));
            Assert.True(obj.IsModified);
        }
        finally { GameObjectDtoSerializer.ToJsonHook = null; }
    }
}
