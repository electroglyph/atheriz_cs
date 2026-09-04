// Port of atheriz/tests/test_modify.py:1 — faithful 7 tests
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedModifyTests
{
    // test_modify.py:6 def test_init_is_modified
    [Fact] public void InitIsModified() // test_modify.py:6
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new GameObject();
        Assert.True(obj.IsModified);
    }

    // test_modify.py:11 def test_create_is_modified
    [Fact] public void CreateIsModified() // test_modify.py:11
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Test Obj");
        Assert.True(obj.IsModified);
    }

    // test_modify.py:17 def test_save_resets_is_modified
    [Fact] public void SaveResetsIsModified() // test_modify.py:17
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Test Obj");
        ObjectRegistry.AddObject(obj);
        Assert.True(obj.IsModified);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
    }

    // test_modify.py:24 def test_attribute_change_sets_is_modified
    [Fact] public void AttributeChangeSetsIsModified() // test_modify.py:24
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Test Obj");
        ObjectRegistry.AddObject(obj);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
        // Changing name should trigger is_modified via EnsureThreadSafe / setter
        obj.Name = "New Name";
        Assert.True(obj.IsModified);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
        // Changing desc should also trigger
        obj.Desc = "New Desc";
        Assert.True(obj.IsModified);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
        // Changing symbol should also trigger
        obj.Symbol = "Y";
        Assert.True(obj.IsModified);
    }

    // test_modify.py:48 def test_save_optimization_logic
    [Fact] public void SaveOptimizationLogic() // test_modify.py:48
    {
        using var env = GlobalTestEnv.Enter();
        var obj1 = GameObject.Create("Obj 1");
        var obj2 = GameObject.Create("Obj 2");
        ObjectRegistry.AddObject(obj1);
        ObjectRegistry.AddObject(obj2);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj1.IsModified);
        Assert.False(obj2.IsModified);
        // Modify only obj1
        obj1.Name = "Modified 1";
        Assert.True(obj1.IsModified);
        Assert.False(obj2.IsModified);
        // After save, both should be False
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj1.IsModified);
        Assert.False(obj2.IsModified);
    }

    // test_modify.py:67 def test_load_is_modified_false
    [Fact] public void LoadIsModifiedFalse() // test_modify.py:67
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Persistent Obj");
        ObjectRegistry.AddObject(obj);
        var objId = obj.Id;
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
        // Force reload from DB — mimic python close/reopen
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch {}
        // _CLOSED = False equivalent
        Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase();
        // LoadObjects should clear registry and reload
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var loaded = ObjectRegistry.Get(objId);
        Assert.Single(loaded);
        var loadedObj = loaded[0];
        Assert.Equal("Persistent Obj", loadedObj.Name);
        Assert.False(loadedObj.IsModified);
    }

    // test_modify.py:84 def test_move_is_modified
    [Fact] public void MoveIsModified() // test_modify.py:84
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Mobile Obj");
        var container1 = GameObject.Create("Container 1", isContainer:true);
        var container2 = GameObject.Create("Container 2", isContainer:true);
        ObjectRegistry.AddObject(obj);
        ObjectRegistry.AddObject(container1);
        ObjectRegistry.AddObject(container2);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
        Assert.False(container1.IsModified);
        Assert.False(container2.IsModified);
        // Initial move to container1
        obj.MoveTo(container1);
        Assert.True(obj.IsModified);
        Assert.True(container1.IsModified);
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        Assert.False(obj.IsModified);
        Assert.False(container1.IsModified);
        // Move from container1 to container2
        obj.MoveTo(container2);
        Assert.True(obj.IsModified);
        Assert.True(container1.IsModified);
        Assert.True(container2.IsModified);
    }
}
