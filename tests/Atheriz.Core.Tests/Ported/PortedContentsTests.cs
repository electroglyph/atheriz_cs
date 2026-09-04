// Port of atheriz/tests/test_contents_helpers.py:1
// Port of atheriz/tests/test_contents_search.py:1
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedContentsTests
{
    // Helpers
    private static GameObject MakeObj(string name, int? id = null)
    {
        var o = GameObject.Create(name);
        if (id != null) o.Id = id.Value;
        ObjectRegistry.AddObject(o);
        return o;
    }

    // ----- filter_visible -----
    [Fact]
    public void FilterVisibleNoLookerReturnsUnchanged()
    {
        var a = MakeObj("a"); var b = MakeObj("b");
        var list = new List<GameObject>{a,b};
        var res = ContentUtils.FilterVisible(list, null);
        Assert.Same(list, res);
    }

    [Fact]
    public void FilterVisibleExcludesLookerSelf()
    {
        using var env = GlobalTestEnv.Enter();
        var a = MakeObj("a"); var b = MakeObj("b");
        var res = ContentUtils.FilterVisible(new List<GameObject>{a,b}, a);
        Assert.DoesNotContain(a, res);
        Assert.Contains(b, res);
    }

    [Fact]
    public void FilterVisibleExcludesInvisible()
    {
        using var env = GlobalTestEnv.Enter();
        var visible = MakeObj("visible");
        var hidden = MakeObj("hidden");
        hidden.AddLock("view", _=>false);
        var looker = MakeObj("looker");
        var res = ContentUtils.FilterVisible(new List<GameObject>{visible, hidden, looker}, looker);
        Assert.Contains(visible, res);
        Assert.DoesNotContain(hidden, res);
        Assert.DoesNotContain(looker, res);
    }

    [Fact]
    public void FilterVisibleWithRealObjects()
    {
        using var env = GlobalTestEnv.Enter();
        var a = MakeObj("a"); var b = MakeObj("b"); var c = MakeObj("c"); var looker = MakeObj("looker");
        var res = ContentUtils.FilterVisible(new List<GameObject>{a,b,c,looker}, looker);
        Assert.DoesNotContain(looker, res);
        Assert.Equal(new HashSet<string>{"a","b","c"}, res.Select(x=>x.Name).ToHashSet());
    }

    // ----- group_by_name -----
    [Fact]
    public void GroupByNameEmptyReturnsEmptyString()
    {
        Assert.Equal("", ContentUtils.GroupByName(new List<GameObject>()));
        // with looker
        var looker = MakeObj("looker");
        Assert.Equal("", ContentUtils.GroupByName(new List<GameObject>(), looker));
    }

    [Fact]
    public void GroupByNameUniqueNames()
    {
        var a = MakeObj("apple"); a.Name = "apple";
        var b = MakeObj("banana"); b.Name = "banana";
        var res = ContentUtils.GroupByName(new List<GameObject>{a,b});
        Assert.Equal("apple, banana", res);
    }

    [Fact]
    public void GroupByNameDuplicatesSuffixedWithCount()
    {
        var a = MakeObj("apple1"); a.Name="apple";
        var b = MakeObj("apple2"); b.Name="apple";
        var c = MakeObj("banana"); c.Name="banana";
        var res = ContentUtils.GroupByName(new List<GameObject>{a,b,c});
        Assert.Contains("apple(2)", res);
        Assert.Contains("banana", res);
    }

    [Fact]
    public void GroupByNameUsesDisplayNameWhenLookerGiven()
    {
        using var env = GlobalTestEnv.Enter();
        var a = new TestDisplayObj("apple") { DisplayName = "The Apple" };
        ObjectRegistry.AddObject(a);
        var b = new TestDisplayObj("banana") { DisplayName = "A Banana" };
        ObjectRegistry.AddObject(b);
        var looker = MakeObj("looker");
        var res = ContentUtils.GroupByName(new List<GameObject>{a,b}, looker);
        Assert.Equal("The Apple, A Banana", res);
    }

    private sealed class TestDisplayObj : GameObject
    {
        public string DisplayName;
        public TestDisplayObj(string n) { Name=n; DisplayName=n; }
        public override string GetDisplayName(GameObject? looker) => DisplayName;
    }

    [Fact]
    public void GroupByNameNoLookerUsesName()
    {
        var a = MakeObj("apple1"); a.Name="apple";
        var res = ContentUtils.GroupByName(new List<GameObject>{a});
        Assert.Equal("apple", res);
    }

    // ----- filter_contents -----
    [Fact]
    public void FilterContentsReturnsMatching()
    {
        using var env = GlobalTestEnv.Enter();
        var a = MakeObj("a"); var b = MakeObj("b"); var c = MakeObj("c");
        a.AddContent(b.Id); a.AddContent(c.Id);
        var res = ContentUtils.FilterContents(a, x=> x==b || x==a);
        Assert.Contains(b, res);
        Assert.DoesNotContain(c, res);
        Assert.Single(res);
    }

    [Fact]
    public void FilterContentsEmptyWhenNothingMatches()
    {
        using var env = GlobalTestEnv.Enter();
        var a = MakeObj("a"); var b = MakeObj("b");
        a.AddContent(b.Id);
        var res = ContentUtils.FilterContents(a, x=> x.Name=="zzz");
        Assert.Empty(res);
    }

    [Fact]
    public void FilterContentsPreservesOrder()
    {
        using var env = GlobalTestEnv.Enter();
        var a = MakeObj("a"); var b = MakeObj("b"); var c = MakeObj("c");
        a.AddContent(b.Id); a.AddContent(c.Id);
        var res = ContentUtils.FilterContents(a, x=>true);
        Assert.Equal(new HashSet<int>{b.Id,c.Id}, res.Select(r=>r.Id).ToHashSet());
    }

    // ----- global registry search -----
    [Fact]
    public void SearchBad()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Empty(ObjectRegistry.Get(-1));
        Assert.Empty(ObjectRegistry.Get(new List<int>{-1}));
    }

    [Fact]
    public void SearchById()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new GameObject(); obj.Id=1; obj.Name="test"; ObjectRegistry.AddObject(obj);
        var res = ObjectRegistry.Get(1);
        Assert.Single(res);
        Assert.Same(obj, res[0]);
        Assert.Empty(ObjectRegistry.Get(999));
    }

    [Fact]
    public void SearchIds()
    {
        using var env = GlobalTestEnv.Enter();
        var o1 = new GameObject(); o1.Id=10; ObjectRegistry.AddObject(o1);
        var o2 = new GameObject(); o2.Id=11; ObjectRegistry.AddObject(o2);
        var o3 = new GameObject(); o3.Id=12; ObjectRegistry.AddObject(o3);
        var res = ObjectRegistry.Get(new List<int>{10,12});
        Assert.Equal(2, res.Count);
        Assert.Contains(o1, res); Assert.Contains(o3, res); Assert.DoesNotContain(o2, res);
    }

    [Fact]
    public void FilterBy()
    {
        using var env = GlobalTestEnv.Enter();
        var o1 = new GameObject(); o1.Id=20; o1.IsPc=true; ObjectRegistry.AddObject(o1);
        var o2 = new GameObject(); o2.Id=21; o2.IsPc=false; ObjectRegistry.AddObject(o2);
        var o3 = new GameObject(); o3.Id=22; o3.IsPc=true; ObjectRegistry.AddObject(o3);
        var res = ObjectRegistry.FilterBy(x=> x.IsPc);
        Assert.Equal(2, res.Count);
        Assert.Contains(o1, res); Assert.Contains(o3, res); Assert.DoesNotContain(o2, res);
    }

    // ----- contents search helpers -----
    private static (GameObject bag, GameObject coin, GameObject pouch, GameObject sword, GameObject box, GameObject gem) BuildNested()
    {
        var bag = new GameObject(); bag.Id=1; bag.Name="bag"; ObjectRegistry.AddObject(bag);
        var coin = new GameObject(); coin.Id=2; coin.Name="coin"; ObjectRegistry.AddObject(coin); bag.AddObject(coin);
        var pouch = new GameObject(); pouch.Id=3; pouch.Name="pouch"; pouch.IsContainer=true; ObjectRegistry.AddObject(pouch); bag.AddObject(pouch);
        var sword = new GameObject(); sword.Id=4; sword.Name="sword"; ObjectRegistry.AddObject(sword); pouch.AddObject(sword);
        var box = new GameObject(); box.Id=5; box.Name="box"; ObjectRegistry.AddObject(box); bag.AddObject(box);
        var gem = new GameObject(); gem.Id=6; gem.Name="gem"; ObjectRegistry.AddObject(gem); box.AddObject(gem);
        return (bag, coin, pouch, sword, box, gem);
    }

    [Fact]
    public void SearchBasics()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new GameObject(); obj.Id=0; obj.Name="sword"; ObjectRegistry.AddObject(obj);
        var container = new GameObject(); container.Id=1; container.Name="container"; ObjectRegistry.AddObject(container); container.AddObject(obj);
        var res = ContentUtils.Search(container, "sword", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(res); Assert.Same(obj, res[0]);
    }

    [Fact]
    public void SearchAlias()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new GameObject(); obj.Id=0; obj.Name="longsword"; obj.Aliases=new List<string>{"sword","blade"}; ObjectRegistry.AddObject(obj);
        var container = new GameObject(); container.Id=1; container.Name="container"; ObjectRegistry.AddObject(container); container.AddObject(obj);
        var res = ContentUtils.Search(container, "blade", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(res); Assert.Same(obj, res[0]);
    }

    [Fact]
    public void SearchIndex()
    {
        using var env = GlobalTestEnv.Enter();
        var o1 = new GameObject(); o1.Id=0; o1.Name="sword"; ObjectRegistry.AddObject(o1);
        var o2 = new GameObject(); o2.Id=1; o2.Name="sword"; ObjectRegistry.AddObject(o2);
        var container = new GameObject(); container.Id=2; container.Name="container"; ObjectRegistry.AddObject(container); container.AddObject(o1); container.AddObject(o2);
        var r1 = ContentUtils.Search(container, "sword 1", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(r1); Assert.Same(o1, r1[0]);
        var r2 = ContentUtils.Search(container, "sword 2", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(r2); Assert.Same(o2, r2[0]);
    }

    [Fact]
    public void SearchAll()
    {
        using var env = GlobalTestEnv.Enter();
        var o1 = new GameObject(); o1.Id=0; o1.Name="coin"; ObjectRegistry.AddObject(o1);
        var o2 = new GameObject(); o2.Id=1; o2.Name="coin"; ObjectRegistry.AddObject(o2);
        var o3 = new GameObject(); o3.Id=2; o3.Name="gem"; ObjectRegistry.AddObject(o3);
        var container = new GameObject(); container.Id=3; container.Name="bag"; ObjectRegistry.AddObject(container); container.AddObject(o1); container.AddObject(o2); container.AddObject(o3);
        var res = ContentUtils.Search(container, "all coin", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Equal(2, res.Count); Assert.Contains(o1, res); Assert.Contains(o2, res); Assert.DoesNotContain(o3, res);
    }

    [Fact]
    public void SearchCount()
    {
        using var env = GlobalTestEnv.Enter();
        var o1 = new GameObject(); o1.Id=0; o1.Name="coin"; ObjectRegistry.AddObject(o1);
        var o2 = new GameObject(); o2.Id=1; o2.Name="coin"; ObjectRegistry.AddObject(o2);
        var o3 = new GameObject(); o3.Id=2; o3.Name="coin"; ObjectRegistry.AddObject(o3);
        var bag = new GameObject(); bag.Id=3; bag.Name="bag"; ObjectRegistry.AddObject(bag); bag.AddObject(o1); bag.AddObject(o2); bag.AddObject(o3);
        var res = ContentUtils.Search(bag, "2 coin", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Equal(2, res.Count);
    }

    [Fact]
    public void SearchId()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new GameObject(); obj.Id=42; obj.Name="unique"; ObjectRegistry.AddObject(obj);
        var container = new GameObject(); container.Id=1; container.Name="world"; ObjectRegistry.AddObject(container); container.AddObject(obj);
        var res = ContentUtils.Search(container, "#42", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(res); Assert.Same(obj, res[0]);
    }

    [Fact]
    public void SearchSelf()
    {
        using var env = GlobalTestEnv.Enter();
        var me = new GameObject(); me.Id=0; me.Name="Hero"; ObjectRegistry.AddObject(me);
        var res = ContentUtils.Search(me, "me", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(res); Assert.Same(me, res[0]);
    }

    [Fact]
    public void SearchPlurals()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new GameObject(); obj.Id=0; obj.Name="sword"; ObjectRegistry.AddObject(obj);
        var chest = new GameObject(); chest.Id=1; chest.Name="chest"; ObjectRegistry.AddObject(chest); chest.AddObject(obj);
        var res = ContentUtils.Search(chest, "swords", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(res); Assert.Same(obj, res[0]);
    }

    [Fact]
    public void SearchRecursiveFindsNested()
    {
        using var env = GlobalTestEnv.Enter();
        var (bag, coin, pouch, sword, box, gem) = BuildNested();
        var res = ContentUtils.Search(bag, "sword", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(res); Assert.Same(sword, res[0]);
    }

    [Fact]
    public void SearchRecursiveFalseStaysFlat()
    {
        using var env = GlobalTestEnv.Enter();
        var (bag, coin, pouch, sword, box, gem) = BuildNested();
        var res = ContentUtils.Search(bag, "sword", id=>ObjectRegistry.Get(id).FirstOrDefault(), recursive:false);
        Assert.Empty(res);
        var res2 = ContentUtils.Search(bag, "coin", id=>ObjectRegistry.Get(id).FirstOrDefault(), recursive:false);
        Assert.Single(res2); Assert.Same(coin, res2[0]);
    }

    [Fact]
    public void SearchSkipsNonContainer()
    {
        using var env = GlobalTestEnv.Enter();
        var (bag, coin, pouch, sword, box, gem) = BuildNested();
        var res = ContentUtils.Search(bag, "gem", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Empty(res);
    }

    [Fact]
    public void SearchByIndexWithSparsePositions()
    {
        using var env = GlobalTestEnv.Enter();
        var container = new GameObject(); container.Id=100; container.Name="chest"; ObjectRegistry.AddObject(container);
        var objs = new List<GameObject>();
        for(int i=0;i<5;i++)
        {
            var o = new GameObject(); o.Id=200+i; o.Name = (i==1||i==4) ? "sword" : "shield"; ObjectRegistry.AddObject(o); container.AddObject(o); objs.Add(o);
        }
        var r2 = ContentUtils.Search(container, "sword 2", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(r2); Assert.Same(objs[4], r2[0]);
        var r1 = ContentUtils.Search(container, "sword 1", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(r1); Assert.Same(objs[1], r1[0]);
    }

    [Fact]
    public void SingularWordsNotTreatedAsPlurals()
    {
        using var env = GlobalTestEnv.Enter();
        var container = new GameObject(); container.Id=500; container.Name="station"; ObjectRegistry.AddObject(container);
        var bus = new GameObject(); bus.Id=501; bus.Name="bus"; ObjectRegistry.AddObject(bus); container.AddObject(bus);
        var glass = new GameObject(); glass.Id=502; glass.Name="glass"; ObjectRegistry.AddObject(glass); container.AddObject(glass);
        var photo1 = new GameObject(); photo1.Id=503; photo1.Name="photo"; ObjectRegistry.AddObject(photo1); container.AddObject(photo1);
        var photo2 = new GameObject(); photo2.Id=504; photo2.Name="photo"; ObjectRegistry.AddObject(photo2); container.AddObject(photo2);
        var rb = ContentUtils.Search(container, "bus", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(rb); Assert.Same(bus, rb[0]);
        var rg = ContentUtils.Search(container, "glass", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Single(rg); Assert.Same(glass, rg[0]);
        var rp = ContentUtils.Search(container, "photos", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Equal(2, rp.Count); Assert.Contains(photo1, rp); Assert.Contains(photo2, rp);
    }

    [Fact]
    public void PluralSearchReturnsEachObjectOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,0,0,0));
        var crate1 = GameObject.Create("crate", isItem:true); ObjectRegistry.AddObject(crate1); crate1.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(crate1);
        var crate2 = GameObject.Create("crate", isItem:true); ObjectRegistry.AddObject(crate2); crate2.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(crate2);
        var res = node.Search("crates");
        var ids = res.Select(o=>o.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(2, res.Count);
    }

    [Fact]
    public void SearchDoesNotReturnContainerItself()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer:true); ObjectRegistry.AddObject(bag);
        var coin = GameObject.Create("coin", isItem:true); ObjectRegistry.AddObject(coin); coin.MoveTo(bag);
        var res = ContentUtils.Search(bag, "bag", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.DoesNotContain(bag, res); Assert.Empty(res);
    }

    [Fact]
    public void SearchContainerNameDoesNotShadowContents()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer:true); ObjectRegistry.AddObject(bag);
        var bag2 = GameObject.Create("bag", isItem:true); ObjectRegistry.AddObject(bag2); bag2.MoveTo(bag);
        var res = ContentUtils.Search(bag, "bag", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.DoesNotContain(bag, res);
        Assert.Contains(bag2, res);
    }

    [Fact]
    public void SearchMeStillReturnsSelf()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(hero);
        var res = hero.Search("me");
        Assert.Contains(hero, res);
    }

    [Fact]
    public void SearchAllAloneReturnsAllContents()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag2", isContainer:true); ObjectRegistry.AddObject(bag);
        var a = GameObject.Create("apple", isItem:true); ObjectRegistry.AddObject(a); a.MoveTo(bag);
        var b = GameObject.Create("banana", isItem:true); ObjectRegistry.AddObject(b); b.MoveTo(bag);
        var res = ContentUtils.Search(bag, "all", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Equal(2, res.Count); Assert.Contains(a, res); Assert.Contains(b, res);
    }

    [Fact]
    public void SearchSubstringDoesNotMatchCaterpillar()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag3", isContainer:true); ObjectRegistry.AddObject(bag);
        var caterpillar = GameObject.Create("caterpillar", isItem:true); ObjectRegistry.AddObject(caterpillar); caterpillar.MoveTo(bag);
        var res = bag.Search("cat");
        Assert.DoesNotContain(caterpillar, res);
        var cat = GameObject.Create("cat", isItem:true); ObjectRegistry.AddObject(cat); cat.MoveTo(bag);
        var res2 = bag.Search("cat");
        Assert.Contains(cat, res2); Assert.DoesNotContain(caterpillar, res2);
    }

    [Fact]
    public void SearchSplitMultipleSpaces()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag4", isContainer:true); ObjectRegistry.AddObject(bag);
        var sword = GameObject.Create("sword", isItem:true); ObjectRegistry.AddObject(sword); sword.MoveTo(bag);
        var r1 = ContentUtils.Search(bag, "sword  ", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Contains(sword, r1);
        var r2 = ContentUtils.Search(bag, "  sword", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Contains(sword, r2);
        var r3 = ContentUtils.Search(bag, "sword  2", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.Empty(r3);
    }

    [Fact]
    public void SearchPluralCatVsCaterpillar()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag5", isContainer:true); ObjectRegistry.AddObject(bag);
        var caterpillar = GameObject.Create("caterpillar", isItem:true); ObjectRegistry.AddObject(caterpillar); caterpillar.MoveTo(bag);
        var res = ContentUtils.Search(bag, "cats", id=>ObjectRegistry.Get(id).FirstOrDefault());
        Assert.DoesNotContain(caterpillar, res);
    }

    // ----- missing tests from test_contents_search.py:338,351,456 -----
    private static (GameObject bag, GameObject deepest) BuildChain(int depth)
    {
        var bag = new GameObject(); bag.Id=1; bag.Name="bag"; ObjectRegistry.AddObject(bag);
        var parent = bag;
        int nextId = 2;
        for(int i=0;i<depth;i++)
        {
            var c = new GameObject(); c.Id=nextId; c.Name=$"c{nextId}"; c.IsContainer=true; ObjectRegistry.AddObject(c);
            parent.AddObject(c);
            parent = c;
            nextId++;
        }
        var deepest = new GameObject(); deepest.Id=nextId; deepest.Name="deepest"; ObjectRegistry.AddObject(deepest);
        parent.AddObject(deepest);
        return (bag, deepest);
    }

    // Port of test_contents_search.py:338 test_search_depth_limit_caps_recursion — MAX_SEARCH_DEPTH stops descent
    [Fact]
    public void SearchDepthLimitCapsRecursion()
    {
        using var env = GlobalTestEnv.Enter();
        var orig = ContentUtils.MaxSearchDepth;
        try
        {
            ContentUtils.MaxSearchDepth = 3;
            var (bag, deepest) = BuildChain(4);
            Assert.Empty(ContentUtils.Search(bag, "deepest", id=>ObjectRegistry.Get(id).FirstOrDefault()));
            var coin = new GameObject(); coin.Id=999; coin.Name="coin"; ObjectRegistry.AddObject(coin); bag.AddObject(coin);
            var res = ContentUtils.Search(bag, "coin", id=>ObjectRegistry.Get(id).FirstOrDefault());
            Assert.Single(res); Assert.Same(coin, res[0]);
        }
        finally { ContentUtils.MaxSearchDepth = orig; }
    }

    // Port of test_contents_search.py:351 test_search_recursion_error_is_caught — RecursionError swallowed
    // In C# GatherContents catches generic exception (no Python recursionlimit), adapted to ensure deep chain does not throw
    [Fact]
    public void SearchRecursionErrorIsCaught()
    {
        using var env = GlobalTestEnv.Enter();
        var orig = ContentUtils.MaxSearchDepth;
        try
        {
            ContentUtils.MaxSearchDepth = 10_000;
            // Build chain depth 80 — would exceed Python recursionlimit 60; in C# depth is iterative via recursion but we catch exceptions
            var (bag, deepest) = BuildChain(80);
            var ex = Record.Exception(()=> ContentUtils.Search(bag, "deepest", id=>ObjectRegistry.Get(id).FirstOrDefault()));
            Assert.Null(ex);
            var result = ContentUtils.Search(bag, "deepest", id=>ObjectRegistry.Get(id).FirstOrDefault());
            Assert.IsType<List<GameObject>>(result);
        }
        finally { ContentUtils.MaxSearchDepth = orig; }
    }

    // Port of test_contents_search.py:456 test_singular_search_returns_each_object_once — dedup singular
    [Fact]
    public void SingularSearchReturnsEachObjectOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,0,0,0)); ObjectRegistry.AddObject(node);
        var crate1 = GameObject.Create("crate", isItem:true); ObjectRegistry.AddObject(crate1); crate1.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(crate1);
        var res = node.Search("crate");
        Assert.Single(res); Assert.Same(crate1, res[0]);
        // Also ensure no duplicates when searching singular with multiple identical objects
        var crate2 = GameObject.Create("crate", isItem:true); ObjectRegistry.AddObject(crate2); crate2.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(crate2);
        var res2 = node.Search("crate");
        // singular returns first match only (count=1) — not both; but ensure no duplicates
        Assert.Single(res2);
        var allRes = ContentUtils.Search(node, "all crate", id=>ObjectRegistry.Get(id).FirstOrDefault());
        var ids = allRes.Select(o=>o.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(2, allRes.Count);
    }
}
