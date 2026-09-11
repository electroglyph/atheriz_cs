// Pins for the WriteSubtypeMarker choke point: markers serialize the registered
// name through the shared JsonOptions.ToElement path (byte-identical string
// element) and still round-trip the subtype through JSON for both marker kinds.
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class SubtypeMarkerToElementTests
{
    private sealed class MarkerProbeObject : GameObject { }

    private sealed class MarkerProbeScript : Script { }

    static SubtypeMarkerToElementTests()
    {
        GameObject.RegisterPersistedSubtype(
            typeof(MarkerProbeObject).FullName!, typeof(MarkerProbeObject), () => new MarkerProbeObject());
        GameObject.RegisterPersistedSubtype(
            typeof(MarkerProbeScript).FullName!, typeof(MarkerProbeScript), () => new MarkerProbeScript());
    }

    [Fact]
    public void BuildDto_RegisteredObjectSubtype_WritesRegisteredNameElement()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDtoConverter.BuildDto(new MarkerProbeObject());
        Assert.True(dto.Extra.TryGetValue("__object_type", out var marker));
        Assert.Equal(typeof(MarkerProbeObject).FullName, marker.GetString());
    }

    [Fact]
    public void BuildDto_RegisteredObjectSubtype_RoundTripsSubtypeThroughJson()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDtoConverter.BuildDto(new MarkerProbeObject());
        var reloaded = GameObjectDtoSerializer.FromJson(GameObjectDtoSerializer.ToJson(dto));
        Assert.IsType<MarkerProbeObject>(GameObjectDtoConverter.FromDto(reloaded));
        Assert.True(reloaded.Extra.ContainsKey("__object_type"));
    }

    [Fact]
    public void BuildDto_RegisteredScriptSubtype_RoundTripsSubtypeThroughJson()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDtoConverter.BuildDto(new MarkerProbeScript());
        Assert.True(dto.Extra.TryGetValue("__script_type", out var marker));
        Assert.Equal(typeof(MarkerProbeScript).FullName, marker.GetString());
        var reloaded = GameObjectDtoSerializer.FromJson(GameObjectDtoSerializer.ToJson(dto));
        Assert.IsType<MarkerProbeScript>(GameObjectDtoConverter.FromDto(reloaded));
    }
}
