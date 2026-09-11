using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Persistence;

// The single BuildDto path must stay reachable through the public ToDto and
// the converter's internal entry point after the dead private twin is removed.
[Collection("Ported")]
public class DtoRoundTripTests
{
    private static GameObject RoundTrip(GameObject o)
    {
        string json = GameObjectDtoSerializer.ToJson(o.ToDto());
        return GameObject.FromDto(GameObjectDtoSerializer.FromJson(json));
    }

    [Fact]
    public void ToDto_JsonRoundTrip_PreservesIdentityFields()
    {
        var o = GameObject.Create("roundtripper", "a description");
        o.Symbol = "@";
        var back = RoundTrip(o);
        Assert.Equal(o.Id, back.Id);
        Assert.Equal("roundtripper", back.Name);
        Assert.Equal("a description", back.Desc);
        Assert.Equal("@", back.Symbol);
    }

    [Fact]
    public void ToDto_JsonRoundTrip_PreservesTagsAndPrivilege()
    {
        var o = GameObject.Create("tagged");
        o.AddTag("quest");
        o.PrivilegeLevel = Privilege.Builder;
        var back = RoundTrip(o);
        Assert.True(back.HasTag("quest"));
        Assert.Equal(Privilege.Builder, back.PrivilegeLevel);
    }

    [Fact]
    public void GetSaveOps_ConverterPath_ReferencesSameId()
    {
        // Exercises the converter's internal DTO entry point (the kept twin).
        var o = GameObject.Create("saved");
        var (sql, ps) = o.GetSaveOps();
        Assert.Contains("objects", sql);
        Assert.Contains(o.Id, ps.OfType<int>());
    }
}
