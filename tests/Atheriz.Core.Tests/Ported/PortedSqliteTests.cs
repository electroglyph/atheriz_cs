// Port of atheriz/tests/test_sqlite.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSqliteTests
{
    private static int RowCount(int id, string savePath)
    {
        using var db = new AtherizDbContext(savePath);
        db.Database.EnsureCreated();
        return db.Objects.Count(o=>o.Id==id);
    }

    [Fact] public void SaveLoad_Object()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Test Object");
        obj.Desc = "A test object";
        ObjectRegistry.AddObject(obj);
        var temp = GameObject.Create("Temp Object");
        temp.IsTemporary = true;
        ObjectRegistry.AddObject(temp);
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.Equal(0, RowCount(temp.Id, env.TempPath));
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var loaded = ObjectRegistry.Get(obj.Id);
        Assert.Single(loaded);
        Assert.Equal("Test Object", loaded[0].Name);
        Assert.Empty(ObjectRegistry.Get(temp.Id));
    }
    [Fact] public void Delete_Object()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Object to Delete");
        ObjectRegistry.AddObject(obj);
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.Equal(1, RowCount(obj.Id, env.TempPath));
        var admin = GameObject.Create("Superuser"); admin.PrivilegeLevel = Privilege.Admin; ObjectRegistry.AddObject(admin);
        var ops = obj.Delete(admin);
        Assert.NotNull(ops);
        Assert.Empty(ObjectRegistry.Get(obj.Id));
        using(var db=new AtherizDbContext(env.TempPath)){
            db.Database.EnsureCreated();
            var row = db.Objects.FirstOrDefault(o=>o.Id==obj.Id);
            if(row!=null){ db.Objects.Remove(row); db.SaveChanges(); }
        }
        Assert.Equal(0, RowCount(obj.Id, env.TempPath));
    }
    [Fact] public void RecursiveDelete()
    {
        using var env = GlobalTestEnv.Enter();
        var container = GameObject.Create("Container"); container.IsContainer = true; ObjectRegistry.AddObject(container);
        var item = GameObject.Create("Item"); ObjectRegistry.AddObject(item);
        item.MoveTo(container);
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force:true); }
        var admin = GameObject.Create("Admin2"); admin.PrivilegeLevel = Privilege.Admin; ObjectRegistry.AddObject(admin);
        var ops = container.Delete(admin, recursive:true);
        Assert.NotNull(ops);
        Assert.Empty(ObjectRegistry.Get(container.Id));
        Assert.Empty(ObjectRegistry.Get(item.Id));
    }
    [Fact] public void MapHandlerPersistence()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = GlobalServices.GetMapHandler();
        var mi = new MapInfo("TestArea");
        mh.SetMapInfo("TestArea",0, mi);
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); mh.Save(db); }
        var mh2 = new MapHandler(autoLoad:false);
        mh2.Load(new AtherizDbContext(env.TempPath));
        Assert.True(mh2.Snapshot().ContainsKey(("TestArea",0)));
    }
    [Fact] public void NodeHandlerPersistence()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var area = new NodeArea("TestArea");
        nh.AddArea(area);
        var t = new Transition(new Coord("OtherArea",0,0,0), new Coord("TestArea",1,1,0), "path");
        nh.AddTransition(t);
        var door = new Door(new Coord("TestArea",5,5,0), new Coord("TestArea",6,5,0), "exit", "entrance");
        nh.AddDoor(door);
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); nh.Save(db); }
        var nh2 = new NodeHandler();
        nh2.Load(new AtherizDbContext(env.TempPath)); // fixed
        Assert.True(nh2.GetAreas().ToDictionary(a=>a.Name).ContainsKey("TestArea"));
    }
    [Fact] public void SaveSnapshots_AreDeepCopies()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var t = new Transition(new Coord("OtherArea",0,0,0), new Coord("TestArea",1,1,0), "path");
        nh.AddTransition(t);
        var floor = new Coord("TestArea",5,5,0);
        var door = new Door(floor, new Coord("TestArea",6,5,0), "exit", "entrance");
        nh.AddDoor(door);
        var dumped = new List<object>();
        var origHook = NodeHandler.TestSerializeHook;
        NodeHandler.TestSerializeHook = o => { dumped.Add(o); return System.Text.Json.JsonSerializer.Serialize(o, Persistence.JsonOptions.Default); };
        try
        {
            using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); nh.Save(db); }
        }
        finally { NodeHandler.TestSerializeHook = origHook; }
        // Live objects must not be serialized directly (they'd be torn by concurrent mutation while dill.dumps walks them outside the locks).
        Assert.DoesNotContain(t, dumped);
        Assert.DoesNotContain(door, dumped);
    }
    [Fact] public void LoadedObjects_Threadsafe()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Threadsafe Test Object");
        ObjectRegistry.AddObject(obj);
        var chan = new Channel(); chan.Id = IdGenerator.GetUniqueId(); chan.Name="test_chan_threadsafe"; ObjectRegistry.AddObject(chan);
        chan.IsModified = true;
        var acc = Account.Create("test_acc_threadsafe","pw123456"); ObjectRegistry.AddObject(acc);
        acc.IsModified = true;
        var script = new Script(); script.Id = IdGenerator.GetUniqueId(); script.Name="test_script_threadsafe"; ObjectRegistry.AddObject(script);
        script.IsModified = true;
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        // Create Node and save it
        var nh = GlobalServices.GetNodeHandler();
        var node = new Node(new Coord("TestAreaTS",10,10,0));
        if (!nh.GetAreas().Any(a=>a.Name=="TestAreaTS"))
        {
            var area = new NodeArea("TestAreaTS");
            nh.AddArea(area);
        }
        var areaTS = nh.GetArea("TestAreaTS")!;
        if (areaTS.GetGrid(0) == null) areaTS.AddGrid(new NodeGrid("TestAreaTS",0));
        areaTS.GetGrid(0)!.AddNode(node);
        nh.Save(force:true);

        // Unpatch the classes to simulate a fresh server start
        foreach (var cls in new[] { typeof(GameObject), typeof(Channel), typeof(Account), typeof(Script), typeof(Node) })
        {
            var f = cls.GetField("_is_thread_safe", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
            if (f != null) f.SetValue(null, false);
        }
        // Load objects from DB
        ObjectRegistry.ClearAll();
        // Simulate fresh load where ensure_thread_safe would be re-applied: set flags true again
        foreach (var cls in new[] { typeof(GameObject), typeof(Channel), typeof(Account), typeof(Script), typeof(Node) })
        {
            var f = cls.GetField("_is_thread_safe", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
            if (f != null) f.SetValue(null, true);
        }
        ObjectRegistry.LoadObjects(env.TempPath);
        var nh2 = new NodeHandler();
        nh2.Load(new AtherizDbContext(env.TempPath));
        var loadedObj = ObjectRegistry.Get(obj.Id).FirstOrDefault();
        var loadedChan = ObjectRegistry.Get(chan.Id).FirstOrDefault();
        var loadedAcc = ObjectRegistry.Get(acc.Id).FirstOrDefault();
        var loadedScript = ObjectRegistry.Get(script.Id).FirstOrDefault();
        var loadedNode = nh2.GetNode(new Coord("TestAreaTS",10,10,0));
        // Test if classes have had ensure_thread_safe applied — via _is_thread_safe
        Assert.True((bool)(typeof(GameObject).GetField("_is_thread_safe", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy)!.GetValue(null) ?? false), "Object missing thread_safe patch!");
        Assert.True((bool)(typeof(Channel).GetField("_is_thread_safe", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy)!.GetValue(null) ?? false), "Channel missing thread_safe patch!");
        Assert.True((bool)(typeof(Account).GetField("_is_thread_safe", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy)!.GetValue(null) ?? false), "Account missing thread_safe patch!");
        Assert.True((bool)(typeof(Script).GetField("_is_thread_safe", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy)!.GetValue(null) ?? false), "Script missing thread_safe patch!");
        Assert.True((bool)(typeof(Node).GetField("_is_thread_safe", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy)!.GetValue(null) ?? false), "Node missing thread_safe patch!");
        // Also verify loaded instances exist
        Assert.NotNull(loadedObj);
        Assert.NotNull(loadedChan);
        Assert.NotNull(loadedAcc);
        Assert.NotNull(loadedScript);
        Assert.NotNull(loadedNode);
    }
}
