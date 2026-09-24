using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Atheriz.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from ChannelAccountDtoRoundTripTests.cs
// DTO persistence shape: channel history triples, account extras (loggedIn
// persisted as false), and the shared single-id fetch outcome.
[Collection("Ported")]
public class ChannelAccountDtoRoundTripTests
{
    [Fact]
    public void Channel_ToDto_KeepsHistoryTriples()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Id = 4242;
            ch.Name = "chan";
            var sender = GameObject.Create("Bob");
            ch.Msg("hello", sender);

            var dto = ch.ToDto();

            Assert.Equal("channel", dto.Type);
            var history = dto.Extra["history"];
            Assert.Equal(JsonValueKind.Array, history.ValueKind);
            Assert.Equal(1, history.GetArrayLength());
            var triple = history[0];
            Assert.Equal(3, triple.GetArrayLength());
            Assert.Equal("Bob", triple[1].GetString());
            Assert.Equal("hello", triple[2].GetString());

            var op = ch.GetSaveOperation();
            Assert.Equal(4242, op.Id);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Account_ToDto_PersistsLoggedInFalse_AndRoundTrips()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("dto_user", "pw", saltOverride: "salt");
            var dto = acc.ToDto();

            Assert.Equal("account", dto.Type);
            Assert.Equal(JsonValueKind.False, dto.Extra["loggedIn"].ValueKind);

            var back = Account.FromDto(dto);
            Assert.Equal(acc.PasswordHash, back.PasswordHash);
            Assert.False(back.LoggedIn);

            var op = acc.GetSaveOperation();
            Assert.Equal(acc.Id, op.Id);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetSingle_MatchesGetFirstOrDefault()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("one");
            ObjectRegistry.AddObject(obj);

            Assert.Same(obj, ObjectRegistry.GetSingle(obj.Id));
            Assert.Equal(ObjectRegistry.Get(obj.Id).FirstOrDefault(), ObjectRegistry.GetSingle(obj.Id));
            Assert.Null(ObjectRegistry.GetSingle(-987654));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from CoordLocationFactoryTests.cs
// Single construction point for coord-backed locations: the factory builds
// the identical record (same values, same coordinate order, same JSON) as
// the inline constructor it replaces at every call site.
[Collection("Ported")]
public sealed class CoordLocationFactoryTests
{
    [Fact]
    public void FromCoord_MatchesDirectConstruction_ValueAndOrder()
    {
        var coord = new Coord("limbo", 1, 2, 3);
        Assert.Equal(new LocationRef.CoordLocation(coord), LocationRef.FromCoord(coord));
        var viaFactory = LocationRef.FromCoord(coord);
        Assert.Equal("limbo", viaFactory.Coord.Area);
        Assert.Equal(1, viaFactory.Coord.X);
        Assert.Equal(2, viaFactory.Coord.Y);
        Assert.Equal(3, viaFactory.Coord.Z);
    }

    [Fact]
    public void FromCoord_SerializesIdentically()
    {
        var coord = new Coord("limbo", 4, 4, 4);
        Assert.Equal(
            JsonSerializer.Serialize(new LocationRef.CoordLocation(coord)),
            JsonSerializer.Serialize(LocationRef.FromCoord(coord)));
    }

    [Fact]
    public void BuildDto_ForNode_UsesFactoryValue()
    {
        using var env = GlobalTestEnv.Enter();
        var coord = new Coord("factorydto", 5, 6, 7);
        var node = new Node(coord);
        ObjectRegistry.AddObject(node);
        var dto = GameObjectDtoConverter.BuildDto(node);
        Assert.Equal(LocationRef.FromCoord(coord), dto.Location);
    }

    [Fact]
    public void NodeAddObject_SetsFactoryLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea("factoryadd");
        var grid = new NodeGrid("factoryadd", 0);
        var room = new Node(new Coord("factoryadd", 0, 0, 0));
        grid.AddNode(room);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(room);
        var obj = GameObject.Create("factoryobj");
        ObjectRegistry.AddObject(obj);
        room.AddObject(obj);
        Assert.Equal(LocationRef.FromCoord(room.Coord), obj.Location);
    }

    [Fact]
    public void NodeAddObjects_SetsFactoryLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea("factoryadds");
        var grid = new NodeGrid("factoryadds", 0);
        var room = new Node(new Coord("factoryadds", 0, 0, 0));
        grid.AddNode(room);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(room);
        var obj = GameObject.Create("factoryobjs");
        ObjectRegistry.AddObject(obj);
        room.AddObjects([obj]);
        Assert.Equal(LocationRef.FromCoord(room.Coord), obj.Location);
    }
}

// Merged from DtoConverterUnificationTests.cs
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

    // Load adopts stored ids without drawing generator ids: the watermark
    // advances only at the registry swap (ObjectRegistry.LoadObjects), so a
    // bare FromDto leaves it untouched while the object keeps its row id.
    [Theory]
    [InlineData("object", 2001)]
    [InlineData("account", 2002)]
    [InlineData("channel", 2003)]
    [InlineData("script", 2004)]
    public void FromDto_AdoptsRowId_WithoutDrawingGeneratorIds(string type, int rowId)
    {
        using var env = GlobalTestEnv.Enter();
        int before = IdGenerator.GetId();
        var dto = GameObjectDto.Create(rowId, "Loaded", type);
        var obj = GameObjectDtoConverter.FromDto(dto);
        Assert.Equal(rowId, obj.Id);
        Assert.Equal(before, IdGenerator.GetId());
    }

    [Fact]
    public void FromDto_NodeAdoptsRowId_WithoutDrawingGeneratorIds()
    {
        using var env = GlobalTestEnv.Enter();
        int before = IdGenerator.GetId();
        var dto = GameObjectDto.Create(2005, "LoadedNode", "node");
        dto.IsNode = true;
        dto.Location = new LocationRef.CoordLocation(new Coord("wmark", 0, 0, 0));
        var node = Assert.IsType<Node>(GameObjectDtoConverter.FromDto(dto));
        Assert.Equal(2005, node.Id);
        Assert.Equal(before, IdGenerator.GetId());
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
        // their declarative policy names, the ad-hoc lambda is Custom.
        var byName = dto.Locks.ToDictionary(d => d.Name, d => d.Policies);
        Assert.Equal(3, byName.Count);
        Assert.Equal([LockPolicies.LockPolicy.NotSelf], byName["delete"]);
        Assert.Equal([LockPolicies.LockPolicy.PuppetOwner], byName["puppet"]);
        Assert.Equal([LockPolicies.LockPolicy.Custom], byName["get"]);
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

// Merged from EngineSubtypeRoundTripTests.cs
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

// Merged from LocationRefStreamingTests.cs
// Streaming Coord-location reads: order-independent like the DOM lookups,
// same branch order (null, number, Area-object, ObjectId-object, Null) and
// byte-identical JsonException messages on corrupt saves.
[Collection("Ported")]
public class LocationRefStreamingTests
{
    private static LocationRef? Read(string json)
        => JsonSerializer.Deserialize<LocationRef>(json);

    private static string Write(LocationRef loc)
        => JsonSerializer.Serialize(loc);

    [Fact]
    public void RoundTrip_CoordLocation()
    {
        var loc = new LocationRef.CoordLocation(new Coord("limbo", 4, 4, 4));
        Assert.Equal(loc, Read(Write(loc)));
    }

    [Fact]
    public void ReorderedKeys_StillParse()
    {
        Assert.Equal(
            new LocationRef.CoordLocation(new Coord("limbo", 1, 2, 3)),
            Read("""{"Z":3,"X":1,"Area":"limbo","Y":2}"""));
    }

    [Fact]
    public void NonNumericXYZ_ThrowsIdenticalMessage()
    {
        var ex = Assert.Throws<JsonException>(() => Read("""{"Area":"a","X":"1","Y":2,"Z":3}"""));
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void MissingXYZ_ThrowsIdenticalMessage()
    {
        var ex = Assert.Throws<JsonException>(() => Read("""{"Area":"a","X":1}"""));
        Assert.Equal("Coord location requires numeric X/Y/Z.", ex.Message);
    }

    [Fact]
    public void NonNumericObjectId_ThrowsIdenticalMessage()
    {
        var ex = Assert.Throws<JsonException>(() => Read("""{"ObjectId":"42"}"""));
        Assert.Equal("ObjectId location requires a numeric id.", ex.Message);
    }

    [Fact]
    public void Shapes_NullNumberEmpty_And_WrongKinds()
    {
        // Root JSON null bypasses the converter (HandleNull is false, as
        // before the streaming rewrite), so it deserializes to C# null —
        // not NullLocation.Instance. Only an explicit {} yields NullLocation.
        Assert.Null(Read("null"));
        Assert.Equal(new LocationRef.ObjectLocation(42), Read("42"));
        Assert.Equal(LocationRef.NullLocation.Instance, Read("{}"));
        Assert.Throws<InvalidOperationException>(() => Read("\"s\""));
        Assert.Throws<InvalidOperationException>(() => Read("[1]"));
        Assert.Throws<InvalidOperationException>(() => Read("true"));
    }

    [Fact]
    public void Truncated_FailsClosedAsJsonException()
    {
        Assert.Throws<JsonException>(() => Read("{\"Area\":\"a"));
    }
}

// Merged from TableLoaderBufferTests.cs
// Shared deserialize-to-buffer core: every loader skips corrupt rows loudly
// while keeping its own add discipline and log name.
[Collection("Ported")]
public class TableLoaderBufferTests
{
    private static void SeedOneGoodOneBad(AtherizDbContext db)
    {
        db.Objects.Add(new ObjectRow { Id = 1, Type = "object", Version = 1, Data = GameObjectDtoSerializer.ToJson(GameObjectDto.Create(1, "Good")) });
        db.Objects.Add(new ObjectRow { Id = 2, Type = "object", Version = 1, Data = "not json at all" });
        db.SaveChanges();
    }

    [Fact]
    public void LoadList_SkipsCorrupt_AndWarnsWithOwnName()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        SeedOneGoodOneBad(db);
        using var cap = new CaptureAtherizLog();
        int added = 0;
        JsonTableLoader.LoadList<ObjectRow, GameObjectDto>(db.Objects, GameObjectDtoSerializer.FromJson, (dto, row) => added++);
        Assert.Equal(1, added);
        var log = cap.Read();
        Assert.Contains("LoadList<ObjectRow>", log);
        Assert.Contains("skipped 1 corrupt", log);
    }

    [Fact]
    public void LoadInto_SkipsCorrupt_AndWarnsWithOwnName()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        SeedOneGoodOneBad(db);
        using var cap = new CaptureAtherizLog();
        using var gate = new ReaderWriterLockSlim();
        int added = 0;
        try
        {
            JsonTableLoader.LoadInto<ObjectRow, GameObjectDto>(db.Objects, gate, GameObjectDtoSerializer.FromJson, (dto, row) => added++);
        }
        finally { gate.Dispose(); }
        Assert.Equal(1, added);
        var log = cap.Read();
        Assert.Contains("LoadInto<ObjectRow>", log);
        Assert.Contains("skipped 1 corrupt", log);
    }
}

// Merged from WalPragmaSharedTests.cs
// Shared PRAGMA core: sync + async creation paths apply identical pragmas,
// with WAL journal mode active on the resulting database.
[Collection("Ported")]
public class WalPragmaSharedTests
{
    private static string ReadJournalMode(AtherizDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State == System.Data.ConnectionState.Closed) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    [Fact]
    public void EnsureCreated_AppliesWalPragmas()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = new AtherizDbContext(env.TempPath);
        db.EnsureCreated();
        Assert.True(File.Exists(Path.Combine(env.TempPath, "database.sqlite3")));
        Assert.Equal("wal", ReadJournalMode(db), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureCreatedAsync_AppliesWalPragmas()
    {
        using var env = GlobalTestEnv.Enter();
        await using var db = new AtherizDbContext(env.TempPath);
        await db.EnsureCreatedAsync();
        Assert.True(File.Exists(Path.Combine(env.TempPath, "database.sqlite3")));
        Assert.Equal("wal", ReadJournalMode(db), StringComparer.OrdinalIgnoreCase);
    }
}

// Merged from TransitionsSingleCommandTests.cs
// Single prepared INSERT rebound per row: every migrated row keeps its own
// source, destination, and payload, and undecodable rows are still dropped.
[Collection("Ported")]
public class TransitionsSingleCommandTests
{
    [Fact]
    public void MigrateTransitionsTable_RebindsEveryRow_DropsUndecodable()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizDbContextFactory.DoSetup(dir);
            var first = JsonSerializer.Serialize(
                new Transition(new Coord("A", 0, 1, 0), new Coord("B", 0, 2, 0), "door"),
                JsonOptions.Default);
            var second = JsonSerializer.Serialize(
                new Transition(new Coord("C", 3, 4, 5), new Coord("D", 6, 7, 8), "gate"),
                JsonOptions.Default);
            var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "database.sqlite3") }.ToString();
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE \"transitions\"; CREATE TABLE \"transitions\" (\"ToArea\" TEXT NOT NULL, \"ToX\" INTEGER NOT NULL, \"ToY\" INTEGER NOT NULL, \"ToZ\" INTEGER NOT NULL, \"Data\" TEXT, PRIMARY KEY (\"ToArea\",\"ToX\",\"ToY\",\"ToZ\"))";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "INSERT INTO \"transitions\" VALUES ('B',0,2,0,@d)";
                cmd.Parameters.AddWithValue("@d", first);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "INSERT INTO \"transitions\" VALUES ('D',6,7,8,@d)";
                cmd.Parameters.AddWithValue("@d", second);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "INSERT INTO \"transitions\" VALUES ('Z',9,9,9,@d)";
                cmd.Parameters.AddWithValue("@d", "not json at all");
                cmd.ExecuteNonQuery();
            }
            using (var ctx = AtherizDbContextFactory.Create(dir))
            {
                AtherizDbContextFactory.MigrateTransitionsTable(ctx);
            }
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT \"FromArea\",\"FromX\",\"FromY\",\"FromZ\",\"ToArea\",\"ToX\",\"ToY\",\"ToZ\",\"Data\" FROM \"transitions\" ORDER BY \"FromX\"";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("A", reader.GetString(0));
                Assert.Equal(0, reader.GetInt32(1));
                Assert.Equal("B", reader.GetString(4));
                Assert.Equal(2, reader.GetInt32(6));
                Assert.Equal(first, reader.GetString(8));
                Assert.True(reader.Read());
                Assert.Equal("C", reader.GetString(0));
                Assert.Equal(3, reader.GetInt32(1));
                Assert.Equal(4, reader.GetInt32(2));
                Assert.Equal(5, reader.GetInt32(3));
                Assert.Equal("D", reader.GetString(4));
                Assert.Equal(6, reader.GetInt32(5));
                Assert.Equal(7, reader.GetInt32(6));
                Assert.Equal(8, reader.GetInt32(7));
                Assert.Equal(second, reader.GetString(8));
                Assert.False(reader.Read());
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
