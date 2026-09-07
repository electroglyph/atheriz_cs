using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Save/load must preserve the full field set: ApplyDtoFields restores only a
// flag subset while Create sets CanHear/IsTickable/TickSeconds and the DTO
// drops Symbol/MoveVerb/TickSeconds/Quelled (GameObject.cs:550-566 vs
// :640-651; GameObjectDto.cs:22-31).
[Collection("Ported")]
public class PersistenceRoundtripTests
{
    private static GameObject RoundTrip(GameObject o)
    {
        string json = GameObjectDtoSerializer.ToJson(o.ToDto());
        return GameObject.FromDto(GameObjectDtoSerializer.FromJson(json));
    }

    [Fact]
    public void CanHear_SurvivesRoundtrip()
    {
        // Correct: a PC that can hear still can hear after save->load, so hear
        // broadcasts keep reaching it.
        var o = GameObject.Create("listener", isPc: true);
        Assert.True(o.CanHear);
        Assert.True(RoundTrip(o).CanHear);
    }

    [Fact]
    public void Tickable_SurvivesRoundtrip()
    {
        // Correct: tick wiring and rate survive, so ResolveRelations can
        // re-register the ticker after load.
        var o = GameObject.Create("ticker", isTickable: true, tickSeconds: 5.0);
        var back = RoundTrip(o);
        Assert.True(back.IsTickable);
        Assert.Equal(5.0, back.TickSeconds);
    }

    [Fact]
    public void Symbol_MoveVerb_Quelled_SurviveRoundtrip()
    {
        // Correct: display/combat fields are not reset to defaults by a save.
        var o = GameObject.Create("actor");
        o.Symbol = "@";
        o.MoveVerb = "swim";
        o.Quelled = true;
        var back = RoundTrip(o);
        Assert.Equal("@", back.Symbol);
        Assert.Equal("swim", back.MoveVerb);
        Assert.True(back.Quelled);
    }
}
