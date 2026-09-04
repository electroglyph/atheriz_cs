using Atheriz.Core;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

// Port of atheriz/tests/test_locks.py
[Collection("Ported")]
public class PortedLocksTests
{
    private static GameObject Mock() => new GameObject();

    // Port of test_locks.py:18 test_add_lock_creates_lock_list
    [Fact] public void AddLockCreatesLockList()
    {
        var obj=Mock();
        obj.AddLock("control", x=>x.IsBuilder);
        Assert.True(obj.HasLockName("control")); // helper via reflection check
        Assert.Single(obj.GetLocks("control"));
    }
    // Port of test_locks.py:27 test_add_lock_appends_to_existing
    [Fact] public void AddLockAppendsToExisting()
    {
        var obj=Mock();
        obj.AddLock("control", x=>x.IsBuilder);
        obj.AddLock("control", x=>x.IsSuperUser);
        Assert.Equal(2, obj.GetLocks("control").Count);
    }
    // Port of test_locks.py:37 test_add_lock_multiple_names
    [Fact] public void AddLockMultipleNames()
    {
        var obj=Mock();
        obj.AddLock("control", _=>true);
        obj.AddLock("view", _=>true);
        obj.AddLock("edit", _=>true);
        Assert.True(obj.HasLockName("control"));
        Assert.True(obj.HasLockName("view"));
        Assert.True(obj.HasLockName("edit"));
    }
    // Port of test_locks.py:52 test_clear_locks_by_name_removes_lock
    [Fact] public void ClearLocksByNameRemovesLock()
    {
        var obj=Mock();
        obj.AddLock("control", _=>true);
        obj.AddLock("view", _=>true);
        obj.ClearLocksByName("control");
        Assert.False(obj.HasLockName("control"));
        Assert.True(obj.HasLockName("view"));
    }
    // Port of test_locks.py:64 test_clear_locks_by_name_nonexistent
    [Fact] public void ClearLocksByNameNonexistent()
    {
        var obj=Mock();
        obj.AddLock("control", _=>true);
        obj.ClearLocksByName("nonexistent");
        Assert.True(obj.HasLockName("control"));
    }
    // Port of test_locks.py:74 test_clear_locks_by_name_empty_locks
    [Fact] public void ClearLocksByNameEmptyLocks()
    {
        var obj=Mock();
        obj.ClearLocksByName("anything");
        Assert.Empty(obj.GetAllLockNames());
    }
    // Port of test_locks.py:88 test_access_superuser_bypasses_locks
    [Fact] public void AccessSuperuserBypassesLocks()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Admin; accessor.Quelled=false;
        obj.AddLock("control", _=>false);
        Assert.True(obj.Access(accessor, "control"));
    }
    // Port of test_locks.py:102 test_access_passes_when_no_locks
    [Fact] public void AccessPassesWhenNoLocks()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Player; accessor.Quelled=false;
        Assert.True(obj.Access(accessor, "nonexistent"));
    }
    // Port of test_locks.py:112 test_access_passes_when_all_locks_pass
    [Fact] public void AccessPassesWhenAllLocksPass()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Builder; accessor.Quelled=false;
        obj.AddLock("control", x=>x.IsBuilder);
        obj.AddLock("control", _=>true);
        Assert.True(obj.Access(accessor, "control"));
    }
    // Port of test_locks.py:125 test_access_fails_when_any_lock_fails
    [Fact] public void AccessFailsWhenAnyLockFails()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Player; accessor.Quelled=false;
        obj.AddLock("control", _=>true);
        obj.AddLock("control", x=>x.IsBuilder);
        Assert.False(obj.Access(accessor, "control"));
    }
    // Port of test_locks.py:138 test_access_with_single_failing_lock
    [Fact] public void AccessWithSingleFailingLock()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Player; accessor.Quelled=false;
        obj.AddLock("view", _=>false);
        Assert.False(obj.Access(accessor, "view"));
    }
    // Port of test_locks.py:150 test_access_with_single_passing_lock
    [Fact] public void AccessWithSinglePassingLock()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Player; accessor.Quelled=false;
        obj.AddLock("view", _=>true);
        Assert.True(obj.Access(accessor, "view"));
    }
    // Port of test_locks.py:162 test_access_checks_correct_lock_name
    [Fact] public void AccessChecksCorrectLockName()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Player; accessor.Quelled=false;
        obj.AddLock("control", _=>false);
        obj.AddLock("view", _=>true);
        Assert.True(obj.Access(accessor, "view"));
        Assert.False(obj.Access(accessor, "control"));
    }
    // Port of test_locks.py:176 test_access_behavior
    [Fact] public void AccessBehavior()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Player; accessor.Quelled=false;
        obj.AddLock("test", _=>true);
        Assert.True(obj.Access(accessor, "test"));
        obj.AddLock("test", _=>false);
        Assert.False(obj.Access(accessor, "test"));
    }
    // Port of test_locks.py:192 test_quelled_superuser
    [Fact] public void QuelledSuperuser()
    {
        var obj=Mock();
        var accessor=Mock(); accessor.PrivilegeLevel=Privilege.Admin; accessor.Quelled=true;
        obj.AddLock("control", x=>x.IsSuperUser);
        Assert.False(obj.Access(accessor, "control"));
    }
}

// Helpers to introspect GameObject locks for testing (mirrors Python's obj.locks dict)
internal static class LockTestExtensions
{
    public static List<Func<GameObject,bool>> GetLocks(this GameObject obj, string name)
    {
        var f=typeof(GameObject).GetField("_locks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict=(System.Collections.Generic.Dictionary<string, List<Func<GameObject,bool>>>?)f!.GetValue(obj);
        if(dict!=null && dict.TryGetValue(name, out var lst)) return new List<Func<GameObject,bool>>(lst);
        return new List<Func<GameObject,bool>>();
    }
    public static bool HasLockName(this GameObject obj, string name)
    {
        var f=typeof(GameObject).GetField("_locks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict=(System.Collections.Generic.Dictionary<string, List<Func<GameObject,bool>>>?)f!.GetValue(obj);
        return dict!=null && dict.ContainsKey(name);
    }
    public static List<string> GetAllLockNames(this GameObject obj)
    {
        var f=typeof(GameObject).GetField("_locks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var dict=(System.Collections.Generic.Dictionary<string, List<Func<GameObject,bool>>>?)f!.GetValue(obj);
        return dict!=null ? dict.Keys.ToList() : new List<string>();
    }
}
