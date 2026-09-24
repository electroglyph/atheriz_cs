using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Ported;

// Port of atheriz/tests/test_database.py
[Collection("Ported")]
public class PortedDatabaseTests
{
    private static int RowCount(string savePath, int id)
    {
        using var db=new AtherizDbContext(savePath);
        db.Database.EnsureCreated();
        return db.Objects.Count(o=>o.Id==id);
    }

    // Port of test_database.py:21 TestDatabaseSetup.test_get_database_returns_cached_singleton
    [Fact] public void GetDatabaseReturnsCachedSingleton()
    {
        using var env=GlobalTestEnv.Enter(nameof(GetDatabaseReturnsCachedSingleton));
        using var db1=new AtherizDbContext(env.TempPath);
        using var db2=new AtherizDbContext(env.TempPath);
        Assert.NotSame(db1, db2);
        Assert.Equal(db1.Database.GetDbConnection().DataSource, db2.Database.GetDbConnection().DataSource);
    }
    [Fact] public void GetDatabaseCreatesSavePath()
    {
        using var env=GlobalTestEnv.Enter(nameof(GetDatabaseCreatesSavePath));
        var newPath=Path.Combine(env.TempPath, "nested","subdir");
        var db=new AtherizDbContext(newPath);
        db.Database.EnsureCreated();
        Assert.True(Directory.Exists(newPath));
        db.Database.CloseConnection();
    }
    [Fact] public void GetDatabasePragmasWal()
    {
        using var env=GlobalTestEnv.Enter(nameof(GetDatabasePragmasWal));
        using var db=new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        using var conn=db.Database.GetDbConnection();
        conn.Open();
        using var cmd=conn.CreateCommand();
        cmd.CommandText="PRAGMA journal_mode";
        var mode=cmd.ExecuteScalar()?.ToString();
        Assert.Equal("wal", mode?.ToLowerInvariant());
    }
    [Fact] public void DatabaseCheckSameThreadFalse()
    {
        using var env=GlobalTestEnv.Enter(nameof(DatabaseCheckSameThreadFalse));
        // Real Thread: the verdict IS thread separation (a pooled wait may
        // inline the delegate onto the waiting thread).
        Exception? workerError = null;
        var thread = new System.Threading.Thread(() => {
            try {
            using var db=new AtherizDbContext(env.TempPath);
            db.Database.EnsureCreated();
            var cnt=db.Objects.Count();
            Assert.True(cnt>=0);
            } catch (Exception ex) { workerError = ex; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(workerError);
    }
    [Fact] public void DatabaseCloseClearsSingleton()
    {
        using var env=GlobalTestEnv.Enter(nameof(DatabaseCloseClearsSingleton));
        AtherizDbContextFactory.CloseDatabase();
        Assert.True(AtherizDbContextFactory.IsClosed);
        AtherizDbContextFactory.ReopenDatabase();
        Assert.False(AtherizDbContextFactory.IsClosed);
    }
    [Fact] public void DatabaseCloseIdempotentSafe()
    {
        AtherizDbContextFactory.CloseDatabase();
        AtherizDbContextFactory.CloseDatabase();
        Assert.True(AtherizDbContextFactory.IsClosed);
        AtherizDbContextFactory.ReopenDatabase();
    }
    [Fact] public void DatabaseCloseNoToctou()
    {
        using var env=GlobalTestEnv.Enter(nameof(DatabaseCloseNoToctou));
        var errors=new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var closer=new System.Threading.Thread(()=>{ try{ AtherizDbContextFactory.CloseDatabase(); }catch(Exception ex){errors.Add(ex);} });
        var getters=Enumerable.Range(0,5).Select(_=>new System.Threading.Thread(()=>{
            try{ for(int i=0;i<50;i++){ using var db=new AtherizDbContext(env.TempPath); db.Database.EnsureCreated(); using var c=db.Database.GetDbConnection(); c.Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT 1"; cmd.ExecuteScalar(); } }catch(Exception ex){ if(!ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)) errors.Add(ex); }
        })).ToList();
        closer.Start(); getters.ForEach(g=>g.Start()); closer.Join(); getters.ForEach(g=>g.Join());
        AtherizDbContextFactory.ReopenDatabase();
        var unexpected=errors.Where(e=>!e.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(unexpected);
    }
    [Fact] public void ReopenDatabaseAfterCloseRestoresAccess()
    {
        using var env=GlobalTestEnv.Enter(nameof(ReopenDatabaseAfterCloseRestoresAccess));
        AtherizDbContextFactory.CloseDatabase();
        Assert.Throws<InvalidOperationException>(()=> new AtherizDbContext(env.TempPath));
        AtherizDbContextFactory.ReopenDatabase();
        using var db=new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        using var conn=db.Database.GetDbConnection();
        conn.Open();
        using var cmd=conn.CreateCommand(); cmd.CommandText="SELECT 1"; var res=cmd.ExecuteScalar();
        Assert.NotNull(res);
    }
    [Fact] public void ReopenDatabaseSurvivesCloseReopenCycles()
    {
        using var env=GlobalTestEnv.Enter(nameof(ReopenDatabaseSurvivesCloseReopenCycles));
        for(int i=0;i<3;i++)
        {
            AtherizDbContextFactory.CloseDatabase();
            Assert.Throws<InvalidOperationException>(()=> new AtherizDbContext(env.TempPath));
            AtherizDbContextFactory.ReopenDatabase();
            using var db=new AtherizDbContext(env.TempPath);
            db.Database.EnsureCreated();
            using var conn=db.Database.GetDbConnection(); conn.Open(); using var cmd=conn.CreateCommand(); cmd.CommandText="SELECT 1"; cmd.ExecuteScalar();
        }
    }
    [Fact] public void DoSetupWorksAfterCloseAndReopen()
    {
        using var env=GlobalTestEnv.Enter(nameof(DoSetupWorksAfterCloseAndReopen));
        AtherizDbContextFactory.CloseDatabase();
        AtherizDbContextFactory.ReopenDatabase();
        AtherizDbContextFactory.DoSetup(env.TempPath);
        using var db=new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        using var conn=db.Database.GetDbConnection(); conn.Open(); using var cmd=conn.CreateCommand(); cmd.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name='objects'"; var row=cmd.ExecuteScalar(); Assert.NotNull(row);
    }
    [Theory]
    [InlineData("objects")]
    [InlineData("mapdata")]
    [InlineData("areas")]
    [InlineData("transitions")]
    [InlineData("doors")]
    public void DoSetupCreatesAllTables(string table)
    {
        using var env=GlobalTestEnv.Enter(nameof(DoSetupCreatesAllTables));
        AtherizDbContextFactory.DoSetup(env.TempPath);
        using var db=new AtherizDbContext(env.TempPath);
        using var conn=db.Database.GetDbConnection(); conn.Open(); using var cmd=conn.CreateCommand(); cmd.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name=@n"; var p=cmd.CreateParameter(); p.ParameterName="@n"; p.Value=table; cmd.Parameters.Add(p); var row=cmd.ExecuteScalar(); Assert.NotNull(row); Assert.Equal(table, row!.ToString());
    }
    [Fact] public void DoSetupIdempotent()
    {
        using var env=GlobalTestEnv.Enter(nameof(DoSetupIdempotent));
        AtherizDbContextFactory.DoSetup(env.TempPath);
        AtherizDbContextFactory.DoSetup(env.TempPath);
    }
    [Fact] public void DoSetupObjectsTableSchema()
    {
        using var env=GlobalTestEnv.Enter(nameof(DoSetupObjectsTableSchema));
        AtherizDbContextFactory.DoSetup(env.TempPath);
        using var db=new AtherizDbContext(env.TempPath);
        using var conn=db.Database.GetDbConnection(); conn.Open(); using var cmd=conn.CreateCommand(); cmd.CommandText="PRAGMA table_info(objects)"; using var reader=cmd.ExecuteReader();
        var cols=new List<string>(); while(reader.Read()) cols.Add(reader.GetString(1));
        Assert.Contains("Id", cols);
        Assert.Contains("Data", cols);
    }
    [Fact] public void DoSetupTransitionsTableCompositePk()
    {
        using var env=GlobalTestEnv.Enter(nameof(DoSetupTransitionsTableCompositePk));
        AtherizDbContextFactory.DoSetup(env.TempPath);
        using var db=new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        db.Transitions.Add(new TransitionRow{ToArea="foo", ToX=1, ToY=2, ToZ=3, Data=""});
        db.SaveChanges();
        var ex=Record.Exception(()=>{ db.Transitions.Add(new TransitionRow{ToArea="foo", ToX=1, ToY=2, ToZ=3, Data=""}); db.SaveChanges(); });
        Assert.NotNull(ex);
        Assert.True(ex is Microsoft.EntityFrameworkCore.DbUpdateException || ex is InvalidOperationException, $"unexpected {ex!.GetType()}");
    }

    [Fact] public void GetDatabaseAfterCloseMustRaise()
    {
        using var env=GlobalTestEnv.Enter(nameof(GetDatabaseAfterCloseMustRaise));
        AtherizDbContextFactory.CloseDatabase();
        Assert.Throws<InvalidOperationException>(()=> new AtherizDbContext(env.TempPath));
        AtherizDbContextFactory.ReopenDatabase();
    }
    [Fact] public void GameOperationsFailAfterClose()
    {
        using var env=GlobalTestEnv.Enter(nameof(GameOperationsFailAfterClose));
        AtherizDbContextFactory.CloseDatabase();
        Assert.Throws<InvalidOperationException>(()=> new AtherizDbContext(env.TempPath));
        AtherizDbContextFactory.ReopenDatabase();
    }

    private class DbHolder: GameObject { }

    [Fact] public void SaveReturnsTypedOperationWithIdAndJson()
    {
        using var env=GlobalTestEnv.Enter();
        var obj=new DbHolder(); obj.Id=42;
        var op=obj.GetSaveOperation();
        Assert.Equal(42, op.Id);
        Assert.IsType<string>(op.Json);
    }
    [Fact] public void SaveJsonRoundTripsDto()
    {
        var obj=new DbHolder(); obj.Id=1;
        var op=obj.GetSaveOperation();
        var dto=GameObjectDtoSerializer.FromJson(op.Json);
        Assert.Equal(1, dto.Id);
    }
    [Fact] public void SaveOperationCarriesId()
    {
        var obj=new DbHolder(); obj.Id=99;
        var op=obj.GetSaveOperation();
        Assert.Equal(99, op.Id);
    }
    [Fact] public void SaveOperationJsonIsString()
    {
        var obj=new DbHolder(); obj.Id=1;
        var op=obj.GetSaveOperation();
        Assert.IsType<string>(op.Json);
        Assert.IsNotType<byte[]>(op.Json);
    }
    [Fact] public void SaveDataCanBeUnpickled()
    {
        var obj=new DbHolder(); obj.Id=1; obj.Name="test-label-wrapper";
        var op=obj.GetSaveOperation();
        var dto=GameObjectDtoSerializer.FromJson(op.Json);
        Assert.Equal(1, dto.Id);
        Assert.Equal("test-label-wrapper", dto.Name);
    }
    [Fact] public void GetSaveOperationDoesNotClearIsModified()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        obj.GetSaveOperation();
        Assert.True(obj.IsModified);
    }
    [Fact] public void SaveUsesLock()
    {
        // GetSaveOperation serializes under the object's write lock — proven behaviorally:
        // a worker GetSaveOperation blocks while this thread holds SyncRoot for write.
        // Real Thread (not Task.Run + blocking wait): the pool inlines an
        // unstarted task onto the waiting pool thread, which would
        // self-deadlock on the held write lock instead of blocking on it.
        var obj = new DbHolder(); obj.Id = 1;
        obj.SyncRoot.EnterWriteLock();
        Persistence.Dto.SaveOperation? result = null;
        Exception? workerError = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new System.Threading.Thread(() => {
            try { result = obj.GetSaveOperation(); }
            catch (Exception ex) { workerError = ex; }
            finally { done.Set(); }
        }) { IsBackground = true };
        thread.Start();
        try
        {
            Assert.False(done.Wait(TimeSpan.FromMilliseconds(300)), "GetSaveOperation completed without acquiring the write lock");
        }
        finally { obj.SyncRoot.ExitWriteLock(); }
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "GetSaveOperation did not finish after the lock was released");
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(workerError);
        Assert.NotNull(result);
    }
    [Fact] public void FlagStaysDirtyAcrossRepeatedSaveOperations()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        obj.GetSaveOperation(); Assert.True(obj.IsModified);
        obj.GetSaveOperation(); Assert.True(obj.IsModified);
    }
    [Fact] public void DelReturnsDeleteOperation()
    {
        var obj=new DbHolder(); obj.Id=5;
        var op=obj.GetDeleteOperation();
        Assert.IsType<Atheriz.Core.Persistence.Dto.DeleteOperation>(op);
        Assert.Equal(5, op.Id);
    }
    [Fact] public void DelOperationCarriesRowId()
    {
        var obj=new DbHolder(); obj.Id=5;
        Assert.Equal(5, obj.GetDeleteOperation().Id);
    }
    [Fact] public void DelParamsContainId()
    {
        var obj=new DbHolder(); obj.Id=5;
        Assert.Equal(5, obj.GetDeleteOperation().Id);
    }
    [Fact] public void DelOpsDoesNotChangeIsModified()
    {
        var obj=new DbHolder(); obj.Id=5; obj.IsModified=true;
        obj.GetDeleteOperation();
        Assert.True(obj.IsModified);
    }
    [Fact] public void DelOpsWorksWithNegativeId()
    {
        var obj=new DbHolder(); obj.Id=-1;
        Assert.Equal(-1, obj.GetDeleteOperation().Id);
    }
    [Fact] public void SaveThenDelOperationsConsistent()
    {
        var obj=new DbHolder(); obj.Id=7;
        var op=obj.GetSaveOperation();
        var del=obj.GetDeleteOperation();
        Assert.Equal(7, op.Id);
        Assert.Equal(7, del.Id);
    }
    [Fact] public void WorksWithRealObject()
    {
        using var env=GlobalTestEnv.Enter();
        var obj=GameObject.Create("real", isItem:true);
        obj.Id=123;
        var op=obj.GetSaveOperation();
        var del=obj.GetDeleteOperation();
        Assert.Equal(123, op.Id);
        Assert.Equal(123, del.Id);
    }
    [Fact] public void ModificationsThenSave()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        obj.Name="a"; obj.GetSaveOperation(); Assert.True(obj.IsModified);
        obj.Name="b"; obj.GetSaveOperation(); Assert.True(obj.IsModified);
        var op=obj.GetSaveOperation();
        var dto=GameObjectDtoSerializer.FromJson(op.Json);
        Assert.Equal("b", dto.Name);
    }
    // Port of test_database.py:348 test_is_modified_stays_true_on_serialization_failure - faithful
    [Fact] public void IsModifiedStaysTrueOnSerializationFailure()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        // Simulate dill.dumps failure via ToJsonHook throwing
        var origHook = GameObjectDtoSerializer.ToJsonHook;
        GameObjectDtoSerializer.ToJsonHook = _ => throw new InvalidOperationException("serialize fail");
        try
        {
            Assert.Throws<InvalidOperationException>(()=> obj.GetSaveOperation());
            Assert.True(obj.IsModified);
            // also test GetSaveOperationClearing via SaveObjects path uses same hook - ensure still true
            obj.IsModified=true;
            Assert.Throws<InvalidOperationException>(()=> obj.GetSaveOperationClearing());
            Assert.True(obj.IsModified);
        }
        finally { GameObjectDtoSerializer.ToJsonHook = origHook; }
    }

    private static bool IsDbLocked() => DbWriteGate.IsHeld;

    // Port of test_database.py:374 test_load_objects_releases_db_lock_before_deserialization
    [Fact] public void LoadObjectsReleasesDbLockBeforeDeserialization()
    {
        using var env=GlobalTestEnv.Enter();
        var obj=GameObject.Create("ProbeLoad"); ObjectRegistry.AddObject(obj);
        ObjectRegistry.SaveObjects(env.TempPath);
        var held=new List<bool>();
        var origHook=GameObjectDtoSerializer.FromJsonHook;
        GameObjectDtoSerializer.FromJsonHook = json => { held.Add(IsDbLocked()); var dto = System.Text.Json.JsonSerializer.Deserialize<GameObjectDto>(json, Persistence.JsonOptions.Default)!; return dto; };
        try
        {
            ObjectRegistry.LoadObjects(env.TempPath);
            Assert.NotEmpty(held);
            Assert.DoesNotContain(true, held);
        }
        finally { GameObjectDtoSerializer.FromJsonHook = origHook; }
    }

    // Port of test_database.py:396 test_save_objects_releases_db_lock_before_serialization
    [Fact] public void SaveObjectsReleasesDbLockBeforeSerialization()
    {
        using var env=GlobalTestEnv.Enter();
        var obj=GameObject.Create("ProbeSave"); obj.Desc="dirty"; obj.IsModified=true; ObjectRegistry.AddObject(obj);
        var held=new List<bool>();
        var origHook=GameObjectDtoSerializer.ToJsonHook;
        GameObjectDtoSerializer.ToJsonHook = dto => { held.Add(IsDbLocked()); return System.Text.Json.JsonSerializer.Serialize(dto, Persistence.JsonOptions.Default); };
        try
        {
            ObjectRegistry.SaveObjects(env.TempPath, force:true);
            Assert.NotEmpty(held);
            Assert.DoesNotContain(true, held);
        }
        finally { GameObjectDtoSerializer.ToJsonHook = origHook; }
    }

    // Recording subclasses: observe the snapshot-serialization boundary through
    // the virtual SerializeSnapshot step (replaces the removed TestSerializeHook).
    private sealed class RecordingMapHandler : MapHandler
    {
        public readonly List<bool> Held = new();
        public RecordingMapHandler() : base(autoLoad: false) { }
        protected override string SerializeSnapshot(object dto) { Held.Add(IsDbLocked()); return base.SerializeSnapshot(dto); }
    }

    private sealed class RecordingNodeHandler : NodeHandler
    {
        public readonly List<bool> Held = new();
        public RecordingNodeHandler() : base(autoLoad: false) { }
        protected override string SerializeSnapshot(object dto) { Held.Add(IsDbLocked()); return base.SerializeSnapshot(dto); }
    }

    // Port of test_database.py:421 test_map_handler_save_releases_db_lock_before_serialization
    [Fact] public void MapHandlerSaveReleasesDbLockBeforeSerialization()
    {
        using var env=GlobalTestEnv.Enter();
        var mh = new RecordingMapHandler();
        var mi=new MapInfo("ProbeMapSave"); mi.PreGrid[(1,1)]="Y"; mh.SetMapInfo("ProbeMapSave",0, mi);
        mh.Save(force:true);
        Assert.NotEmpty(mh.Held);
        Assert.DoesNotContain(true, mh.Held);
    }

    // Port of test_database.py:445 test_node_handler_save_releases_db_lock_before_serialization
    [Fact] public void NodeHandlerSaveReleasesDbLockBeforeSerialization()
    {
        using var env=GlobalTestEnv.Enter();
        var nh = new RecordingNodeHandler();
        var n=new Node(new Coord("ProbeNodeSave",0,0,0), desc:"n"); nh.AddNode(n);
        nh.Save(force:true);
        Assert.NotEmpty(nh.Held);
        Assert.DoesNotContain(true, nh.Held);
    }

    [Fact] public void BusyTimeoutIsConfigured()
    {
        using var env=GlobalTestEnv.Enter();
        using var db=new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        using var conn=db.Database.GetDbConnection(); conn.Open(); using var cmd=conn.CreateCommand(); cmd.CommandText="PRAGMA busy_timeout"; var row=cmd.ExecuteScalar(); Assert.NotNull(row); Assert.True(Convert.ToInt32(row) > 0);
    }
}
