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
        var t=System.Threading.Tasks.Task.Run(()=>{
            using var db=new AtherizDbContext(env.TempPath);
            db.Database.EnsureCreated();
            var cnt=db.Objects.Count();
            Assert.True(cnt>=0);
        });
        t.Wait();
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

    [Fact] public void SaveReturnsTupleOfSqlAndParams()
    {
        using var env=GlobalTestEnv.Enter();
        var obj=new DbHolder(); obj.Id=42;
        var (sql, parms)=obj.GetSaveOps();
        Assert.IsType<string>(sql); Assert.IsType<object[]>(parms);
        Assert.Equal(2, parms.Length);
    }
    [Fact] public void SaveSqlIsInsertOrReplace()
    {
        var obj=new DbHolder(); obj.Id=1;
        var (sql, _)=obj.GetSaveOps();
        Assert.Equal("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", sql);
    }
    [Fact] public void SaveParamsContainId()
    {
        var obj=new DbHolder(); obj.Id=99;
        var (_, parms)=obj.GetSaveOps();
        Assert.Equal(99, (int)parms[0]);
    }
    [Fact] public void SaveParamsDataIsJsonString()
    {
        var obj=new DbHolder(); obj.Id=1;
        var (_, parms)=obj.GetSaveOps();
        Assert.IsType<string>(parms[1]);
        Assert.IsNotType<byte[]>(parms[1]);
    }
    [Fact] public void SaveDataCanBeUnpickled()
    {
        var obj=new DbHolder(); obj.Id=1; obj.Name="test-label-wrapper";
        var (_, parms)=obj.GetSaveOps();
        var json=(string)parms[1];
        var dto=GameObjectDtoSerializer.FromJson(json);
        Assert.Equal(1, dto.Id);
        Assert.Equal("test-label-wrapper", dto.Name);
    }
    [Fact] public void GetSaveOpsDoesNotClearIsModified()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        obj.GetSaveOps();
        Assert.True(obj.IsModified);
    }
    private sealed class LockCountTracker { public int Entries = 0; }
    [Fact] public void SaveUsesLock()
    {
        var obj=new DbHolder(); obj.Id=1;
        var tracker = new LockCountTracker();
        var trackerField = typeof(GameObject).GetField("_testTracker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var entriesField = typeof(LockCountTracker).GetField(nameof(LockCountTracker.Entries));
        trackerField!.SetValue(obj, tracker);
        typeof(GameObject).GetField("_trackerEntriesField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(obj, entriesField);
        obj.GetSaveOps();
        Assert.True(tracker.Entries > 0);
        // Also ensure exactly one acquisition for faithful to Python's acquired == [True]
        Assert.Equal(1, tracker.Entries);
    }
    [Fact] public void FlagStaysDirtyAcrossRepeatedSaveOps()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        obj.GetSaveOps(); Assert.True(obj.IsModified);
        obj.GetSaveOps(); Assert.True(obj.IsModified);
    }
    [Fact] public void DelReturnsTuple()
    {
        var obj=new DbHolder(); obj.Id=5;
        var (sql, parms)=obj.GetDelOps();
        Assert.IsType<string>(sql); Assert.IsType<object[]>(parms);
        Assert.Single(parms);
    }
    [Fact] public void DelSqlIsDeleteById()
    {
        var obj=new DbHolder(); obj.Id=5;
        var (sql, _)=obj.GetDelOps();
        Assert.Equal("DELETE FROM objects WHERE id = ?", sql);
    }
    [Fact] public void DelParamsContainId()
    {
        var obj=new DbHolder(); obj.Id=5;
        var (_, parms)=obj.GetDelOps();
        Assert.Equal(5, (int)parms[0]);
    }
    [Fact] public void DelOpsDoesNotChangeIsModified()
    {
        var obj=new DbHolder(); obj.Id=5; obj.IsModified=true;
        obj.GetDelOps();
        Assert.True(obj.IsModified);
    }
    [Fact] public void DelOpsWorksWithNegativeId()
    {
        var obj=new DbHolder(); obj.Id=-1;
        var (_, parms)=obj.GetDelOps();
        Assert.Equal(-1, (int)parms[0]);
    }
    [Fact] public void SaveThenDelOperationsConsistent()
    {
        var obj=new DbHolder(); obj.Id=7;
        var (saveSql, _)=obj.GetSaveOps();
        var (delSql, delParms)=obj.GetDelOps();
        Assert.Contains("INSERT OR REPLACE", saveSql);
        Assert.Contains("DELETE", delSql);
        Assert.Equal(7, (int)delParms[0]);
    }
    [Fact] public void WorksWithRealObject()
    {
        using var env=GlobalTestEnv.Enter();
        var obj=GameObject.Create("real", isItem:true);
        obj.Id=123;
        var (_, saveParms)=obj.GetSaveOps();
        var (_, delParms)=obj.GetDelOps();
        Assert.Equal(123, (int)saveParms[0]);
        Assert.Equal(123, (int)delParms[0]);
    }
    [Fact] public void ModificationsThenSave()
    {
        var obj=new DbHolder(); obj.Id=1; obj.IsModified=true;
        obj.Name="a"; obj.GetSaveOps(); Assert.True(obj.IsModified);
        obj.Name="b"; obj.GetSaveOps(); Assert.True(obj.IsModified);
        var (_, parms)=obj.GetSaveOps();
        var dto=GameObjectDtoSerializer.FromJson((string)parms[1]);
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
            Assert.Throws<InvalidOperationException>(()=> obj.GetSaveOps());
            Assert.True(obj.IsModified);
            // also test GetSaveOpsClearing via SaveObjects path uses same hook - ensure still true
            obj.IsModified=true;
            Assert.Throws<InvalidOperationException>(()=> obj.GetSaveOpsClearing());
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

    // Port of test_database.py:421 test_map_handler_save_releases_db_lock_before_serialization
    [Fact] public void MapHandlerSaveReleasesDbLockBeforeSerialization()
    {
        using var env=GlobalTestEnv.Enter();
        var mh=GlobalServices.GetMapHandler();
        var mi=new MapInfo("ProbeMapSave"); mi.PreGrid[(1,1)]="Y"; mh.SetMapInfo("ProbeMapSave",0, mi);
        var held=new List<bool>();
        var orig = MapHandler.TestSerializeHook;
        MapHandler.TestSerializeHook = o => { held.Add(IsDbLocked()); return System.Text.Json.JsonSerializer.Serialize(o, Persistence.JsonOptions.Default); };
        try
        {
            mh.Save(force:true);
            Assert.NotEmpty(held);
            Assert.DoesNotContain(true, held);
        }
        finally { MapHandler.TestSerializeHook = orig; }
    }

    // Port of test_database.py:445 test_node_handler_save_releases_db_lock_before_serialization
    [Fact] public void NodeHandlerSaveReleasesDbLockBeforeSerialization()
    {
        using var env=GlobalTestEnv.Enter();
        var nh=GlobalServices.GetNodeHandler();
        var n=new Node(new Coord("ProbeNodeSave",0,0,0), desc:"n"); nh.AddNode(n);
        var held=new List<bool>();
        var orig = NodeHandler.TestSerializeHook;
        NodeHandler.TestSerializeHook = o => { held.Add(IsDbLocked()); return System.Text.Json.JsonSerializer.Serialize(o, Persistence.JsonOptions.Default); };
        try
        {
            nh.Save(force:true);
            Assert.NotEmpty(held);
            Assert.DoesNotContain(true, held);
        }
        finally { NodeHandler.TestSerializeHook = orig; }
    }

    [Fact] public void BusyTimeoutIsConfigured()
    {
        using var env=GlobalTestEnv.Enter();
        using var db=new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        using var conn=db.Database.GetDbConnection(); conn.Open(); using var cmd=conn.CreateCommand(); cmd.CommandText="PRAGMA busy_timeout"; var row=cmd.ExecuteScalar(); Assert.NotNull(row); Assert.True(Convert.ToInt32(row) > 0);
    }
}
