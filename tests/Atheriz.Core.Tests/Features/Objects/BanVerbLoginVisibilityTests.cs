using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Objects.VerbConjugation;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Pins for ban/verb/login/visibility behavior.
[Collection("Ported")]
public class BanVerbLoginVisibilityTests
{
    // Writing the canonical ban key clears the legacy spelling so no stale
    // key lingers beside it.
    [Fact]
    public void BanReason_Write_ClearsLegacyKey()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("banned");
            o.SetExtraJson("banReason", JsonSerializer.SerializeToElement("old"));
            Assert.Equal("old", o.BanReason);
            o.BanReason = "new";
            Assert.Equal("new", o.BanReason);
            Assert.False(o.HasExtra("banReason"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // The stance forms come straight from the tense callees, whose
    // infinitive/past fallbacks never return empty — no empty-guards remain.
    [Fact]
    public void VerbStance_Forms_ComeFromCalleesDirectly()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "VerbConjugation", "Conjugate.cs");
        var region = SourceScan.Region(src, "public static (string second, string third) VerbActorStanceComponents");
        Assert.DoesNotContain("IsNullOrEmpty", region);
    }

    [Fact]
    public void VerbStance_UnknownVerb_ReturnsVerbUnchanged()
    {
        var (you, them) = Conjugate.VerbActorStanceComponents("florp");
        Assert.Equal("florp", you);
        Assert.Equal("florp", them);
    }

    // Moving to the current location leaves both ends clean: the membership
    // is already correct, so no checkpoint write is owed.
    [Fact]
    public void MoveTo_SameLocation_SkipsContentsChurn()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("banverb", 0, 0, 0));
            ObjectRegistry.AddObject(node);
            var mover = GameObject.Create("stayer");
            Assert.True(mover.MoveTo(node));
            Assert.Contains(mover.Id, node.ContentsSnapshot);
            node.IsModified = false;
            Assert.True(mover.MoveTo(node, announce: false));
            Assert.Contains(mover.Id, node.ContentsSnapshot);
            Assert.False(node.IsModified);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // A wrong name still fails closed, and a right name+password still opens:
    // the always-run compare changes timing only, not outcomes.
    [Fact]
    public void Login_WrongName_Fails_RightCredentials_Pass()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("login", "correct-horse");
        Assert.False(acc.Login("someone-else", "correct-horse"));
        Assert.True(acc.Login("login", "correct-horse"));
        Assert.False(acc.Login("login", "wrong-horse"));
    }

    [Fact]
    public void Login_AlwaysRunsBothCompares()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var region = SourceScan.Region(src, "public bool Login(");
        Assert.Contains("bool nameOk", region);
        Assert.Contains("bool hashOk", region);
        Assert.DoesNotContain("&& CryptographicOperations.FixedTimeEquals", region);
    }

    // A failing rollback re-add is logged, never thrown in place of the
    // original DB failure.
    [Fact]
    public void AccountDelete_RollbackFailure_DoesNotMaskOriginal()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var region = SourceScan.Region(src, "DB failure: roll back so the account stays live");
        Assert.Contains("ObjectRegistry.AddObject(this);", region);
        Assert.Contains("Suppressed Account.DeleteImmediate rollback", region);
    }

    // The null-looker filter returns a copy: mutating the result leaves the
    // caller's list alone.
    [Fact]
    public void FilterVisible_NullLooker_ReturnsCopy()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var a = GameObject.Create("vara");
            var b = GameObject.Create("varb");
            var input = new List<GameObject> { a, b };
            var result = ContentUtils.FilterVisible(input, null);
            Assert.Equal(2, result.Count);
            result.Clear();
            Assert.Equal(2, input.Count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Grouping folds case like search does: "Sword"/"sword" stack together.
    [Fact]
    public void GroupByName_FoldsCase()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var a = GameObject.Create("Sword");
            var b = GameObject.Create("sword");
            Assert.Equal("Sword(2)", ContentUtils.GroupByName(new List<GameObject> { a, b }));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
