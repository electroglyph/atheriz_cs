// Regression pins: path guards, legend loading, map-edit keys.
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class CorrectnessBatchCTests
{
    // DenyRoot refuses the root and its direct children (/save).
    [Fact]
    public void DenyRoot_TopLevelLeaf_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => PathGuards.DenyRoot("/"));
        Assert.Throws<InvalidOperationException>(() => PathGuards.DenyRoot("/save"));
        var nested = Path.Combine(Path.GetTempPath(), "pin-" + Guid.NewGuid().ToString("N"), "game");
        PathGuards.DenyRoot(nested); // must not throw (also covers non-existent paths)
    }

    // Markers are always required: inside a game folder a markerless
    // target (foreign or contained) refuses; only markers pass.
    [Fact]
    public void GuardWipePath_InGameFolder_StillRejectsMarkerlessPaths()
    {
        var game = Path.Combine(Path.GetTempPath(), "pin-game-" + Guid.NewGuid().ToString("N"));
        var foreign = Path.Combine(Path.GetTempPath(), "pin-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(game, "settings.py"), "");
        File.WriteAllText(Path.Combine(game, "__init__.py"), "");
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(game);
            Assert.True(GameUtils.IsInGameFolder());
            Assert.Throws<InvalidOperationException>(() => PathGuards.GuardWipePath(foreign));
            // A contained but markerless target refuses too — no override.
            var inner = Path.Combine(game, "save");
            Directory.CreateDirectory(inner);
            Assert.Throws<InvalidOperationException>(() => PathGuards.GuardWipePath(inner));
            // Markers restore the pass.
            File.WriteAllText(Path.Combine(inner, "database.sqlite3"), "x");
            PathGuards.GuardWipePath(inner);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { Directory.Delete(game, true); } catch { }
            try { Directory.Delete(foreign, true); } catch { }
        }
    }

    // The live loader rejects malformed coords instead of truncating.
    [Fact]
    public void LegendEntryDto_ToDomain_MalformedCoord_Throws()
    {
        Assert.Throws<JsonException>(() => new MapInfo.LegendEntryDto { Symbol = "s", Coord = [1, 2, 3] }.ToDomain());
        Assert.Throws<JsonException>(() => new MapInfo.LegendEntryDto { Symbol = "s", Coord = [5] }.ToDomain());
    }

    [Fact]
    public void LegendEntryDto_ToDomain_WellFormed_RoundTrips()
    {
        var pair = new MapInfo.LegendEntryDto { Symbol = "s", Desc = "d", Coord = [1, 2] }.ToDomain();
        Assert.Equal((1, 2), pair.Coord);
        var bare = new MapInfo.LegendEntryDto { Symbol = "s", Coord = null }.ToDomain();
        Assert.Null(bare.Coord);
        var back = MapInfo.LegendEntryDto.FromDomain(pair);
        Assert.Equal([1, 2], back.Coord);
    }

    [Fact]
    public void MapInfoPersistDto_ToDomain_SkipsMalformedLegendEntry()
    {
        using var env = GlobalTestEnv.Enter();
        var mi = new MapInfo("pin-area");
        mi.LegendEntries.Add(new LegendEntry("ok", "fine", (1, 2)));
        var dto = MapInfo.MapInfoPersistDto.FromDomain(mi);
        dto.LegendEntries.Add(new MapInfo.LegendEntryDto { Symbol = "bad", Coord = [1, 2, 3] });
        dto.LegendEntries.Add(new MapInfo.LegendEntryDto { Symbol = "short", Coord = [5] });
        var loaded = dto.ToDomain(new AtherizSettings());
        Assert.Single(loaded.LegendEntries);
        Assert.Equal("ok", loaded.LegendEntries[0].Symbol);
    }

    [Fact]
    public void LegendEntry_FromPayload_MalformedCoord_Throws()
    {
        static Dictionary<string, JsonElement> Dict(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        Assert.Throws<JsonException>(() => LegendEntry.FromPayload(Dict("""{"symbol":"s","coord":[1,2,3]}""")));
        Assert.Throws<JsonException>(() => LegendEntry.FromPayload(Dict("""{"symbol":"s","coord":[5]}""")));
        Assert.Equal((1, 2), LegendEntry.FromPayload(Dict("""{"symbol":"s","coord":[1,2]}""")).Coord);
    }

    // After a cap shrink, consuming a still-valid key resolves first
    // (no evict-before-resolve unknown_key).
    [Fact]
    public void MapEdit_Consume_AfterCapShrink_ResolvesValidKey()
    {
        using var env = GlobalTestEnv.Enter();
        MapEdit.Reset();
        var orig = AtherizSettings.Global.MapeditMaxChains;
        try
        {
            AtherizSettings.Global.MapeditMaxChains = 3;
            var k1 = MapEdit.Grant("1.1.1.1", "A", 0);
            MapEdit.Grant("1.1.1.1", "A", 0);
            MapEdit.Grant("1.1.1.1", "A", 0);
            AtherizSettings.Global.MapeditMaxChains = 2;
            var res = MapEdit.Consume(k1, "1.1.1.1", 0);
            Assert.Equal(MapEditStatus.Processed, res.Status);
            Assert.NotNull(res.NewKey);
            // The old key stays valid as a previous-key pointing at the rotation.
            Assert.Equal(res.NewKey, MapEdit.GetChain(k1)?.Key);
            Assert.NotNull(MapEdit.GetChain(res.NewKey!));
            var miss = MapEdit.Consume("no-such-key", "1.1.1.1", 0);
            Assert.Equal(MapEditStatus.Reject, miss.Status);
            Assert.Equal("unknown_key", miss.Reason);
        }
        finally
        {
            AtherizSettings.Global.MapeditMaxChains = orig;
            MapEdit.Reset();
        }
    }
}
