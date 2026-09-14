using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Link lookup folds case, so AddLink must too — otherwise the second link is
// installed but unreachable (shadowed).
[Collection("Ported")]
public class AddLinkCaseDedupTests
{
    [Fact]
    public void AddLink_CaseVariant_DoesNotDuplicate()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = new Node(new Coord("LinkCaseDedup", 0, 0, 0));
        ObjectRegistry.AddObject(n1);
        n1.AddLink(new NodeLink("North", new Coord("LinkCaseDedup", 0, 1, 0)));
        n1.AddLink(new NodeLink("north", new Coord("LinkCaseDedup", 0, 2, 0)));
        Assert.Single(n1.GetLinks());
        Assert.NotNull(n1.GetLinkByName("NORTH"));
    }
}
