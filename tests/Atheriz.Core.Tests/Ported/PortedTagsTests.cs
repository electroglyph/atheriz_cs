using Atheriz.Core.Objects;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

// Port of atheriz/tests/test_tags.py
[Collection("Ported")]
public class PortedTagsTests
{
    private static readonly string[] EntityTypes = ["object","account","channel","node","script"];
    private static GameObject MakeInstance(string entity, int uniqueX=0)
    {
        return entity switch
        {
            "object" => new GameObject(),
            "account" => new Account(),
            "channel" => new Channel(),
            "node" => new Node(new Coord("test", uniqueX, 0, 0)),
            "script" => new Script(),
            _ => throw new ArgumentException(entity)
        };
    }
    private static GameObject MakeObj(int id, HashSet<string> tags, string entity="object")
    {
        var obj=MakeInstance(entity, uniqueX:id);
        obj.Id=id;
        // set tags directly via AddTags
        foreach(var t in tags) obj.AddTag(t);
        ObjectRegistry.AddObject(obj);
        return obj;
    }

    // Port of test_tags.py:54 serialization
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void TagsSurviveSerialization(string entity)
    {
        var obj=MakeInstance(entity, 1);
        obj.Id=1;
        obj.AddTags(new HashSet<string>{"warrior","hero"});
        var dto=obj.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        var restoredDto=GameObjectDtoSerializer.FromJson(json);
        var restored=GameObject.FromDto(restoredDto);
        Assert.Equal(new HashSet<string>{"warrior","hero"}, restored.TagsSnapshot);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void EmptyTagsSurviveSerialization(string entity)
    {
        var obj=MakeInstance(entity, 2);
        obj.Id=2;
        var dto=obj.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        var restored=GameObject.FromDto(GameObjectDtoSerializer.FromJson(json));
        Assert.Empty(restored.TagsSnapshot);
        Assert.IsType<HashSet<string>>(restored.TagsSnapshot);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void TagsDefaultIsEmptySet(string entity)
    {
        var obj=MakeInstance(entity);
        Assert.Empty(obj.TagsSnapshot);
        Assert.IsType<HashSet<string>>(obj.TagsSnapshot);
    }

    // Port of test_tags.py:94 get_by_tag single
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagSingleMatch(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagSingleMatch));
        var obj=MakeObj(10, new HashSet<string>{"villain"}, entity);
        var result=ObjectRegistry.GetByTag("villain");
        Assert.Contains(obj, result);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagSingleNoMatch(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagSingleNoMatch));
        MakeObj(11, new HashSet<string>{"hero"}, entity);
        var result=ObjectRegistry.GetByTag("villain");
        Assert.Empty(result);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagSingleMultipleObjects(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagSingleMultipleObjects));
        var a=MakeObj(12, new HashSet<string>{"knight"}, entity);
        var b=MakeObj(13, new HashSet<string>{"knight","mage"}, entity);
        var c=MakeObj(14, new HashSet<string>{"mage"}, entity);
        var result=ObjectRegistry.GetByTag("knight");
        Assert.Contains(a, result);
        Assert.Contains(b, result);
        Assert.DoesNotContain(c, result);
    }
    // Port of test_tags.py:122 list any match
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagListAnyMatch(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagListAnyMatch));
        var a=MakeObj(20, new HashSet<string>{"warrior"}, entity);
        var b=MakeObj(21, new HashSet<string>{"mage"}, entity);
        var c=MakeObj(22, new HashSet<string>{"bard"}, entity);
        var result=ObjectRegistry.GetByTag(new[]{"warrior","mage"});
        Assert.Contains(a, result);
        Assert.Contains(b, result);
        Assert.DoesNotContain(c, result);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagListNoMatch(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagListNoMatch));
        MakeObj(23, new HashSet<string>{"rogue"}, entity);
        var result=ObjectRegistry.GetByTag(new[]{"warrior","mage"});
        Assert.Empty(result);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagListOverlap(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagListOverlap));
        var obj=MakeObj(24, new HashSet<string>{"warrior","mage"}, entity);
        var result=ObjectRegistry.GetByTag(new[]{"warrior","mage"});
        Assert.Equal(1, result.Count(x=>ReferenceEquals(x,obj)));
    }
    [Fact] public void GetByTagEmptyList()
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagEmptyList));
        MakeObj(25, new HashSet<string>{"warrior"});
        var result=ObjectRegistry.GetByTag(Array.Empty<string>());
        Assert.Empty(result);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void GetByTagAll(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagAll));
        var a=MakeObj(26, new HashSet<string>{"warrior","hero"}, entity);
        var b=MakeObj(27, new HashSet<string>{"warrior"}, entity);
        var c=MakeObj(28, new HashSet<string>{"mage"}, entity);
        var result=ObjectRegistry.GetByTag(new[]{"warrior","hero"}, all:true);
        Assert.Contains(a, result); Assert.DoesNotContain(b, result); Assert.DoesNotContain(c, result);
        result=ObjectRegistry.GetByTag(new[]{"warrior"}, all:true);
        Assert.Contains(a, result); Assert.Contains(b, result); Assert.DoesNotContain(c, result);
        result=ObjectRegistry.GetByTag(Array.Empty<string>(), all:true);
        Assert.Contains(a, result); Assert.Contains(b, result); Assert.Contains(c, result);
    }
    // Port of test_tags.py:187 missing tags attr — faithful to object.__delattr__(obj,"tags")
    // Original does object.__delattr__(obj,"tags") then get_by_tag must not crash (hasattr guard)
    // In C# _tags is not deletable, so we set _tags field to null via reflection to simulate missing attribute
    // and ensure GetByTag handles null gracefully (hasattr fallback)
    [Fact] public void GetByTagMissingTagsAttr()
    {
        using var env=GlobalTestEnv.Enter(nameof(GetByTagMissingTagsAttr));
        var obj=new GameObject(); obj.Id=30;
        var f=typeof(GameObject).GetField("_tags", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        f!.SetValue(obj, null);
        ObjectRegistry.AddObject(obj);
        var ex = Record.Exception(()=> ObjectRegistry.GetByTag("warrior"));
        Assert.Null(ex);
        var result=ObjectRegistry.GetByTag("warrior");
        Assert.DoesNotContain(obj, result);
        // Restore for cleanup to avoid NRE in later teardown
        f.SetValue(obj, new HashSet<string>());
    }

    // Port of test_tags.py:202 add_tag variants — use direct method but entity param via TaggedObj would be parametrized; we test generic
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void AddTagSingleString(string entity)
    {
        var obj=MakeInstance(entity);
        obj.AddTag("warrior");
        Assert.Contains("warrior", obj.TagsSnapshot);
    }
    [Fact] public void AddTagList()
    {
        var obj=new GameObject();
        obj.AddTags(new[]{"warrior","mage"});
        Assert.Contains("warrior", obj.TagsSnapshot);
        Assert.Contains("mage", obj.TagsSnapshot);
    }
    [Fact] public void AddTagSet()
    {
        var obj=new GameObject();
        obj.AddTags(new HashSet<string>{"rogue","bard"});
        Assert.Contains("rogue", obj.TagsSnapshot);
        Assert.Contains("bard", obj.TagsSnapshot);
    }
    [Fact] public void AddTagIdempotent()
    {
        var obj=new GameObject();
        obj.AddTag("hero"); obj.AddTag("hero");
        Assert.Single(obj.TagsSnapshot.Where(t=>t=="hero"));
    }
    [Fact] public void AddTagSetsIsModified()
    {
        var obj=new GameObject(); obj.IsModified=false;
        obj.AddTag("knight");
        Assert.True(obj.IsModified);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void AddTagVisibleToGetByTag(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(AddTagVisibleToGetByTag));
        var obj=MakeInstance(entity, 40); obj.Id=40; ObjectRegistry.AddObject(obj);
        obj.AddTag("paladin");
        Assert.Contains(obj, ObjectRegistry.GetByTag("paladin"));
    }
    // Port of test_tags.py:243 remove
    [Fact] public void RemoveTagSingleString()
    {
        var obj=new GameObject(); obj.AddTags(new[]{"warrior","mage"}); obj.RemoveTag("warrior");
        Assert.DoesNotContain("warrior", obj.TagsSnapshot); Assert.Contains("mage", obj.TagsSnapshot);
    }
    [Fact] public void RemoveTagList()
    {
        var obj=new GameObject(); obj.AddTags(new[]{"warrior","mage","bard"}); obj.RemoveTags(new[]{"warrior","mage"});
        Assert.DoesNotContain("warrior", obj.TagsSnapshot); Assert.DoesNotContain("mage", obj.TagsSnapshot); Assert.Contains("bard", obj.TagsSnapshot);
    }
    [Fact] public void RemoveTagSet()
    {
        var obj=new GameObject(); obj.AddTags(new HashSet<string>{"a","b","c"}); obj.RemoveTags(new HashSet<string>{"a","b"});
        Assert.DoesNotContain("a", obj.TagsSnapshot); Assert.DoesNotContain("b", obj.TagsSnapshot); Assert.Contains("c", obj.TagsSnapshot);
    }
    [Fact] public void RemoveTagMissingIsSilent()
    {
        var obj=new GameObject(); obj.RemoveTag("nonexistent"); // should not throw
    }
    [Fact] public void RemoveTagSetsIsModified()
    {
        var obj=new GameObject(); obj.AddTag("knight"); obj.IsModified=false; obj.RemoveTag("knight"); Assert.True(obj.IsModified);
    }
    [Theory]
    [InlineData("object")]
    [InlineData("account")]
    [InlineData("channel")]
    [InlineData("node")]
    [InlineData("script")]
    public void RemoveTagInvisibleToGetByTag(string entity)
    {
        using var env=GlobalTestEnv.Enter(nameof(RemoveTagInvisibleToGetByTag));
        var obj=MakeInstance(entity, 41); obj.Id=41; obj.AddTag("necromancer"); ObjectRegistry.AddObject(obj);
        Assert.Contains(obj, ObjectRegistry.GetByTag("necromancer"));
        obj.RemoveTag("necromancer");
        Assert.DoesNotContain(obj, ObjectRegistry.GetByTag("necromancer"));
    }
    // Port of test_tags.py:291 has_tag
    [Fact] public void HasTagSingleString()
    {
        var obj=new GameObject(); obj.AddTag("warrior");
        Assert.True(obj.HasTag("warrior")); Assert.False(obj.HasTag("mage"));
    }
    [Fact] public void HasTagList()
    {
        var obj=new GameObject(); obj.AddTags(new[]{"warrior","mage"});
        Assert.True(obj.HasTags(new[]{"warrior"})); Assert.True(obj.HasTags(new[]{"warrior","rogue"})); Assert.False(obj.HasTags(new[]{"rogue","bard"}));
    }
    [Fact] public void HasTagSet()
    {
        var obj=new GameObject(); obj.AddTag("warrior");
        Assert.True(obj.HasTags(new HashSet<string>{"warrior","mage"})); Assert.False(obj.HasTags(new HashSet<string>{"mage"}));
    }
    [Fact] public void HasTagAll()
    {
        var obj=new GameObject(); obj.AddTags(new[]{"warrior","hero"});
        Assert.True(obj.HasTags(new[]{"warrior","hero"}, all:true));
        Assert.True(obj.HasTags(new[]{"warrior"}, all:true));
        Assert.False(obj.HasTags(new[]{"warrior","mage"}, all:true));
        Assert.False(obj.HasTags(new[]{"mage"}, all:true));
        Assert.True(obj.HasTags(Array.Empty<string>(), all:true));
    }
    // Port of test_tags.py:323 aliases mutability
    [Fact] public void AliasesListIsCopiedNotReferenced()
    {
        using var env=GlobalTestEnv.Enter(nameof(AliasesListIsCopiedNotReferenced));
        var orig=new List<string>{"alpha","beta"};
        var obj=GameObject.Create("Aliased", aliases: orig);
        orig.Add("gamma");
        Assert.DoesNotContain("gamma", obj.Aliases);
        Assert.Equal(new[]{"alpha","beta"}, obj.Aliases);
    }
    [Fact] public void AliasesMutatingObjectDoesNotAffectCaller()
    {
        using var env=GlobalTestEnv.Enter(nameof(AliasesMutatingObjectDoesNotAffectCaller));
        var orig=new List<string>{"one"};
        var obj=GameObject.Create("Aliased2", aliases: orig);
        // Faithful to original test_tags.py:331-337 via reflection: object.__getattribute__(obj,"aliases").append("two")
        // In C# Aliases getter returns copy, so to simulate in-place mutation we must access underlying _aliases field directly
        var f = typeof(GameObject).GetField("_aliases", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var underlying = (List<string>)f.GetValue(obj)!;
        underlying.Add("two");
        Assert.Equal(new[]{"one"}, orig);
        Assert.Contains("two", underlying);
        // Also verify copy semantics: mutating a copy does NOT affect underlying (C# divergence documented)
        var copy = obj.Aliases; copy.Add("three");
        Assert.DoesNotContain("three", obj.Aliases);
    }
    [Fact] public void AliasesEmptyListNotShared()
    {
        using var env=GlobalTestEnv.Enter(nameof(AliasesEmptyListNotShared));
        var a=GameObject.Create("A", aliases: null);
        var b=GameObject.Create("B", aliases: null);
        // Faithful to original: object.__getattribute__(a,"aliases").append("x") — mutate underlying list in-place via reflection
        var f = typeof(GameObject).GetField("_aliases", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var listA = (List<string>)f.GetValue(a)!;
        var listB = (List<string>)f.GetValue(b)!;
        listA.Add("x");
        Assert.Contains("x", listA);
        Assert.DoesNotContain("x", listB);
        Assert.DoesNotContain("x", b.Aliases);
        Assert.Contains("x", a.Aliases);
    }
    [Fact] public void AliasesNoneGivesEmpty()
    {
        using var env=GlobalTestEnv.Enter(nameof(AliasesNoneGivesEmpty));
        var obj=GameObject.Create("NoAlias");
        Assert.Empty(obj.Aliases);
    }
}
