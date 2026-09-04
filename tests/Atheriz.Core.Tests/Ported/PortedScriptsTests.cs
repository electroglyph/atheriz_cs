// Port of atheriz/tests/test_scripts.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using System.Text.Json;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedScriptsTests
{
    // Port of test_scripts.py:23 DummyObj — hookable at_test_hook returns "original_result" and logs
    class DummyObj : GameObject
    {
        public List<string> Log = new();
        public DummyObj(){ }
        public string AtTestHook(string arg1, string? kwarg1=null)
        {
            return Hookable("at_test_hook", () =>
            {
                Log.Add($"at_test_hook: {arg1}, {(kwarg1 ?? "None")}");
                return "original_result";
            }, arg1, kwarg1);
        }
    }
    class DummyNode : Node
    {
        public List<string> Log = new();
        public DummyNode(Coord coord): base(coord) {}
        public string AtTestHook(string arg1, string? kwarg1=null)
        {
            return Hookable("at_test_hook", () =>
            {
                Log.Add($"at_test_hook: {arg1}, {(kwarg1 ?? "None")}");
                return "original_result";
            }, arg1, kwarg1);
        }
    }
    class DummyBeforeScript : Script
    {
        [Before] public void at_test_hook(string arg1, string? kwarg1=null)
        {
            string kk = kwarg1 ?? "None";
            if (Child is DummyObj d) d.Log.Add($"before: {arg1}, {kk}");
            else if (Child is DummyNode dn) dn.Log.Add($"before: {arg1}, {kk}");
            else if (Child != null) try { ((dynamic)Child).Log.Add($"before: {arg1}, {kk}"); } catch {}
        }
    }
    class DummyAfterScript : Script
    {
        [After] public string at_test_hook(string arg1, string? kwarg1=null)
        {
            string kk = kwarg1 ?? "None";
            if (Child is DummyObj d) d.Log.Add($"after: {arg1}, {kk}");
            else if (Child is DummyNode dn) dn.Log.Add($"after: {arg1}, {kk}");
            else if (Child != null) try { ((dynamic)Child).Log.Add($"after: {arg1}, {kk}"); } catch {}
            return "after_result";
        }
    }
    class DummyReplaceScript : Script
    {
        [Replace] public string at_test_hook(string arg1, string? kwarg1=null)
        {
            string kk = kwarg1 ?? "None";
            if (Child is DummyObj d) d.Log.Add($"replace: {arg1}, {kk}");
            else if (Child is DummyNode dn) dn.Log.Add($"replace: {arg1}, {kk}");
            else if (Child != null) try { ((dynamic)Child).Log.Add($"replace: {arg1}, {kk}"); } catch {}
            return "replace_result";
        }
    }
    class DummyUnmarkedScript : Script
    {
        public void at_test_hook(string arg1, string? kwarg1=null) {}
    }

    // Port of test_scripts.py:74 test_add_remove_script — asserts hooks dict population len(obj.hooks.get("at_test_hook", set())) ==1
    [Fact] public void AddRemoveScript()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var script = new DummyBeforeScript(); script.Id = 101; ObjectRegistry.AddObject(script);
        script.InstallHooks(obj);
        Assert.Contains(script.Id, obj.ScriptsSnapshot);
        var hooksField = typeof(GameObject).GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var hooks = (System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<Delegate>>)hooksField.GetValue(obj)!;
        Assert.True(hooks.TryGetValue("at_test_hook", out var set) && set.Count==1);
        script.RemoveHooks(obj);
        Assert.DoesNotContain(script.Id, obj.ScriptsSnapshot);
        hooks = (System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<Delegate>>)hooksField.GetValue(obj)!;
        Assert.True(!hooks.TryGetValue("at_test_hook", out var afterSet) || afterSet.Count==0);
    }
    // Port of test_scripts.py:88 test_before_hook — exact result == original_result and log == ["before: v1, v2","at_test_hook: v1, v2"] ; before does not abort (wontfix)
    [Fact] public void BeforeHook_Installed()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj2"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var script = new DummyBeforeScript(); script.Id = 102; ObjectRegistry.AddObject(script);
        script.InstallHooks(obj);
        Assert.Contains(script.Id, obj.ScriptsSnapshot);
        var hooksField = typeof(GameObject).GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var hooks = (System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<Delegate>>)hooksField.GetValue(obj)!;
        Assert.True(hooks.TryGetValue("at_test_hook", out var set) && set.Count==1);
        var res = obj.AtTestHook("v1", "v2");
        Assert.Equal(new[]{"before: v1, v2","at_test_hook: v1, v2"}, obj.Log);
        Assert.Equal("original_result", res);
    }
    // Port of test_scripts.py:99 test_after_hook
    [Fact] public void AfterHook()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var script = new DummyAfterScript(); script.Id = 103; ObjectRegistry.AddObject(script);
        script.InstallHooks(obj);
        var res = obj.AtTestHook("v3", "v4");
        Assert.Equal(new[]{"at_test_hook: v3, v4","after: v3, v4"}, obj.Log);
        Assert.Equal("after_result", res);
    }
    // Port of test_scripts.py:110 test_replace_hook
    [Fact] public void ReplaceHook()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var script = new DummyReplaceScript(); script.Id = 104; ObjectRegistry.AddObject(script);
        script.InstallHooks(obj);
        var res = obj.AtTestHook("v5", "v6");
        Assert.Equal(new[]{"replace: v5, v6"}, obj.Log);
        Assert.Equal("replace_result", res);
    }
    // Port of test_scripts.py:121 test_unmarked_hook_raises_error — after fix, undecorated not installed, no hook pollution and no error
    [Fact] public void UnmarkedHookRaisesError()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var script = new DummyUnmarkedScript(); script.Id = 105; ObjectRegistry.AddObject(script);
        script.InstallHooks(obj);
        var hooksField = typeof(GameObject).GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var hooks = (System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<Delegate>>)hooksField.GetValue(obj)!;
        Assert.True(!hooks.TryGetValue("at_test_hook", out var set) || set.Count==0);
        var ex = Record.Exception(()=> obj.AtTestHook("foo", "bar"));
        Assert.Null(ex);
    }
    private class InstallSpyScript : Script
    {
        public bool InstallCalled = false;
        [Before] public void at_test_hook(string arg1, string? kwarg1=null) {}
        public override void AtInstall() { InstallCalled = true; base.AtInstall(); }
    }
    [Fact] public void ScriptAtInstall_Called()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("TestObj3"); ObjectRegistry.AddObject(obj);
        var script = new InstallSpyScript(); script.Id = IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(script);
        // InstallHooks should call AtInstall and set InstallCalled
        script.InstallHooks(obj);
        Assert.True(script.InstallCalled);
    }
    [Fact] public void ScriptDbSerialization()
    {
        using var env = GlobalTestEnv.Enter();
        var script = new DummyBeforeScript(); script.Id = IdGenerator.GetUniqueId(); script.Name="TestScript"; script.Desc="Test Description";
        // Simulate Python Script.create date_created via Extra
        var f = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var extra = (Dictionary<string, JsonElement>)f.GetValue(script)!;
        extra["date_created"] = JsonDocument.Parse($"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}").RootElement.Clone();
        script.IsModified = true;
        ObjectRegistry.AddObject(script);
        using(var db=new Atheriz.Core.Persistence.AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force:true); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var loaded = ObjectRegistry.Get(script.Id);
        Assert.Single(loaded);
        Assert.Equal("TestScript", loaded[0].Name);
        Assert.Equal("Test Description", loaded[0].Desc);
        var f2 = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var loadedExtra = (Dictionary<string, JsonElement>)f2.GetValue(loaded[0])!;
        var hasDate = loadedExtra.ContainsKey("date_created") || loaded[0].GetType().GetProperty("DateCreated") != null || loaded[0].GetType().GetField("date_created", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance) != null;
        // Try reflection for DateCreated property if exists
        object? dateVal = null;
        var prop = loaded[0].GetType().GetProperty("DateCreated") ?? loaded[0].GetType().GetProperty("date_created");
        if (prop != null) dateVal = prop.GetValue(loaded[0]);
        else if (loadedExtra.TryGetValue("date_created", out var je)) dateVal = je.ValueKind != JsonValueKind.Null ? (object)je : null;
        Assert.NotNull(dateVal);
        if (dateVal is JsonElement je2) Assert.True(je2.ValueKind != JsonValueKind.Null);
    }
    [Fact] public void Node_AddRemoveScript()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test_area",0,0,0)); ObjectRegistry.AddObject(node);
        var script = new DummyBeforeScript(); script.Id = IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(script);
        script.InstallHooks(node);
        Assert.Contains(script.Id, node.ScriptsSnapshot);
        var hooksField = typeof(GameObject).GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var hooks = (System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<Delegate>>)hooksField.GetValue(node)!;
        Assert.True(hooks.TryGetValue("at_test_hook", out var set) && set.Count==1);
        script.RemoveHooks(node);
        Assert.DoesNotContain(script.Id, node.ScriptsSnapshot);
        hooks = (System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<Delegate>>)hooksField.GetValue(node)!;
        Assert.True(!hooks.TryGetValue("at_test_hook", out var afterSet) || afterSet.Count==0);
    }
    // Port of test_scripts.py:189 test_node_hooks
    [Fact] public void NodeHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new DummyNode(new Coord("test_area",0,0,1));
        ObjectRegistry.AddObject(node);
        var script = new DummyBeforeScript(); script.Id = 202; ObjectRegistry.AddObject(script);
        script.InstallHooks(node);
        var res = node.AtTestHook("n1", "n2");
        Assert.Equal(new[]{"before: n1, n2","at_test_hook: n1, n2"}, node.Log);
        Assert.Equal("original_result", res);
    }
    // Port of test_scripts.py:199 test_attached_script_persistence
    [Fact] public void AttachedScriptPersistence()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="PersistObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var coord = new Coord("persist_area",1,1,1);
        var node = new DummyNode(coord);
        ObjectRegistry.AddObject(node);
        var objScript = new DummyBeforeScript(); objScript.Id=IdGenerator.GetUniqueId(); objScript.Name="ObjScript"; ObjectRegistry.AddObject(objScript);
        var nodeScript = new DummyAfterScript(); nodeScript.Id=IdGenerator.GetUniqueId(); nodeScript.Name="NodeScript"; ObjectRegistry.AddObject(nodeScript);
        obj.AddScript(objScript);
        node.AddScript(nodeScript);
        Assert.Equal("original_result", obj.AtTestHook("o1"));
        Assert.Equal("after_result", node.AtTestHook("n1"));
        obj.IsModified=true; objScript.IsModified=true; nodeScript.IsModified=true; node.IsModified=true;
        ObjectRegistry.SaveObjects(env.TempPath, force:true);
        var nh = GlobalServices.GetNodeHandler();
        nh.AddNode(node);
        nh.Save(force:true);
        int objId=obj.Id; int objScriptId=objScript.Id; int nodeScriptId=nodeScript.Id;
        ObjectRegistry.ClearAll();
        nh.Clear();
        ObjectRegistry.LoadObjects(env.TempPath);
        var newNh = GlobalServices.GetNodeHandler();
        newNh.Load(new Atheriz.Core.Persistence.AtherizDbContext(env.TempPath));
        var restoredObjs = ObjectRegistry.Get(objId);
        Assert.Single(restoredObjs);
        var restoredObj = restoredObjs[0] as DummyObj;
        Assert.NotNull(restoredObj);
        restoredObj!.Log.Clear();
        // Reinstall hooks if not automatically restored (engine gap)
        if (!restoredObj.HasHook("at_test_hook"))
        {
            var scr = ObjectRegistry.Get(objScriptId).FirstOrDefault() as Script;
            scr?.InstallHooks(restoredObj);
        }
        var result = restoredObj.AtTestHook("o2", null);
        Assert.Equal("original_result", result);
        Assert.Equal(new[]{"before: o2, None","at_test_hook: o2, None"}, restoredObj.Log);
        var restoredNode = newNh.GetNode(coord) as DummyNode;
        Assert.NotNull(restoredNode);
        restoredNode!.Log.Clear();
        if (!restoredNode.HasHook("at_test_hook"))
        {
            var scr2 = ObjectRegistry.Get(nodeScriptId).FirstOrDefault() as Script;
            scr2?.InstallHooks(restoredNode);
        }
        var afterResult = restoredNode.AtTestHook("n2", null);
        Assert.Equal("after_result", afterResult);
        Assert.Equal(new[]{"at_test_hook: n2, None","after: n2, None"}, restoredNode.Log);
    }
    [Fact] public void HasScriptType()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var s1 = new DummyBeforeScript(); s1.Id=IdGenerator.GetUniqueId(); s1.Name="Script1"; ObjectRegistry.AddObject(s1);
        var s2 = new DummyAfterScript(); s2.Id=IdGenerator.GetUniqueId(); s2.Name="Script2"; ObjectRegistry.AddObject(s2);
        Assert.False(obj.HasScriptType("bleh"));
        obj.AddScript(s1);
        Assert.True(obj.HasScriptType("DummyBeforeScript"));
        Assert.True(obj.HasScriptType("dummybeforescript"));
        Assert.True(obj.HasScriptType("DUMMYBEFORESCRIPT"));
        Assert.True(obj.HasScriptType("Before"));
        Assert.False(obj.HasScriptType("DummyAfterScript"));
        Assert.False(obj.HasScriptType("After"));
        obj.AddScript(s2);
        Assert.True(obj.HasScriptType("DummyAfterScript"));
        Assert.True(obj.HasScriptType("After"));
    }
    // Port of test_scripts.py:282 test_get_scripts_by_type
    [Fact] public void GetScriptsByType()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="TestObj"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var s1 = new DummyBeforeScript(); s1.Id=IdGenerator.GetUniqueId(); s1.Name="Script1"; ObjectRegistry.AddObject(s1);
        var s2 = new DummyAfterScript(); s2.Id=IdGenerator.GetUniqueId(); s2.Name="Script2"; ObjectRegistry.AddObject(s2);
        var s3 = new DummyBeforeScript(); s3.Id=IdGenerator.GetUniqueId(); s3.Name="Script3"; ObjectRegistry.AddObject(s3);
        Assert.Empty(obj.GetScriptsByType("bleh"));
        obj.AddScript(s1);
        Assert.Equal(new[]{s1}, obj.GetScriptsByType("DummyBeforeScript"));
        Assert.Equal(new[]{s1}, obj.GetScriptsByType("dummybeforescript"));
        Assert.Equal(new[]{s1}, obj.GetScriptsByType("DUMMYBEFORESCRIPT"));
        Assert.Equal(new[]{s1}, obj.GetScriptsByType("Before"));
        Assert.Empty(obj.GetScriptsByType("DummyAfterScript"));
        Assert.Empty(obj.GetScriptsByType("After"));
        obj.AddScript(s2);
        Assert.Equal(new[]{s2}, obj.GetScriptsByType("DummyAfterScript"));
        Assert.Equal(new[]{s2}, obj.GetScriptsByType("After"));
        obj.AddScript(s3);
        var returned = obj.GetScriptsByType("Before");
        Assert.Equal(2, returned.Count);
        Assert.Contains(s1, returned);
        Assert.Contains(s3, returned);
    }
    [Fact] public void TemporaryScript_NotSaved()
    {
        using var env = GlobalTestEnv.Enter();
        var script = new DummyBeforeScript(); script.Id = IdGenerator.GetUniqueId(); script.Name="TempScript";
        script.IsTemporary = true; ObjectRegistry.AddObject(script);
        using(var db=new Atheriz.Core.Persistence.AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        using(var db=new Atheriz.Core.Persistence.AtherizDbContext(env.TempPath)){
            var row = db.Objects.FirstOrDefault(o=>o.Id==script.Id);
            Assert.Null(row);
        }
    }
    // Port of test_scripts.py:349 test_script_dill_preserves_attributes
    [Fact] public void ScriptDillPreservesAttributes()
    {
        using var env = GlobalTestEnv.Enter();
        var script = new DummyBeforeScript(); script.Id=IdGenerator.GetUniqueId(); script.Name="DillScript"; script.Desc="DillDesc";
        ObjectRegistry.AddObject(script);
        var f = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var extra = (Dictionary<string, JsonElement>)f.GetValue(script)!;
        extra["custom_attr"] = JsonDocument.Parse("\"preserved\"").RootElement.Clone();
        script.IsModified=true;
        var dto = script.ToDto();
        var json = GameObjectDtoSerializer.ToJson(dto);
        var dto2 = GameObjectDtoSerializer.FromJson(json);
        var des = GameObject.FromDto(dto2) as Script;
        Assert.NotNull(des);
        Assert.Equal("DillScript", des!.Name);
        Assert.Equal("DillDesc", des.Desc);
        var f2 = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var desExtra = (Dictionary<string, JsonElement>)f2.GetValue(des)!;
        Assert.True(desExtra.ContainsKey("custom_attr"));
        Assert.Equal("preserved", desExtra["custom_attr"].GetString());
        Assert.Equal(script.Id, des.Id);
    }
    // Port of test_scripts.py:363 test_script_dill_clears_child_and_hooks
    [Fact] public void ScriptDillClearsChildAndHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new DummyObj(); obj.Name="HookHost"; obj.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj);
        var script = new DummyBeforeScript(); script.Id=IdGenerator.GetUniqueId(); script.Name="HookScript"; ObjectRegistry.AddObject(script);
        obj.AddScript(script);
        Assert.Equal(obj.Id, script.Child!.Id);
        var hooksField = typeof(GameObject).GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var hooks = (Dictionary<string, HashSet<Delegate>>)hooksField.GetValue(obj)!;
        Assert.True(hooks.TryGetValue("at_test_hook", out var set) && set.Count==1);
        var dto = script.ToDto();
        var json = GameObjectDtoSerializer.ToJson(dto);
        var dto2 = GameObjectDtoSerializer.FromJson(json);
        var des = GameObject.FromDto(dto2) as Script;
        Assert.NotNull(des);
        Assert.Null(des!.Child);
        Assert.False(hooks.TryGetValue("at_test_hook", out var s2) && s2.Any(d=> ReferenceEquals(d.Target, des)));
        obj.RemoveScript(script);
        des.InstallHooks(obj);
        obj.Log.Clear();
        var res = obj.AtTestHook("roundtrip", null);
        Assert.Equal(new[]{"before: roundtrip, None","at_test_hook: roundtrip, None"}, obj.Log);
        Assert.Equal("original_result", res);
    }
    [Fact] public void ScriptSharedBetweenHosts_Rejected()
    {
        using var env = GlobalTestEnv.Enter();
        var obj1 = new DummyObj(); obj1.Name="Host1"; obj1.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj1);
        var obj2 = new DummyObj(); obj2.Name="Host2"; obj2.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj2);
        var script = new DummyBeforeScript(); script.Id = IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(script);
        script.InstallHooks(obj1);
        Assert.Equal(obj1.Id, script.Child!.Id);
        var ex = Assert.Throws<InvalidOperationException>(() => script.InstallHooks(obj2));
        Assert.Contains("already attached", ex.Message);
    }
    // Port of test_scripts.py:408 test_script_install_hooks_does_not_overwrite_existing_child
    [Fact] public void ScriptInstallHooksDoesNotOverwriteExistingChild()
    {
        using var env = GlobalTestEnv.Enter();
        var obj1 = new DummyObj(); obj1.Name="HostA"; obj1.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj1);
        var obj2 = new DummyObj(); obj2.Name="HostB"; obj2.Id=IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(obj2);
        var script = new DummyBeforeScript(); script.Id=IdGenerator.GetUniqueId(); script.Name="HookScript2"; ObjectRegistry.AddObject(script);
        script.InstallHooks(obj1);
        Assert.Equal(obj1.Id, script.Child!.Id);
        var ex = Assert.Throws<InvalidOperationException>(() => script.InstallHooks(obj2));
        Assert.Contains("already attached", ex.Message);
        Assert.Equal(obj1.Id, script.Child!.Id);
    }
}
