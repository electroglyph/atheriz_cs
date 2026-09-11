using Atheriz.Core;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// LoadList/LoadInto share one add phase with per-loader op names while each
// keeps its own lock discipline and warning; LoadAll keeps its corrupt-only
// tail.
[Collection("Ported")]
public class TableLoaderAddPhaseTests
{
    private static void SeedOneGoodOneBad(AtherizDbContext db)
    {
        db.Objects.Add(new ObjectRow { Id = 1, Type = "object", Version = 1, Data = GameObjectDtoSerializer.ToJson(GameObjectDto.Create(1, "Good")) });
        db.Objects.Add(new ObjectRow { Id = 2, Type = "object", Version = 1, Data = "not json at all" });
        db.SaveChanges();
    }

    [Fact]
    public void LoadList_AddFailure_CountsCorruptAndAddFailures()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        try
        {
            using var db = AtherizDbContextFactory.CreateForTests();
            SeedOneGoodOneBad(db);
            int calls = 0;
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                JsonTableLoader.LoadList<ObjectRow, GameObjectDto>(db.Objects, GameObjectDtoSerializer.FromJson,
                    (dto, row) => { calls++; throw new InvalidOperationException("nope"); });
                log = cap.Read();
            }
            Assert.Equal(1, calls);
            Assert.Contains("LoadList<ObjectRow>", log);
            Assert.Contains("skipped 1 corrupt, 1 add-failures", log);
            Assert.Contains("Suppressed JsonTableLoader.LoadList<ObjectRow> add: nope", log);
        }
        finally
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }

    [Fact]
    public void LoadInto_AddFailure_CountsCorruptAndAddFailuresAndReleasesLock()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        try
        {
            using var db = AtherizDbContextFactory.CreateForTests();
            SeedOneGoodOneBad(db);
            using var gate = new ReaderWriterLockSlim();
            int calls = 0;
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                JsonTableLoader.LoadInto<ObjectRow, GameObjectDto>(db.Objects, gate, GameObjectDtoSerializer.FromJson,
                    (dto, row) => { calls++; throw new InvalidOperationException("nope"); });
                log = cap.Read();
            }
            Assert.Equal(1, calls);
            Assert.False(gate.IsWriteLockHeld);
            Assert.Contains("LoadInto<ObjectRow>", log);
            Assert.Contains("skipped 1 corrupt, 1 add-failures", log);
            Assert.Contains("Suppressed JsonTableLoader.LoadInto<ObjectRow> add: nope", log);
        }
        finally
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }

    [Fact]
    public void LoadInto_AddCallback_ObservesCallerWriteLock()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        SeedOneGoodOneBad(db);
        using var gate = new ReaderWriterLockSlim();
        bool heldDuringAdd = false;
        JsonTableLoader.LoadInto<ObjectRow, GameObjectDto>(db.Objects, gate, GameObjectDtoSerializer.FromJson,
            (dto, row) => { heldDuringAdd = gate.IsWriteLockHeld; });
        Assert.True(heldDuringAdd);
        Assert.False(gate.IsWriteLockHeld);
    }

    [Fact]
    public void LoadAll_CorruptRow_WarnsWithoutAddFailureTail()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        SeedOneGoodOneBad(db);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            var list = JsonTableLoader.LoadAll<ObjectRow, GameObjectDto>(db.Objects, GameObjectDtoSerializer.FromJson);
            log = cap.Read();
            Assert.Single(list);
        }
        Assert.Contains("LoadAll<ObjectRow>", log);
        Assert.Contains("skipped 1 corrupt.", log);
        Assert.DoesNotContain("add-failures", log);
    }
}
