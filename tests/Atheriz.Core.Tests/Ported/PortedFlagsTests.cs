using Atheriz.Core.Objects;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

// Port of atheriz/tests/test_flags.py
[Collection("Ported")]
public class PortedFlagsTests
{
    private static GameObject NewFlags()
    {
        var o = new GameObject();
        return o;
    }

    // Port of test_flags.py:35 TestFlagsConstructor.test_all_boolean_flags_default_false
    [Fact] public void AllBooleanFlagsDefaultFalse()
    {
        var o = NewFlags();
        Assert.False(o.IsPc);
        Assert.False(o.IsNpc);
        Assert.False(o.IsItem);
        Assert.False(o.IsMapable);
        Assert.False(o.IsContainer);
        Assert.False(o.IsScript);
        Assert.False(o.IsAccount);
        Assert.False(o.IsChannel);
        Assert.False(o.IsNode);
        Assert.False(o.IsDeleted);
        Assert.False(o.IsConnected);
        Assert.False(o.IsTemporary);
        Assert.False(o.CanHear);
    }
    // Port of test_flags.py:51 test_is_modified_default_true
    [Fact] public void IsModifiedDefaultTrue() => Assert.True(NewFlags().IsModified);
    // Port of test_flags.py:56 test_tags_default_empty_set
    [Fact] public void TagsDefaultEmptySet()
    {
        var o = NewFlags();
        Assert.Empty(o.TagsSnapshot);
        Assert.IsType<HashSet<string>>(o.TagsSnapshot);
    }
    // Port of test_flags.py:60 test_is_tickable_uses_underscore_attr
    [Fact] public void IsTickableUsesUnderlying()
    {
        var o = NewFlags();
        Assert.False(o.IsTickable);
        o.IsTickable = true;
        Assert.True(o.IsTickable);
    }
    // Port of test_flags.py:68 test_is_tickable_is_property — original asserts isinstance(Flags.is_tickable, property) (read-only descriptor)
    // C# IsTickable is readable+ writable (o.IsTickable = true) via SetFlag, unlike Python read-only property that uses _is_tickable field
    // Keep test but document divergence: ensure property exists and is readable, not necessarily read-only
    [Fact] public void IsTickableIsProperty()
    {
        var prop = typeof(GameObject).GetProperty("IsTickable");
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        // Note: Python is read-only; C# is writable by design — no Assert.False(CanWrite) intended
    }

    // Port of test_flags.py:73 add_tag
    [Fact] public void AddStringTag() { var o=NewFlags(); o.AddTag("combat"); Assert.Contains("combat", o.TagsSnapshot); }
    [Fact] public void AddListOfTags() { var o=NewFlags(); o.AddTags(new[]{"a","b","c"}); Assert.Equal(new HashSet<string>{"a","b","c"}, o.TagsSnapshot); }
    [Fact] public void AddSetOfTags() { var o=NewFlags(); o.AddTags(new HashSet<string>{"x","y"}); Assert.Equal(new HashSet<string>{"x","y"}, o.TagsSnapshot); }
    [Fact] public void AddIdempotent() { var o=NewFlags(); o.AddTag("foo"); o.AddTag("foo"); Assert.Single(o.TagsSnapshot); Assert.Contains("foo", o.TagsSnapshot); }
    [Fact] public void AddSetsIsModified() { var o=NewFlags(); o.IsModified=false; o.AddTag("trigger"); Assert.True(o.IsModified); }
    [Fact] public void AddEmptyString() { var o=NewFlags(); o.AddTag(""); Assert.Contains("", o.TagsSnapshot); }
    [Fact] public void AddOverlappingListAndString() { var o=NewFlags(); o.AddTag("a"); o.AddTags(new[]{"a","b"}); Assert.Equal(new HashSet<string>{"a","b"}, o.TagsSnapshot); }

    // Port of test_flags.py:131 remove
    [Fact] public void RemoveExistingTag() { var o=NewFlags(); o.AddTag("present"); o.RemoveTag("present"); Assert.DoesNotContain("present", o.TagsSnapshot); }
    [Fact] public void RemoveMissingTagSilent() { var o=NewFlags(); o.RemoveTag("never-added"); Assert.Empty(o.TagsSnapshot); }
    [Fact] public void RemoveString() { var o=NewFlags(); o.AddTags(new[]{"a","b"}); o.RemoveTag("a"); Assert.Equal(new HashSet<string>{"b"}, o.TagsSnapshot); }
    [Fact] public void RemoveListOfTags() { var o=NewFlags(); o.AddTags(new[]{"a","b","c","d"}); o.RemoveTags(new[]{"a","c"}); Assert.Equal(new HashSet<string>{"b","d"}, o.TagsSnapshot); }
    [Fact] public void RemoveSetOfTags() { var o=NewFlags(); o.AddTags(new HashSet<string>{"a","b","c"}); o.RemoveTags(new HashSet<string>{"a","b"}); Assert.Equal(new HashSet<string>{"c"}, o.TagsSnapshot); }
    [Fact] public void RemoveMissingInList() { var o=NewFlags(); o.AddTag("a"); o.RemoveTags(new[]{"a","missing"}); Assert.Empty(o.TagsSnapshot); }
    [Fact] public void RemoveSetsIsModified() { var o=NewFlags(); o.AddTag("x"); o.IsModified=false; o.RemoveTag("x"); Assert.True(o.IsModified); }
    [Fact] public void RemoveEmptySet() { var o=NewFlags(); o.AddTag("a"); o.RemoveTags(new HashSet<string>()); Assert.Contains("a", o.TagsSnapshot); }

    // Port of test_flags.py:171 has_tag
    [Fact] public void HasSingleTagPresent() { var o=NewFlags(); o.AddTag("a"); Assert.True(o.HasTag("a")); }
    [Fact] public void HasSingleTagAbsent() { var o=NewFlags(); Assert.False(o.HasTag("missing")); }
    [Fact] public void HasListAnyMatch() { var o=NewFlags(); o.AddTags(new[]{"a","b"}); Assert.True(o.HasTags(new[]{"a","c","d"})); Assert.False(o.HasTags(new[]{"x","y","z"})); }
    [Fact] public void HasListAllMatch() { var o=NewFlags(); o.AddTags(new[]{"a","b"}); Assert.True(o.HasTags(new[]{"a","b"}, all:true)); Assert.False(o.HasTags(new[]{"a","c"}, all:true)); }
    [Fact] public void HasSetWithAll() { var o=NewFlags(); o.AddTags(new HashSet<string>{"a","b","c"}); Assert.True(o.HasTags(new HashSet<string>{"a","b"}, all:true)); Assert.False(o.HasTags(new HashSet<string>{"a","z"}, all:true)); }
    [Fact] public void HasEmptyListAny() { var o=NewFlags(); Assert.False(o.HasTags(Array.Empty<string>())); }
    [Fact] public void HasEmptyListAll() { var o=NewFlags(); Assert.True(o.HasTags(Array.Empty<string>(), all:true)); }
    [Fact] public void HasTagReturnsBool() { var o=NewFlags(); var r=o.HasTag("a"); Assert.IsType<bool>(r); }
    [Fact] public void HasTagSetWithAny() { var o=NewFlags(); o.AddTag("a"); Assert.True(o.HasTags(new HashSet<string>{"a","b"})); }

    // Port of test_flags.py:219 thread safety
    [Fact] public void ConcurrentAddTags()
    {
        var o=NewFlags();
        var errs=new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var threads=Enumerable.Range(0,50).Select(i=>new System.Threading.Thread(()=>{ try{o.AddTag($"t{i}");}catch(Exception ex){errs.Add(ex);} } )).ToList();
        threads.ForEach(t=>t.Start()); threads.ForEach(t=>t.Join());
        Assert.Empty(errs);
        Assert.Equal(50, o.TagsSnapshot.Count);
    }
    [Fact] public void ConcurrentAddAndRemove()
    {
        var o=NewFlags();
        o.AddTags(new[]{"a","b","c","d","e"});
        var errs=new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var t1=new System.Threading.Thread(()=>{ try{ for(int i=0;i<20;i++) o.AddTag($"x{i}");}catch(Exception ex){errs.Add(ex);} });
        var t2=new System.Threading.Thread(()=>{ try{ for(int i=0;i<20;i++) o.RemoveTags(new[]{"a","b","c"});}catch(Exception ex){errs.Add(ex);} });
        t1.Start(); t2.Start(); t1.Join(); t2.Join();
        Assert.Empty(errs);
    }

    // Port of test_flags.py:266 integration
    [Fact] public void AddRemoveCycle() { var o=NewFlags(); o.AddTag("temp"); Assert.True(o.HasTag("temp")); o.RemoveTag("temp"); Assert.False(o.HasTag("temp")); }
    [Fact] public void ReAddAfterRemove() { var o=NewFlags(); o.AddTag("x"); o.RemoveTag("x"); o.AddTag("x"); Assert.True(o.HasTag("x")); }
    [Fact] public void ModifyFlagWorks() { var o=NewFlags(); o.IsPc=true; Assert.True(o.IsPc); o.IsPc=false; Assert.False(o.IsPc); }
    [Fact] public void SubclassCanInitialize()
    {
        var c=new GameObject(); c.IsPc=true;
        Assert.True(c.IsPc);
        Assert.False(c.IsNpc);
        Assert.True(c.IsModified);
    }
    // Port of test_flags.py:107 add_uses_lock — original spies SpyLock.__enter__ called
    // Translated: verify that GameObject lock was acquired during AddTag via instrumented counting LockWrapper
    // In C# Write() calls _lock.EnterWriteLock via IncrementTracker if _testTracker set; we instrument via reflection
    [Fact] public void AddUsesLock()
    {
        var o=NewFlags();
        // Instrument: create tracker object with public int Entries field and inject via reflection so Write() increments it
        var tracker = new LockCountTracker();
        var trackerField = typeof(GameObject).GetField("_testTracker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var entriesField = typeof(LockCountTracker).GetField(nameof(LockCountTracker.Entries));
        trackerField!.SetValue(o, tracker);
        typeof(GameObject).GetField("_trackerEntriesField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(o, entriesField);
        // Now AddTag should increment tracker.Entries via IncrementTracker()
        o.AddTag("x");
        Assert.Contains("x", o.TagsSnapshot);
        // Verify lock was used (EnterWriteLock tracked) — faithful to SpyLock.__enter__ called
        Assert.True(tracker.Entries > 0);
    }
    private sealed class LockCountTracker { public int Entries = 0; }
}
