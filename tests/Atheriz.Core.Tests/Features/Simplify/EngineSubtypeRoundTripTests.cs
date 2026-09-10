using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Engine-owned world subtypes must survive a save/load round-trip with their
// overrides intact: unregistered they save as their base kind (loud log) and
// reload without behavior (dead dashboard klaxon, frozen wanderers).
[Collection("Ported")]
public class EngineSubtypeRoundTripTests
{
    [Fact]
    public void AlarmObject_RoundTrip_PreservesSubtype()
    {
        using var env = GlobalTestEnv.Enter();
        InitialSetup.RegisterPersistedSubtypes();
        var alarm = new InitialSetup.AlarmObject();
        var dto = GameObjectDtoConverter.BuildDto(alarm);
        Assert.True(dto.Extra.ContainsKey("__object_type"));
        Assert.IsType<InitialSetup.AlarmObject>(GameObjectDtoConverter.FromDto(dto));
    }

    [Fact]
    public void WandererNpc_RoundTrip_PreservesSubtypeAndFlags()
    {
        using var env = GlobalTestEnv.Enter();
        InitialSetup.RegisterPersistedSubtypes();
        var npc = new WanderCommand.WandererNpc();
        var dto = GameObjectDtoConverter.BuildDto(npc);
        Assert.True(dto.Extra.ContainsKey("__object_type"));
        var reloaded = Assert.IsType<WanderCommand.WandererNpc>(GameObjectDtoConverter.FromDto(dto));
        Assert.True(reloaded.IsNpc);
        Assert.True(reloaded.IsTickable);
        Assert.Equal(1.0, reloaded.TickSeconds);
    }

    [Fact]
    public void FollowScript_RoundTrip_PreservesSubtype()
    {
        using var env = GlobalTestEnv.Enter();
        InitialSetup.RegisterPersistedSubtypes();
        var script = new FollowScript();
        var dto = GameObjectDtoConverter.BuildDto(script);
        Assert.True(dto.Extra.ContainsKey("__script_type"));
        Assert.IsType<FollowScript>(GameObjectDtoConverter.FromDto(dto));
    }
}
