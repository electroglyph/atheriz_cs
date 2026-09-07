using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Tests;
using Microsoft.Data.Sqlite;

namespace Atheriz.Core.Tests.Features.Persistence;

// P1-14 persistence regression pins: gate pairing/timeout, transaction
// commit/rollback/retry, journal roundtrip, setup parity, DTO hardening,
// loader loud-skips.
[Collection("Ported")]
public class PersistenceRegressionTests
{
    [Fact]
    public void GateTransaction_CommitsAndReleasesGate()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        DbTransactionHelper.WithGateAndTransaction(db,
            d => d.GameTime.Add(new GameTimeRow { Id = 0, Data = "{}" }));
        Assert.False(DbWriteGate.IsHeld);
        Assert.NotNull(db.GameTime.Find(0));
    }

    [Fact]
    public void GateTransaction_RollsBack_InvokesOnRollback_ReleasesGate()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        bool rolledBack = false;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DbTransactionHelper.WithGateAndTransaction(db,
                d =>
                {
                    d.GameTime.Add(new GameTimeRow { Id = 0, Data = "{}" });
                    throw new InvalidOperationException("boom");
                },
                onRollback: () => rolledBack = true));
        Assert.Equal("boom", ex.Message);
        Assert.True(rolledBack);
        Assert.False(DbWriteGate.IsHeld);
        // Find consults the EF change tracker first; clear it to assert the DB row is gone.
        db.ChangeTracker.Clear();
        Assert.Null(db.GameTime.Find(0));
    }

    [Fact]
    public void GateTransaction_NonBusyError_RunsWorkExactlyOnce()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        int runs = 0;
        Assert.Throws<InvalidOperationException>(() =>
            DbTransactionHelper.WithGateAndTransaction(db,
                d => { runs++; throw new InvalidOperationException("nope"); }));
        Assert.Equal(1, runs);
        Assert.False(DbWriteGate.IsHeld);
    }

    [Fact]
    public void GateTransaction_BusyConflict_RetriesThenCommits()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        var busy = MakeBusyException();
        Assert.True(DbTransactionHelper.IsBusyConflict(new Exception("wrap", busy)));
        Assert.False(DbTransactionHelper.IsBusyConflict(new InvalidOperationException("plain")));
        int runs = 0;
        DbTransactionHelper.WithGateAndTransaction(db,
            d =>
            {
                runs++;
                if (runs == 1) throw new Exception("locked", busy);
                d.GameTime.Add(new GameTimeRow { Id = 0, Data = "{}" });
            });
        Assert.Equal(2, runs);
        Assert.NotNull(db.GameTime.Find(0));
        Assert.False(DbWriteGate.IsHeld);
    }

    // SqliteException ctor visibility differs by version; prefer the public
    // (string, int) ctor, else fall back to non-public reflection (tests may reflect; prod may not).
    private static Exception MakeBusyException()
    {
        var t = typeof(SqliteException);
        var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(int);
            });
        Assert.True(ctor != null, "No (string,int) SqliteException ctor found");
        return (Exception)ctor!.Invoke(["database is locked", 5]);
    }

    [Fact]
    public void GateTryEnter_TimesOutWhenHeld_SucceedsAfterRelease()
    {
        using var env = GlobalTestEnv.Enter();
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holder = Task.Run(() =>
        {
            DbWriteGate.Enter();
            try
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            finally { DbWriteGate.Exit(); }
        });
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            Assert.False(DbWriteGate.TryEnter(TimeSpan.FromMilliseconds(50)));
        }
        finally
        {
            release.Set();
            holder.Wait(TimeSpan.FromSeconds(10));
        }
        Assert.True(DbWriteGate.TryEnter(TimeSpan.FromSeconds(5)));
        DbWriteGate.Exit();
        Assert.False(DbWriteGate.IsHeld);
    }

    [Fact]
    public async Task EnsureCreatedAsync_ReleasesGateAcrossThreads()
    {
        using var env = GlobalTestEnv.Enter();
        await using var db = new AtherizDbContext(env.TempPath);
        for (int i = 0; i < 20; i++)
            await db.EnsureCreatedAsync();
        Assert.False(DbWriteGate.IsHeld);
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
        Assert.True(File.Exists(Path.Combine(env.TempPath, "database.sqlite3")));
    }

    [Fact]
    public async Task SetupPaths_Agree_NoSeedRow()
    {
        using var env = GlobalTestEnv.Enter();
        var syncDir = Path.Combine(Path.GetTempPath(), "atheriz-test-" + Guid.NewGuid().ToString("N"));
        var asyncDir = Path.Combine(Path.GetTempPath(), "atheriz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(syncDir);
        Directory.CreateDirectory(asyncDir);
        try
        {
            AtherizDbContextFactory.DoSetup(syncDir);
            await AtherizDbContextFactory.DoSetupAsync(asyncDir);
            await using (var s = new AtherizDbContext(syncDir))
                Assert.Null(s.GameTime.Find(0));
            await using (var a = new AtherizDbContext(asyncDir))
                Assert.Null(a.GameTime.Find(0));
        }
        finally
        {
            try { Directory.Delete(syncDir, true); } catch { }
            try { Directory.Delete(asyncDir, true); } catch { }
        }
    }

    [Fact]
    public void Journal_DirtyClean_Roundtrip()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.False(CheckpointJournal.IsDirty(env.TempPath));
        CheckpointJournal.MarkDirty(env.TempPath);
        Assert.True(CheckpointJournal.IsDirty(env.TempPath));
        CheckpointJournal.MarkClean(env.TempPath);
        Assert.False(CheckpointJournal.IsDirty(env.TempPath));
    }

    [Fact]
    public void Migrate_BackfillsNullCollectionsAndLocations()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDtoSerializer.FromJson(
            """{"id":5,"name":"Nully","tags":null,"aliases":null,"contents":null,"extra":null,"locks":null,"channels":null,"location":null,"home":null}""");
        var m = GameObjectDtoSerializer.Migrate(dto);
        Assert.NotNull(m.Tags);
        Assert.NotNull(m.Aliases);
        Assert.NotNull(m.Contents);
        Assert.NotNull(m.Extra);
        Assert.NotNull(m.Locks);
        Assert.NotNull(m.Channels);
        Assert.NotNull(m.Location);
        Assert.NotNull(m.Home);
    }

    [Fact]
    public void FromDto_DoesNotMutateCallerExtra()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = GameObjectDto.Create(7, "Marked");
        dto.Extra["__object_type"] = JsonDocument.Parse("\"Nope.NotReal\"").RootElement.Clone();
        var before = dto.Extra.Count;
        var obj = GameObjectDtoConverter.FromDto(dto);
        Assert.NotNull(obj);
        Assert.Equal(before, dto.Extra.Count);
        Assert.True(dto.Extra.ContainsKey("__object_type"));
    }

    [Fact]
    public void LocationRef_ObjectIdWrongType_ThrowsJsonException()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<LocationRef>("""{"ObjectId":"abc"}""", JsonOptions.Default));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<LocationRef>("""{"Area":"limbo","X":1}""", JsonOptions.Default));
        // Top-level JSON null never reaches the converter (System.Text.Json returns null).
        Assert.Null(JsonSerializer.Deserialize<LocationRef>("null", JsonOptions.Default));
    }

    [Fact]
    public void TableLoader_CorruptRows_SkippedLoudly()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        db.Objects.Add(new ObjectRow { Id = 1, Type = "object", Version = 1, Data = GameObjectDtoSerializer.ToJson(GameObjectDto.Create(1, "Good")) });
        db.Objects.Add(new ObjectRow { Id = 2, Type = "object", Version = 1, Data = "not json at all" });
        db.SaveChanges();
        using var cap = new CaptureAtherizLog();
        var list = JsonTableLoader.LoadAll<ObjectRow, GameObjectDto>(db.Objects, GameObjectDtoSerializer.FromJson);
        Assert.Single(list);
        Assert.Contains("skipped 1 corrupt", cap.Read());
    }
}
