using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Unified subtype markers, kind dispatch, and lock-def loop: markers round-trip
// per entity kind, dispatch order is preserved, and double loads stay stable.
[Collection("Ported")]
public class DtoConverterUnificationTests
{
    private sealed class DtoProbeObject : GameObject { }

    static DtoConverterUnificationTests()
    {
        GameObject.RegisterPersistedSubtype(typeof(DtoProbeObject).FullName!, typeof(DtoProbeObject), () => new DtoProbeObject());
    }

    [Theory]
    [InlineData("object", typeof(GameObject))]
    [InlineData("account", typeof(Account))]
    [InlineData("channel", typeof(Channel))]
    [InlineData("script", typeof(Script))]
    public void FromDto_KindDispatch_RestoresEntityKind(string type, Type expected)
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(1001, "Kinded", type);
        var obj = GameObjectDtoConverter.FromDto(dto);
        Assert.IsType(expected, obj);
    }

    [Fact]
    public void FromDto_NodeKind_RestoresNodeAtLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var coord = new Coord("simplify", 1, 2, 3);
        var dto = GameObjectDto.Create(1002, "Nody", "node");
        dto.IsNode = true;
        dto.Location = new LocationRef.CoordLocation(coord);
        var obj = GameObjectDtoConverter.FromDto(dto);
        var node = Assert.IsType<Node>(obj);
        Assert.Equal(coord, node.Coord);
    }

    [Fact]
    public void FromDto_ScriptWinsOverNodeFlag_PreservesDispatchOrder()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(1003, "Both", "script");
        dto.IsNode = true;
        Assert.IsType<Script>(GameObjectDtoConverter.FromDto(dto));
    }

    [Fact]
    public void SubtypeMarker_RoundTrips_DoubleLoadKeepsMarker()
    {
        using var env = GlobalTestEnv.Enter();
        var probe = new DtoProbeObject();
        var dto = GameObjectDtoConverter.BuildDto(probe);
        Assert.True(dto.Extra.ContainsKey("__object_type"));
        var json = GameObjectDtoSerializer.ToJson(dto);
        var reloaded = GameObjectDtoSerializer.FromJson(json);
        var first = GameObjectDtoConverter.FromDto(reloaded);
        Assert.IsType<DtoProbeObject>(first);
        // The caller's dict was never stripped: the marker survives both loads.
        Assert.True(reloaded.Extra.ContainsKey("__object_type"));
        Assert.IsType<DtoProbeObject>(GameObjectDtoConverter.FromDto(reloaded));
    }

    [Fact]
    public void BuildDto_LockDefs_PreserveNameAndPolicy()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("locked");
        obj.AddLock("get", _ => true);
        var dto = GameObjectDtoConverter.BuildDto(obj);
        // Emit-all (pre-existing F004 shape): Create-installed defaults keep
        // their declarative policy names, the ad-hoc lambda is "custom".
        var byName = dto.Locks.ToDictionary(d => d.Name, d => d.Policy);
        Assert.Equal(3, byName.Count);
        Assert.Equal("not-self", byName["delete"]);
        Assert.Equal("puppet-owner", byName["puppet"]);
        Assert.Equal("custom", byName["get"]);
    }

    [Fact]
    public void FromDto_UnknownScriptMarker_LoadsBaseScriptKeepsMarker()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(1004, "Marked", "script");
        dto.Extra["__script_type"] = JsonDocument.Parse("\"Nope.NotReal\"").RootElement.Clone();
        var obj = GameObjectDtoConverter.FromDto(dto);
        Assert.IsType<Script>(obj);
        Assert.True(dto.Extra.ContainsKey("__script_type"));
    }
}
