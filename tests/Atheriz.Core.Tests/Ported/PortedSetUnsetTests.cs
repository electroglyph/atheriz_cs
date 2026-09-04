// Port of atheriz/tests/test_set_unset.py:1 — faithful 18 tests (TestSetCommand 14 + TestUnsetCommand 4)
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSetUnsetTests
{
    private static GameObject MakeCaller(string name="Alice", bool builder=false) => PortedHelpers.MakeCaller(name, builder);

    private static Node MakeRoom(Coord? coord=null)
    {
        coord ??= new Coord("test",0,0,0);
        var r = new Node(coord.Value, desc:"A test room.", symbol:"#");
        ObjectRegistry.AddObject(r);
        return r;
    }

    private sealed class TestCaller : GameObject
    {
        public List<GameObject>? SearchResult;
        public TestCaller(string n) { Name = n; IsPc = true; }
        public override List<GameObject> Search(string q, bool rec=true, GameObject? looker=null)
        {
            if (SearchResult != null) return SearchResult;
            return base.Search(q, rec, looker);
        }
    }

    private static GameArgumentParser.ParsedArgs MakeSetArgs(string target, string attribute, string value)
    {
        var pa = new GameArgumentParser.ParsedArgs();
        pa["target"]=target;
        pa["attribute"]=attribute;
        pa["value"]=value;
        return pa;
    }
    private static GameArgumentParser.ParsedArgs MakeUnsetArgs(string target, string attribute)
    {
        var pa = new GameArgumentParser.ParsedArgs();
        pa["target"]=target;
        pa["attribute"]=attribute;
        return pa;
    }

    // ----- TestSetCommand -----
    // test_set_unset.py:35 test_access_requires_builder
    [Fact] public void SetAccessRequiresBuilder() // test_set_unset.py:35
    {
        var c = MakeCaller(builder:false);
        Assert.False(new SetCommand().Access(c));
    }
    // test_set_unset.py:39 test_access_allowed_for_builder
    [Fact] public void SetAccessAllowedForBuilder() // test_set_unset.py:39
    {
        var c = MakeCaller(builder:true);
        Assert.True(new SetCommand().Access(c));
    }
    // test_set_unset.py:43 test_no_args_shows_help
    [Fact] public void SetNoArgsShowsHelp() // test_set_unset.py:43
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        new SetCommand().Run(c, null);
        Assert.Single(c.PeekMessages());
    }
    // test_set_unset.py:48 test_target_me
    [Fact] public void SetTargetMe() // test_set_unset.py:48
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeSetArgs("me","my_attr","42");
        new SetCommand().Run(c, args);
        // verify attribute set to 42 (int)
        Assert.True(SetHelper.HasAttr(c, "my_attr"));
        // retrieve via extra or property
        var f = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict = f?.GetValue(c) as Dictionary<string, System.Text.Json.JsonElement>;
        Assert.NotNull(dict);
        Assert.True(dict!.ContainsKey("my_attr"));
        var val = dict["my_attr"];
        Assert.Equal(System.Text.Json.JsonValueKind.Number, val.ValueKind);
        Assert.Equal(42, val.GetInt32());
    }
    // test_set_unset.py:54 test_target_here
    [Fact] public void SetTargetHere() // test_set_unset.py:54
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var room = MakeRoom();
        c.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord);
        var args = MakeSetArgs("here","my_attr","'hello'");
        new SetCommand().Run(c, args);
        Assert.True(SetHelper.HasAttr(room, "my_attr"));
        var f = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict = f?.GetValue(room) as Dictionary<string, System.Text.Json.JsonElement>;
        Assert.NotNull(dict);
        Assert.Equal("hello", dict!["my_attr"].GetString());
    }
    // test_set_unset.py:62 test_target_by_id
    [Fact] public void SetTargetById() // test_set_unset.py:62
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var target = GameObject.Create("Target");
        // need to add with specific id 999 — manipulate Id after creation
        // Remove old id entry and set new
        ObjectRegistry.RemoveObject(target);
        target.Id = 999;
        ObjectRegistry.AddObject(target);
        var args = MakeSetArgs("#999","x","1");
        new SetCommand().Run(c, args);
        Assert.True(SetHelper.HasAttr(target, "x"));
        var f = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict = f?.GetValue(target) as Dictionary<string, System.Text.Json.JsonElement>;
        Assert.Equal(1, dict!["x"].GetInt32());
    }
    // test_set_unset.py:71 test_target_by_id_invalid_format
    [Fact] public void SetTargetByIdInvalidFormat() // test_set_unset.py:71
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeSetArgs("#abc","x","1");
        new SetCommand().Run(c, args);
        Assert.Contains(c.PeekMessages(), m=>m == "Invalid ID format. Use #<number>.");
    }
    // test_set_unset.py:77 test_target_by_id_not_found
    [Fact] public void SetTargetByIdNotFound() // test_set_unset.py:77
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeSetArgs("#99999","x","1");
        new SetCommand().Run(c, args);
        Assert.Contains(c.PeekMessages(), m=>m == "No object found with ID 99999.");
    }
    // test_set_unset.py:83 test_target_not_found
    [Fact] public void SetTargetNotFound() // test_set_unset.py:83
    {
        using var env = GlobalTestEnv.Enter();
        var c = new TestCaller("Alice");
        c.PrivilegeLevel = Privilege.Builder;
        c.Quelled = false;
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        c.SearchResult = new List<GameObject>(); // empty
        var args = MakeSetArgs("missing","x","1");
        new SetCommand().Run(c, args);
        Assert.Contains(c.PeekMessages(), m=>m == "No match found for 'missing'.");
    }
    // test_set_unset.py:90 test_target_multiple_matches
    [Fact] public void SetTargetMultipleMatches() // test_set_unset.py:90
    {
        using var env = GlobalTestEnv.Enter();
        var c = new TestCaller("Alice");
        c.PrivilegeLevel = Privilege.Builder;
        c.Quelled = false;
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        var a = GameObject.Create("A"); ObjectRegistry.AddObject(a);
        var b = GameObject.Create("B"); ObjectRegistry.AddObject(b);
        c.SearchResult = new List<GameObject>{a,b};
        var args = MakeSetArgs("x","y","1");
        new SetCommand().Run(c, args);
        Assert.Contains(c.PeekMessages(), m=>m.Contains("Multiple matches"));
    }
    // test_set_unset.py:97 test_falls_back_to_plain_string
    [Fact] public void SetFallsBackToPlainString() // test_set_unset.py:97
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeSetArgs("me","note","hello world");
        new SetCommand().Run(c, args);
        Assert.True(SetHelper.HasAttr(c, "note"));
        var f = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict = f?.GetValue(c) as Dictionary<string, System.Text.Json.JsonElement>;
        Assert.Equal("hello world", dict!["note"].GetString());
    }
    // test_set_unset.py:103 test_warns_for_new_attribute
    [Fact] public void SetWarnsForNewAttribute() // test_set_unset.py:103
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeSetArgs("me","brand_new","1");
        new SetCommand().Run(c, args);
        Assert.Contains(c.PeekMessages(), m=>m.Contains("new attribute"));
    }
    // test_set_unset.py:109 test_cannot_escalate_privilege_via_set
    [Fact] public void SetCannotEscalatePrivilegeViaSet() // test_set_unset.py:109
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeSetArgs("me","privilege_level","5");
        new SetCommand().Run(c, args);
        Assert.Equal(Privilege.Builder, c.PrivilegeLevel);
        Assert.False(c.IsSuperUser);
    }
    // test_set_unset.py:116 test_cannot_overwrite_lock_via_set
    [Fact] public void SetCannotOverwriteLockViaSet() // test_set_unset.py:116
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var origLock = c.SyncRoot;
        var args = MakeSetArgs("me","lock","garbage");
        new SetCommand().Run(c, args);
        Assert.Same(origLock, c.SyncRoot);
        Assert.Equal("Alice", c.Name);
    }

    // ----- TestUnsetCommand -----
    // test_set_unset.py:126 test_access_requires_builder
    [Fact] public void UnsetAccessRequiresBuilder() // test_set_unset.py:126
    {
        var c = MakeCaller(builder:false);
        Assert.False(new UnsetCommand().Access(c));
    }
    // test_set_unset.py:130 test_deletes_existing_attr
    [Fact] public void UnsetDeletesExistingAttr() // test_set_unset.py:130
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        SetHelper.SetAttr(c, "foo", 1);
        Assert.True(SetHelper.HasAttr(c, "foo"));
        var args = MakeUnsetArgs("me","foo");
        new UnsetCommand().Run(c, args);
        Assert.False(SetHelper.HasAttr(c, "foo"));
    }
    // test_set_unset.py:137 test_missing_attr_msg
    [Fact] public void UnsetMissingAttrMsg() // test_set_unset.py:137
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeUnsetArgs("me","nope");
        new UnsetCommand().Run(c, args);
        Assert.Contains(c.PeekMessages(), m=>m == "Alice has no attribute 'nope'.");
    }
    // test_set_unset.py:143 test_unset_cannot_remove_lock
    [Fact] public void UnsetCannotRemoveLock() // test_set_unset.py:143
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder:true);
        var args = MakeUnsetArgs("me","lock");
        new UnsetCommand().Run(c, args);
        Assert.True(SetHelper.HasAttr(c, "lock") || c.SyncRoot != null); // lock still exists
        Assert.Equal("Alice", c.Name);
        // Also ensure HasAttr for lock via SyncRoot considered protected still present
        // Alternative: check that lock object still same
        Assert.NotNull(c.SyncRoot);
    }
}
