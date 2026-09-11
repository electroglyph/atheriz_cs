using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Guarded setters assign and dirty-mark only on real change; the excluded
// setters keep their unconditional dirty-marking (and LastMapTime its
// never-marking) exactly as before.
[Collection("Ported")]
public class GuardedSetterDirtyFlagTests
{
    private static GameObject Clean(string name = "guarded")
    {
        var o = GameObject.Create(name);
        o.IsModified = false;
        return o;
    }

    [Fact]
    public void Id_SameValue_DoesNotMarkModified()
    {
        var o = Clean();
        o.Id = o.Id;
        Assert.False(o.IsModified);
    }

    [Fact]
    public void Id_ChangedValue_MarksModified()
    {
        var o = Clean();
        o.Id = o.Id + 1000;
        Assert.True(o.IsModified);
    }

    [Fact]
    public void Name_SameValue_DoesNotMarkModified()
    {
        var o = Clean("same-name");
        o.Name = "same-name";
        Assert.False(o.IsModified);
    }

    [Fact]
    public void Name_ChangedValue_MarksModified()
    {
        var o = Clean("old-name");
        o.Name = "new-name";
        Assert.True(o.IsModified);
    }

    [Fact]
    public void Desc_SameValue_DoesNotMarkModified()
    {
        var o = Clean();
        o.Desc = "kept";
        o.IsModified = false;
        o.Desc = "kept";
        Assert.False(o.IsModified);
    }

    [Fact]
    public void Desc_ChangedValue_MarksModified()
    {
        var o = Clean();
        o.Desc = "changed";
        Assert.True(o.IsModified);
    }

    [Fact]
    public void Symbol_SameValue_DoesNotMarkModified()
    {
        var o = Clean();
        o.Symbol = o.Symbol;
        Assert.False(o.IsModified);
    }

    [Fact]
    public void Symbol_ChangedValue_MarksModified()
    {
        var o = Clean();
        o.Symbol = "#";
        Assert.True(o.IsModified);
    }

    [Fact]
    public void PrivilegeLevel_SameValue_DoesNotMarkModified()
    {
        var o = Clean();
        o.PrivilegeLevel = Privilege.Guest;
        Assert.False(o.IsModified);
    }

    [Fact]
    public void PrivilegeLevel_ChangedValue_MarksModified()
    {
        var o = Clean();
        o.PrivilegeLevel = Privilege.Admin;
        Assert.True(o.IsModified);
    }

    [Fact]
    public void Gender_SameValue_DoesNotMarkModified()
    {
        var o = Clean();
        o.Gender = o.Gender;
        Assert.False(o.IsModified);
    }

    [Fact]
    public void Gender_ChangedValue_MarksModified()
    {
        var o = Clean();
        o.Gender = "feminine";
        Assert.True(o.IsModified);
    }

    [Fact]
    public void MoveVerb_SameValue_StillMarksModified()
    {
        var o = Clean();
        o.MoveVerb = "walk";
        o.IsModified = false;
        o.MoveVerb = "walk";
        Assert.True(o.IsModified);
    }

    [Fact]
    public void Quelled_SameValue_StillMarksModified()
    {
        var o = Clean();
        o.Quelled = o.Quelled;
        Assert.True(o.IsModified);
    }

    [Fact]
    public void MapEnabled_SameValue_StillMarksModified()
    {
        var o = Clean();
        o.MapEnabled = o.MapEnabled;
        Assert.True(o.IsModified);
    }

    [Fact]
    public void IsMapable_SameValue_StillMarksModified()
    {
        var o = Clean();
        o.IsMapable = o.IsMapable;
        Assert.True(o.IsModified);
    }

    [Fact]
    public void TickSeconds_SameValue_StillMarksModified()
    {
        var o = Clean();
        o.TickSeconds = o.TickSeconds;
        Assert.True(o.IsModified);
    }

    [Fact]
    public void LastMapTime_Set_DoesNotMarkModified()
    {
        var o = Clean();
        o.LastMapTime = 123.0;
        Assert.False(o.IsModified);
    }
}
