using System.Text.Json;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Batch C of the commands.md rollout: alias, copy, name, wipe, find,
// objects (all builder verbs).
[Collection("Ported")]
public class BatchCCommandsTests
{
    private static GameObject MakeBuilder()
        => PortedHelpers.MakeCaller("BatchCBuilder", Privilege.Builder);

    private static GameObject MakeBox(string name = "box")
    {
        var box = GameObject.Create(name);
        ObjectRegistry.AddObject(box);
        return box;
    }

    private static void Run(GameObject caller, string text)
    {
        var job = CommandDispatcher.DispatchLoggedIn(caller, text, immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void Alias_Set_ReplacesList()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeBox();
        Run(MakeBuilder(), "alias box = a1;a2");
        Assert.Equal(["a1", "a2"], box.Aliases);
    }

    [Fact]
    public void Alias_Bare_ListsCurrent()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeBox();
        Run(hero, "alias box = a1");
        hero.ClearMessages();
        Run(hero, "alias box");
        Assert.Contains(hero.PeekMessages(), m => m.Contains("a1"));
    }

    [Fact]
    public void Alias_EmptyRight_Clears()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeBox();
        Run(hero, "alias box = a1");
        Run(hero, "alias box =");
        Assert.Empty(box.Aliases);
    }

    [Fact]
    public void Copy_DuplicatesFields()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var src = MakeBox("src");
        src.Desc = "shiny";
        src.Aliases = ["s1"];
        src.AddTag("junk");
        src.SetExtraJson("note", JsonSerializer.SerializeToElement("hi"));
        Run(hero, "copy src");
        var copies = ObjectRegistry.FilterBy(o => o.Name == "src");
        Assert.Equal(2, copies.Count);
        var copy = copies.First(o => o.Id != src.Id);
        Assert.Equal("shiny", copy.Desc);
        Assert.Equal(["s1"], copy.Aliases);
        Assert.True(copy.HasTag("junk"));
        Assert.True(copy.TryGetExtraJson("note", out _));
    }

    [Fact]
    public void Copy_NewName_Used()
    {
        using var env = GlobalTestEnv.Enter();
        MakeBox("src");
        Run(MakeBuilder(), "copy src = src2");
        Assert.Single(ObjectRegistry.FilterBy(o => o.Name == "src2"));
    }

    [Fact]
    public void Copy_CharacterWithoutNewName_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var bob = GameObject.Create("Bob", isPc: true);
        ObjectRegistry.AddObject(bob);
        Run(hero, "copy Bob");
        Assert.Single(ObjectRegistry.FilterBy(o => o.Name == "Bob"));
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Copy_CharacterCollision_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var bob = GameObject.Create("Bob", isPc: true);
        var rob = GameObject.Create("Robert", isPc: true);
        ObjectRegistry.AddObject(bob);
        ObjectRegistry.AddObject(rob);
        Run(hero, "copy Bob = Robert");
        Assert.Single(ObjectRegistry.FilterBy(o => o.Name == "Robert"));
    }

    [Fact]
    public void Name_RenamesObject()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeBox();
        Run(MakeBuilder(), "name box = crate");
        Assert.Equal("crate", box.Name);
    }

    [Fact]
    public void Name_BadName_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeBox();
        Run(hero, "name box = ??");
        Assert.Equal("box", box.Name);
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Name_PcCollision_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var rob = GameObject.Create("Robert", isPc: true);
        ObjectRegistry.AddObject(rob);
        var rock = MakeBox("rock");
        Run(hero, "name rock = Robert");
        Assert.Equal("rock", rock.Name);
        Assert.Single(ObjectRegistry.FilterBy(o => o.Name == "Robert"));
    }

    [Fact]
    public void Name_HigherPrivilege_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var crown = MakeBox("crown");
        crown.PrivilegeLevel = Privilege.Admin;
        Run(hero, "name crown = pebble");
        Assert.Equal("crown", crown.Name);
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Wipe_All_RemovesExtras()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeBox();
        box.SetExtraJson("one", JsonSerializer.SerializeToElement(1));
        box.SetExtraJson("two", JsonSerializer.SerializeToElement(2));
        Run(MakeBuilder(), "wipe box");
        Assert.Empty(box.GetExtraSnapshot());
    }

    [Fact]
    public void Wipe_Pattern_Selective()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeBox();
        box.SetExtraJson("del1", JsonSerializer.SerializeToElement(1));
        box.SetExtraJson("keep", JsonSerializer.SerializeToElement(2));
        Run(MakeBuilder(), "wipe box = del*");
        Assert.False(box.HasExtra("del1"));
        Assert.True(box.HasExtra("keep"));
    }

    [Fact]
    public void Wipe_Protected_RefusedForBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeBox();
        box.SetExtraJson("_secret", JsonSerializer.SerializeToElement(1));
        Run(hero, "wipe box");
        Assert.Single(hero.PeekMessages());
        Assert.True(box.HasExtra("_secret"));
    }

    [Fact]
    public void Wipe_Protected_SuperuserAllowed()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("BatchCSuper", Privilege.Admin);
        var box = MakeBox();
        box.SetExtraJson("_secret", JsonSerializer.SerializeToElement(1));
        Run(hero, "wipe box");
        Assert.False(box.HasExtra("_secret"));
    }

    [Fact]
    public void Find_NameSubstring_FindsPc()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var bob = GameObject.Create("Bob", isPc: true);
        ObjectRegistry.AddObject(bob);
        Run(hero, "find bob");
        Assert.Contains(hero.PeekMessages(), m => m.Contains("Bob") && m.Contains($"#{bob.Id}"));
    }

    [Fact]
    public void Find_IdForm_FindsObject()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeBox("onlybox");
        Run(hero, $"find #{box.Id}");
        Assert.Contains(hero.PeekMessages(), m => m.Contains("onlybox"));
    }

    [Fact]
    public void Find_StarForm_FindsAccount()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var acc = Account.Create("batchc-find", "password1");
        Run(hero, "find *batchc");
        Assert.Contains(hero.PeekMessages(), m => m.Contains(acc.Name));
    }

    [Fact]
    public void Find_TypeFilter_RestrictsKind()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var bob = GameObject.Create("Bob", isPc: true);
        var sled = GameObject.Create("bobsled", isItem: true);
        ObjectRegistry.AddObject(bob);
        ObjectRegistry.AddObject(sled);
        Run(hero, "find --type char bob");
        var text = string.Join("\n", hero.PeekMessages());
        Assert.Contains("Bob", text);
        Assert.DoesNotContain("bobsled", text);
    }

    [Fact]
    public void Find_CapsAtFifty()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        for (int i = 0; i < 55; i++)
            ObjectRegistry.AddObject(GameObject.Create($"capmob{i}", isNpc: true));
        Run(hero, "find capmob");
        var lines = hero.PeekMessages()[0].Split('\n');
        Assert.Equal(52, lines.Length);
        Assert.Contains("and 5 more", hero.PeekMessages()[0]);
    }

    [Fact]
    public void Objects_ReportsCounts()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var pc1 = GameObject.Create("c1", isPc: true);
        var pc2 = GameObject.Create("c2", isPc: true);
        var node = new Node(new Coord("objroom", 0, 0, 0));
        var item = GameObject.Create("rock", isItem: true);
        ObjectRegistry.AddObject(pc1);
        ObjectRegistry.AddObject(pc2);
        ObjectRegistry.AddObject(node);
        ObjectRegistry.AddObject(item);
        Run(hero, "objects");
        var text = string.Join("\n", hero.PeekMessages());
        Assert.Contains("2 characters", text);
        Assert.Contains("1 rooms", text);
        Assert.Contains("0 exits", text);
    }

    [Fact]
    public void Ic_Alias_AnswersLikePuppet()
    {
        using var env = GlobalTestEnv.Enter();
        var viaKey = PortedHelpers.MakeCaller("BatchCPuppetKey", Privilege.Builder);
        Run(viaKey, "puppet nosuchtarget");
        var expected = viaKey.PeekMessages().ToList();
        var viaAlias = PortedHelpers.MakeCaller("BatchCPuppetAlias", Privilege.Builder);
        Run(viaAlias, "ic nosuchtarget");
        Assert.Equal(expected, viaAlias.PeekMessages());
    }

    [Fact]
    public void Ooc_Alias_AnswersLikeUnpuppet()
    {
        using var env = GlobalTestEnv.Enter();
        var viaKey = PortedHelpers.MakeCaller("BatchCUnpuppetKey", Privilege.Builder);
        Run(viaKey, "unpuppet");
        var expected = viaKey.PeekMessages().ToList();
        var viaAlias = PortedHelpers.MakeCaller("BatchCUnpuppetAlias", Privilege.Builder);
        Run(viaAlias, "ooc");
        Assert.Equal(expected, viaAlias.PeekMessages());
    }

    [Fact]
    public void Alias_Comma_SeparatesLikeSemicolon()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeBox();
        Run(MakeBuilder(), "alias box = a1,a2;a3");
        Assert.Equal(["a1", "a2", "a3"], box.Aliases);
    }

    [Fact]
    public void Copy_Semicolon_AssignsAliasesToCopy()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeBox("copybox");
        Run(hero, "copy copybox = CopyBox2;c1");
        var copy = ObjectRegistry.FilterBy(o => o.Name.Equals("CopyBox2", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        Assert.NotNull(copy);
        Assert.Equal(["c1"], copy!.Aliases);
        Assert.Empty(box.Aliases);
    }

    [Fact]
    public void Wipe_MultiplePatterns_RemovesUnion()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeBox();
        box.SetExtraJson("del1", JsonSerializer.SerializeToElement(1));
        box.SetExtraJson("del2", JsonSerializer.SerializeToElement(2));
        box.SetExtraJson("keep", JsonSerializer.SerializeToElement(3));
        Run(MakeBuilder(), "wipe box = del1 del2");
        Assert.False(box.HasExtra("del1"));
        Assert.False(box.HasExtra("del2"));
        Assert.True(box.HasExtra("keep"));
    }

    [Fact]
    public void Find_DbrefRange_ListsSpan()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var one = MakeBox("rangebox1");
        var two = MakeBox("rangebox2");
        int lo = Math.Min(one.Id, two.Id), hi = Math.Max(one.Id, two.Id);
        Run(hero, $"find #{lo}-#{hi}");
        var text = string.Join("\n", hero.PeekMessages());
        Assert.Contains("rangebox1", text);
        Assert.Contains("rangebox2", text);
    }
}
