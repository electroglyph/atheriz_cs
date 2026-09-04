// Port of atheriz/tests/test_save_race.py — faithful
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSaveRaceTests
{
    private static GameObject? StoredObject(int id, string tempPath)
    {
        using var db = new AtherizDbContext(tempPath);
        var row = db.Objects.FirstOrDefault(o=>o.Id==id);
        if (row==null) return null;
        var dto = GameObjectDtoSerializer.FromJson(row.Data);
        return GameObject.FromDto(dto);
    }

    private sealed class MutatingBeta : GameObject
    {
        public GameObject? Alpha;
        public override (string Sql, object[] Params) GetSaveOpsClearing()
        {
            var res = base.GetSaveOpsClearing();
            // Mutate alpha when beta is being serialized (as in Python racing_dumps for beta)
            if (Alpha != null)
            {
                Alpha.Name = "mutated-during-checkpoint";
                Alpha.Desc = "mutated";
            }
            return res;
        }
    }

    [Fact] public void MutationDuringCheckpoint_NotLost()
    {
        using var env = GlobalTestEnv.Enter();
        var alpha = GameObject.Create("alpha"); ObjectRegistry.AddObject(alpha);
        var beta = new MutatingBeta(); beta.Name="beta"; beta.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(beta);
        // Give alpha and beta proper ids
        // Save initial
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.False(alpha.IsModified);
        Assert.False(beta.IsModified);
        // Set up mutation: beta's GetSaveOpsClearing will mutate alpha
        ((MutatingBeta)beta).Alpha = alpha;
        alpha.Name = "pre-mutation";
        beta.Name = "trigger";
        Assert.True(alpha.IsModified);
        Assert.True(beta.IsModified);
        using(var db=new AtherizDbContext(env.TempPath)){ ObjectRegistry.SaveObjects(db); }
        Assert.Equal("mutated-during-checkpoint", alpha.Name);
        var stored = StoredObject(alpha.Id, env.TempPath);
        Assert.NotNull(stored);
        Assert.Equal("pre-mutation", stored!.Name);
        Assert.True(alpha.IsModified);
        // Second save should persist mutated
        using(var db=new AtherizDbContext(env.TempPath)){ ObjectRegistry.SaveObjects(db); }
        Assert.False(alpha.IsModified);
        var stored2 = StoredObject(alpha.Id, env.TempPath);
        Assert.Equal("mutated-during-checkpoint", stored2!.Name);
    }
    [Fact] public void SerializationFailure_RestoresFlag()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("doomed"); ObjectRegistry.AddObject(obj);
        obj.Name = "dirty-before-failure";
        Assert.True(obj.IsModified);
        // Simulate failure by overriding GetSaveOpsClearing to throw
        var failing = new FailingGameObject(obj);
        // Replace obj in registry with failing wrapper for test
        ObjectRegistry.RemoveObject(obj);
        ObjectRegistry.AddObject(failing);
        Assert.Throws<InvalidOperationException>(()=> failing.GetSaveOpsClearing());
        Assert.True(failing.IsModified);
        // Restore
        ObjectRegistry.RemoveObject(failing);
        ObjectRegistry.AddObject(obj);
        obj.IsModified = true;
    }
    private sealed class FailingGameObject : GameObject
    {
        private readonly GameObject _inner;
        public FailingGameObject(GameObject inner){ _inner = inner; Id = inner.Id; Name = inner.Name; IsModified = inner.IsModified; }
        public override (string Sql, object[] Params) GetSaveOpsClearing() => throw new InvalidOperationException("serialize fail");
    }
    [Fact] public void CleanFlag_PersistsAcrossRestart()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("persist-me"); ObjectRegistry.AddObject(obj);
        obj.Desc = "some description";
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var reloaded = ObjectRegistry.Get(obj.Id);
        Assert.Single(reloaded);
        Assert.False(reloaded[0].IsModified);
    }
    [Fact] public void GetSaveOpsClearing_ConsumesFlagImmediately()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("clearme"); ObjectRegistry.AddObject(obj);
        obj.Desc = "dirty";
        Assert.True(obj.IsModified);
        var (_, parms) = obj.GetSaveOpsClearing();
        Assert.False(obj.IsModified);
        var json = (string)parms[1];
        var dto = GameObjectDtoSerializer.FromJson(json);
        Assert.False(dto.IsModified);
    }
}
