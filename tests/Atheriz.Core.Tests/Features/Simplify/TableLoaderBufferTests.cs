using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

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
