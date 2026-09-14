using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Node.Delete must survive throwing move hooks on contents: the rescue move
// fails closed and the content is deleted instead.
[Collection("Ported")]
public class NodeDeleteHookFailureTests
{
    private sealed class ThrowOnPostMove : GameObject
    {
        public override void AtPostMove(GameObject? destination, string? toExit = null)
            => throw new InvalidOperationException("boom in AtPostMove");
    }

    [Fact]
    public void NodeDelete_WithThrowingMoveHook_CompletesWithoutThrowing()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = new Node(new Coord("DelHookFail", 0, 0, 0));
        var n2 = new Node(new Coord("DelHookFail", 0, 1, 0));
        ObjectRegistry.AddObject(n1);
        ObjectRegistry.AddObject(n2);
        var caller = GameObject.Create("Caller", isPc: true);
        ObjectRegistry.AddObject(caller);
        var content = new ThrowOnPostMove { Name = "Fragile" };
        ObjectRegistry.AddObject(content);
        content.Home = new LocationRef.CoordLocation(n2.Coord);
        n1.AddObject(content);

        var ex = Record.Exception(() => n1.Delete(caller, recursive: false));
        Assert.Null(ex);
        Assert.True(content.IsDeleted);
        Assert.True(n1.IsDeleted);
    }
}
