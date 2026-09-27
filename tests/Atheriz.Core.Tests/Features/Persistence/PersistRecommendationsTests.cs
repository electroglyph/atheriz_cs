using System.Text.Json;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Persistence;

// Pins for the typed-extra API, facade registration, versioned markers,
// versioned markers + migrate hooks, assembly-suffix fallback loads,
// row-level poison skip, unified checkpoint, world export, unhidden save.
// Real engine paths throughout (registry/converter/handlers), no mocks.
[Collection("Ported")]
public sealed class PersistRecommendationsTests
{
    private sealed class ProbeObject : GameObject
    {
        public string? Note;
        public int MigratedFrom;
        public override int PersistedTypeVersion => 2;
        public override void SaveExtra(Dictionary<string, JsonElement> extra)
            => WriteExtra(extra, "note", Note ?? "");
        public override void LoadExtra(IReadOnlyDictionary<string, JsonElement> extra)
        {
            if (ReadExtra<string>(extra, "note", out var n)) Note = n;
        }
        public override void OnLoadMigrate(int savedVersion) => MigratedFrom = savedVersion;
    }

    private sealed class ProbeNode : Node
    {
        public int MigratedFrom;
        public ProbeNode(Coord c) : base(c) { }
        public override int PersistedTypeVersion => 3;
        public override void OnLoadMigrate(int savedVersion) => MigratedFrom = savedVersion;
    }

    [Fact]
    public void TypedExtra_RoundTrip_And_GettersDoNotDirty()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("typed");
        ObjectRegistry.AddObject(obj);
        obj.IsModified = false;
        Assert.False(obj.HasPersisted("hp"));
        Assert.Null(obj.GetPersisted<int?>("hp"));
        Assert.Equal(42, obj.GetPersisted("hp", 42));
        Assert.False(obj.IsModified);
        obj.SetPersisted("hp", 100);
        Assert.True(obj.HasPersisted("hp"));
        Assert.Equal(100, obj.GetPersisted("hp", 0));
        Assert.True(obj.TryGetPersisted<int>("hp", out var v) && v == 100);
        Assert.True(obj.IsModified);
    }

    [Fact]
    public void ModifyPersisted_UpdatesThroughCallback_And_MarksDirty()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("mod");
        ObjectRegistry.AddObject(obj);
        obj.IsModified = false;
        int next = obj.ModifyPersisted<int>("counter", c => c + 1, seed: 10);
        Assert.Equal(11, next);
        Assert.Equal(11, obj.GetPersisted("counter", 0));
        Assert.True(obj.IsModified);
    }

    [Fact]
    public void Facade_Register_RoundTripsSubtype_WithVersionAndMigrate()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.Register(() => new ProbeObject(), "probe.obj");
        var probe = new ProbeObject { Note = "hello" };
        var dto = probe.ToDto();
        Assert.True(dto.Extra.TryGetValue("__object_type", out var marker)
            && marker.GetString() == "probe.obj");
        Assert.True(dto.Extra.TryGetValue("__object_type_version", out var ver)
            && ver.GetInt32() == 2);
        var reloaded = Assert.IsType<ProbeObject>(GameObject.FromDto(dto));
        Assert.Equal("hello", reloaded.Note);
        Assert.Equal(2, reloaded.MigratedFrom);
    }

    [Fact]
    public void SubtypeMarker_AssemblySuffix_FallsBackToRegisteredSubtype()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.Register(() => new ProbeObject(), "exact.probe");
        var dto = new ProbeObject().ToDto();
        dto.Extra["__object_type"] = JsonOptions.ToElement("exact.probe, SomeAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        var reloaded = GameObject.FromDto(dto);
        Assert.IsType<ProbeObject>(reloaded);
    }

    [Fact]
    public void SubtypeMarker_TrulyUnknownKey_LoadsBase()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.Register(() => new ProbeObject(), "exact.probe");
        var dto = new ProbeObject().ToDto();
        dto.Extra["__object_type"] = JsonOptions.ToElement("no.such.type");
        var reloaded = GameObject.FromDto(dto);
        Assert.Equal(typeof(GameObject), reloaded.GetType());
    }

    private sealed class ClobberObject : GameObject
    {
        public override void SaveExtra(Dictionary<string, JsonElement> extra)
            => WriteExtra(extra, "__object_type", "evil.key");
    }

    [Fact]
    public void SaveExtra_MarkerClobber_RestoredToRegisteredKey()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.Register(() => new ClobberObject(), "clobber.obj");
        var dto = new ClobberObject().ToDto();
        Assert.True(dto.Extra.TryGetValue("__object_type", out var marker)
            && marker.GetString() == "clobber.obj");
        Assert.IsType<ClobberObject>(GameObject.FromDto(dto));
    }

    [Fact]
    public void NodeSubtype_AssemblySuffix_FallsBackToRegisteredSubtype()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.RegisterNode(c => new ProbeNode(c), "nprobe.suffix");
        var area = new NodeAreaDto
        {
            Name = "nsfx",
            Grids = new()
            {
                [0] = new NodeGridDto
                {
                    Area = "nsfx",
                    Z = 0,
                    Nodes = new()
                    {
                        ["0,0"] = new NodeDto
                        {
                            Coord = new Coord("nsfx", 0, 0, 0),
                            Name = "Suffix Chamber",
                            ObjectType = "nprobe.suffix, SomeAssembly",
                            ObjectTypeVersion = 3,
                            Id = 434344,
                        },
                    },
                },
            },
        };
        var node = Assert.IsType<ProbeNode>(area.ToDomain().Grids[0].Nodes[(0, 0)]);
        Assert.Equal(3, node.MigratedFrom);
    }

    [Fact]
    public void WorldExport_PreservesIsModified()
    {
        using var env = GlobalTestEnv.Enter();
        var clean = GameObject.Create("export-clean");
        ObjectRegistry.AddObject(clean);
        clean.IsModified = false;
        var dirty = GameObject.Create("export-dirty");
        ObjectRegistry.AddObject(dirty);
        dirty.IsModified = true;
        string path = Path.Combine(env.TempPath, "world-export-flags.json");
        WorldExport.ExportToFile(path);
        Assert.False(clean.IsModified);
        Assert.True(dirty.IsModified);
    }

    [Fact]
    public void WorldImport_RejectsDuplicate_Negative_And_UnknownType()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("exported-bad");
        ObjectRegistry.AddObject(obj);
        string path = Path.Combine(env.TempPath, "world-export-bad.json");
        WorldExport.ExportToFile(path);
        var dtos = WorldExport.ImportFromFile(path);
        Assert.NotEmpty(dtos);
        var dup = new List<GameObjectDto>(dtos) { dtos[0] };
        string dupPath = Path.Combine(env.TempPath, "world-export-dup.json");
        File.WriteAllText(dupPath, System.Text.Json.JsonSerializer.Serialize(dup, JsonOptions.Default));
        Assert.Throws<InvalidDataException>(() => WorldExport.ImportFromFile(dupPath));
        var neg = new List<GameObjectDto>(dtos) { new GameObjectDto { Id = -5, Type = "object", Name = "neg" } };
        string negPath = Path.Combine(env.TempPath, "world-export-neg.json");
        File.WriteAllText(negPath, System.Text.Json.JsonSerializer.Serialize(neg, JsonOptions.Default));
        Assert.Throws<InvalidDataException>(() => WorldExport.ImportFromFile(negPath));
        var unknown = new List<GameObjectDto>(dtos) { new GameObjectDto { Id = 999998, Type = "spaceship", Name = "weird" } };
        string unknownPath = Path.Combine(env.TempPath, "world-export-unknown.json");
        File.WriteAllText(unknownPath, System.Text.Json.JsonSerializer.Serialize(unknown, JsonOptions.Default));
        Assert.Throws<InvalidDataException>(() => WorldExport.ImportFromFile(unknownPath));
    }

    [Fact]
    public void NodeSubtype_RoundTrip_CarriesVersionToMigrateHook()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.RegisterNode(c => new ProbeNode(c), "nprobe");
        var area = new NodeAreaDto
        {
            Name = "nver",
            Grids = new()
            {
                [0] = new NodeGridDto
                {
                    Area = "nver",
                    Z = 0,
                    Nodes = new()
                    {
                        ["0,0"] = new NodeDto
                        {
                            Coord = new Coord("nver", 0, 0, 0),
                            Name = "Ver Chamber",
                            ObjectType = "nprobe",
                            ObjectTypeVersion = 3,
                            Id = 434343,
                        },
                    },
                },
            },
        };
        var node = Assert.IsType<ProbeNode>(area.ToDomain().Grids[0].Nodes[(0, 0)]);
        Assert.Equal(3, node.MigratedFrom);
    }

    [Fact]
    public void PoisonRow_Skipped_HealthyRowsStillCommit()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        var healthy = GameObject.Create("healthy");
        var poison = GameObject.Create("Poison");
        ObjectRegistry.AddObject(healthy);
        ObjectRegistry.AddObject(poison);
        GameObjectDtoSerializer.ToJsonHook = dto =>
            dto.Name == "Poison" ? throw new InvalidOperationException("poison-boom") : System.Text.Json.JsonSerializer.Serialize(dto, JsonOptions.Default);
        try
        {
            var ex = Record.Exception(() => ObjectRegistry.SaveObjects(db, force: true));
            Assert.Null(ex);
        }
        finally { GameObjectDtoSerializer.ToJsonHook = null; }
        db.ChangeTracker.Clear();
        Assert.NotNull(db.Objects.Find(healthy.Id));
        Assert.Null(db.Objects.Find(poison.Id));
        Assert.False(healthy.IsModified);
        Assert.True(poison.IsModified);
    }

    [Fact]
    public void CheckpointWriter_CommitsAllSections_Atomically()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "cptest");
        Directory.CreateDirectory(dir);
        var settings = new AtherizSettings { SavePath = dir };
        var map = new MapHandler(settings, autoLoad: false);
        var nodes = new NodeHandler(autoLoad: false);
        var time = new GameTime(settings, autoLoad: false);
        var obj = GameObject.Create("checkpointed");
        ObjectRegistry.AddObject(obj);
        var failures = CheckpointWriter.WriteCheckpoint(settings, "test", map, nodes, time, includeTime: true);
        Assert.Empty(failures);
        // The test-device env override wins over settings.SavePath: verify
        // wherever the checkpoint actually went.
        string actual = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? dir;
        Assert.False(CheckpointJournal.IsDirty(actual));
        using var db = AtherizDbContextFactory.CreateForTests(actual);
        Assert.NotNull(db.Objects.Find(obj.Id));
    }

    [Fact]
    public void WorldExport_RoundTrips_And_RejectsGarbage()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("exported");
        obj.SetPersisted("level", 7);
        ObjectRegistry.AddObject(obj);
        string path = Path.Combine(env.TempPath, "world-export.json");
        int count = WorldExport.ExportToFile(path);
        Assert.True(count >= 1);
        var dtos = WorldExport.ImportFromFile(path);
        var match = Assert.Single(dtos, d => d.Id == obj.Id);
        Assert.Equal("exported", match.Name);
        var reloaded = GameObject.FromDto(match);
        Assert.Equal(7, reloaded.GetPersisted("level", 0));
        File.WriteAllText(path, "not json");
        Assert.Throws<InvalidDataException>(() => WorldExport.ImportFromFile(path));
    }

    [Fact]
    public void Account_Hooks_RoundTrip_PasswordAndCharacters()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("hookacc", "pw123456", saltOverride: "testsalt");
        var ch = GameObject.Create("hookchar", isPc: true);
        ObjectRegistry.AddObject(ch);
        acc.AddCharacter(ch);
        var dto = acc.ToDto();
        Assert.Equal("account", dto.Type);
        Assert.True(dto.Extra.ContainsKey("password"));
        var reloaded = Account.FromDto(dto);
        Assert.True(reloaded.CheckPassword("pw123456", saltOverride: "testsalt"));
        Assert.Contains(ch.Id, reloaded.Characters);
        Assert.False(reloaded.LoggedIn);
    }

    [Fact]
    public void SaveCommand_IsUnhidden()
    {
        Assert.False(new Atheriz.Core.Commands.LoggedIn.SaveCommand().Hide);
    }

    [Fact]
    public void GetterResult_IsDetachedCopy_MutatingItStaysClean()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("detached");
        ObjectRegistry.AddObject(obj);
        obj.SetPersisted("tags", new List<string> { "a" });
        obj.IsModified = false;
        var copy = obj.GetPersisted("tags", new List<string>());
        copy.Add("mutated");
        Assert.False(obj.IsModified);
        Assert.Equal(new List<string> { "a" }, obj.GetPersisted("tags", new List<string>()));
        Assert.True(obj.TryGetPersisted<List<string>>("tags", out var again));
        Assert.Equal(new List<string> { "a" }, again);
    }

    private sealed class TransientWidget : GameObject { }

    [Fact]
    public void TransientType_PersistFalse_SavesAsBaseWithoutMarker()
    {
        using var env = GlobalTestEnv.Enter();
        PersistedTypes.Register<TransientWidget>("transient.widget", persist: false);
        var dto = new TransientWidget().ToDto();
        Assert.False(dto.Extra.TryGetValue("__object_type", out _));
        Assert.Equal(typeof(GameObject), GameObject.FromDto(dto).GetType());
    }

    private sealed class DupFirst : GameObject { }
    private sealed class DupSecond : GameObject { }

    [Fact]
    public void DuplicateKey_ReRegistration_ReplacesMapping_Loudly()
    {
        using var env = GlobalTestEnv.Enter();
        const string key = "dup.replaced.key";
        GameObject.RegisterPersistedSubtype(key, typeof(DupFirst), () => new DupFirst());
        GameObject.RegisterPersistedSubtype(key, typeof(DupSecond), () => new DupSecond());
        var dto = new DupSecond().ToDto();
        Assert.True(dto.Extra.TryGetValue("__object_type", out var marker)
            && marker.GetString() == key);
        Assert.IsType<DupSecond>(GameObject.FromDto(dto));
        // The superseded type no longer owns the key: it saves as base.
        var stale = new DupFirst().ToDto();
        Assert.False(stale.Extra.TryGetValue("__object_type", out var staleMarker)
            && staleMarker.GetString() == key);
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "Converters", "GameObjectDtoConverter.cs");
        Assert.Contains("re-registered from", src);
    }

    private sealed class AlwaysThrowMap : MapHandler
    {
        public AlwaysThrowMap() : base(autoLoad: false) { }
        public override void Save(AtherizDbContext db, bool force = false)
            => throw new InvalidOperationException("map-boom");
    }

    [Fact]
    public void CheckpointWriter_SectionFailure_RollsBackHealthyRows_And_StaysDirty()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "cprollback");
        Directory.CreateDirectory(dir);
        var settings = new AtherizSettings { SavePath = dir };
        var map = new AlwaysThrowMap();
        var nodes = new NodeHandler(autoLoad: false);
        var obj = GameObject.Create("rollback-me");
        ObjectRegistry.AddObject(obj);
        var failures = CheckpointWriter.WriteCheckpoint(settings, "test", map, nodes, includeTime: false);
        Assert.Contains(failures, f => f.Name == "map");
        string actual = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? dir;
        Assert.True(CheckpointJournal.IsDirty(actual));
        using var db = AtherizDbContextFactory.CreateForTests(actual);
        Assert.Null(db.Objects.Find(obj.Id));
    }

    [Fact]
    public void SaveCommand_Export_WritesFile_And_RoundTrips()
    {
        using var env = GlobalTestEnv.Enter();
        var go = GameObject.Create("saver", isPc: true);
        ObjectRegistry.AddObject(go);
        string path = Path.Combine(env.TempPath, "save-cmd-export.json");
        new SaveCommand().Run(go, "export " + path);
        Assert.Contains(go.PeekMessages(), m => m.Contains("Exported"));
        Assert.True(File.Exists(path));
        var dtos = WorldExport.ImportFromFile(path);
        Assert.Contains(dtos, d => d.Id == go.Id);
        go.ClearMessages();
        new SaveCommand().Run(go, "export");
        Assert.Contains(go.PeekMessages(), m => m.Contains("Usage: save export"));
    }
}
