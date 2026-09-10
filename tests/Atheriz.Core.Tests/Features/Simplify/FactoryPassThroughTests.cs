using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Collapsed pass-through wrappers: the ctor guard, null-means-default paths,
// and the optional upsert configuration keep identical runtime behavior.
[Collection("Ported")]
public class FactoryPassThroughTests
{
    [Fact]
    public void ClosedFactory_Create_ThrowsClosedWithoutReopening()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizDbContextFactory.CloseDatabase();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => AtherizDbContextFactory.Create(env.TempPath));
            Assert.Contains("database is closed", ex.Message);
        }
        finally { AtherizDbContextFactory.ReopenDatabase(); }
    }

    [Fact]
    public void Journal_ParameterlessOverloads_UseDefaultPath()
    {
        using var env = GlobalTestEnv.Enter();
        CheckpointJournal.MarkDirty();
        Assert.True(CheckpointJournal.IsDirty());
        CheckpointJournal.MarkClean();
        Assert.False(CheckpointJournal.IsDirty());
    }

    [Fact]
    public void UpsertJson_WithoutConfigure_InsertsAndUpdates()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        DbTransactionHelper.UpsertJson(db.GameTime,
            () => db.GameTime.Find(0), () => new GameTimeRow { Id = 0 }, "{}");
        db.SaveChanges();
        Assert.Equal("{}", db.GameTime.Find(0)!.Data);
        DbTransactionHelper.UpsertJson(db.GameTime,
            () => db.GameTime.Find(0), () => new GameTimeRow { Id = 0 }, """{"t":1}""");
        db.SaveChanges();
        db.ChangeTracker.Clear();
        Assert.Equal("""{"t":1}""", db.GameTime.Find(0)!.Data);
    }

    [Fact]
    public void UpsertJson_OptionalConfigure_SetsDiscriminator()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        DbTransactionHelper.UpsertJson(db.Objects,
            () => db.Objects.Find(9), () => new ObjectRow { Id = 9, Type = "object", Version = 1 },
            "{}", row => row.Type = "script");
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var row = db.Objects.Find(9);
        Assert.NotNull(row);
        Assert.Equal("{}", row!.Data);
        Assert.Equal("script", row.Type);
    }
}
