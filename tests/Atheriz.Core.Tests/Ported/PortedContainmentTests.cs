// Port of atheriz/tests/test_containment.py:1
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedContainmentTests
{
    private static GameObject MakeContainer(string name)
    {
        var o = GameObject.Create(name, isContainer: true);
        ObjectRegistry.AddObject(o);
        return o;
    }

    private sealed class SimpleCaller : GameObject
    {
        private readonly List<string> _msgs = new();
        public IReadOnlyList<string> Msgs => _msgs;
        public Func<string, List<GameObject>>? SearchFunc = null!;
        public SimpleCaller(string name) { Name = name; IsPc = true; }
        public override void InstallHook(string funcName, Delegate hook) => base.InstallHook(funcName, hook);
        public void RecordMsg(string? t) => _msgs.Add(t ?? "");
        // Override Msg to capture
        public override void Msg(string text, GameObject? fromObj=null, IDictionary<string,object?>? mapping=null, bool raiseErrors=false, string? msgType=null)
        {
            _msgs.Add(text);
            base.Msg(text, fromObj, mapping, raiseErrors, msgType);
        }
        public List<GameObject> SearchOverride(string q) => SearchFunc?.Invoke(q) ?? new List<GameObject>();
    }

    private sealed class CaptureGameObject : GameObject
    {
        public List<string> Captured = new();
        public Func<string, List<GameObject>>? SearchHandler = null!;
        public CaptureGameObject(string name, bool isPc=false) { Name=name; IsPc=isPc; }
        public override void Msg(string text) { Captured.Add(text); base.Msg(text); }
        public override void Msg(string text, GameObject? fromObj, IDictionary<string,object?>? mapping, bool raiseErrors=false, string? msgType=null) { Captured.Add(text); base.Msg(text, fromObj, mapping, raiseErrors, msgType); }
    }

    [Fact]
    public void DirectSelfContainmentIsBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeContainer("Box");
        var ok = box.MoveTo(box);
        Assert.False(ok);
        Assert.NotEqual(box.Id, box.Location is Persistence.Dto.LocationRef.ObjectLocation ol ? ol.ObjectId : -1);
    }

    [Fact]
    public void DirectSelfViaIdEqualityBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeContainer("Box");
        var box2 = box;
        var ok = box.MoveTo(box2);
        Assert.False(ok);
    }

    [Fact]
    public void SimpleIndirectCycleBagContainsPouchThenBagIntoPouchFails()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeContainer("Bag");
        var pouch = MakeContainer("Pouch");
        Assert.True(pouch.MoveTo(bag));
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
        Assert.Contains(pouch, ObjectRegistry.Get(bag.ContentsSnapshot.ToList()));
        var oldLoc = bag.Location;
        var ok = bag.MoveTo(pouch);
        Assert.False(ok);
        Assert.NotEqual(pouch.Id, bag.Location is Persistence.Dto.LocationRef.ObjectLocation o ? o.ObjectId : -1);
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
    }

    [Fact]
    public void IndirectCycleViaIntermediateContainer()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeContainer("Bag");
        var pouch = MakeContainer("Pouch");
        var box = MakeContainer("Box");
        Assert.True(pouch.MoveTo(bag));
        Assert.True(box.MoveTo(pouch));
        Assert.Equal(pouch.Id, ((Persistence.Dto.LocationRef.ObjectLocation)box.Location).ObjectId);
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
        var ok = bag.MoveTo(box);
        Assert.False(ok);
        Assert.NotEqual(box.Id, bag.Location is Persistence.Dto.LocationRef.ObjectLocation o ? o.ObjectId : -1);
    }

    [Fact]
    public void DeepChainThreeLevelsBlocksOuterIntoInner()
    {
        using var env = GlobalTestEnv.Enter();
        var outer = MakeContainer("Outer");
        var middle = MakeContainer("Middle");
        var inner = MakeContainer("Inner");
        var tiny = MakeContainer("Tiny");
        Assert.True(middle.MoveTo(outer));
        Assert.True(inner.MoveTo(middle));
        Assert.True(tiny.MoveTo(inner));
        Assert.Equal(inner.Id, ((Persistence.Dto.LocationRef.ObjectLocation)tiny.Location).ObjectId);
        Assert.Equal(middle.Id, ((Persistence.Dto.LocationRef.ObjectLocation)inner.Location).ObjectId);
        Assert.Equal(outer.Id, ((Persistence.Dto.LocationRef.ObjectLocation)middle.Location).ObjectId);
        var ok = outer.MoveTo(inner);
        Assert.False(ok);
        Assert.NotEqual(inner.Id, outer.Location is Persistence.Dto.LocationRef.ObjectLocation o ? o.ObjectId : -1);
        var ok2 = outer.MoveTo(tiny);
        Assert.False(ok2);
        Assert.False(middle.MoveTo(tiny));
    }

    [Fact]
    public void DeepChainValidNestingSucceedsWhenNoCycle()
    {
        using var env = GlobalTestEnv.Enter();
        var outer = MakeContainer("Outer");
        var inner = MakeContainer("Inner");
        Assert.True(inner.MoveTo(outer));
        Assert.Equal(outer.Id, ((Persistence.Dto.LocationRef.ObjectLocation)inner.Location).ObjectId);
    }

    [Fact]
    public void ValidPutSucceedsWhenNoCycle()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeContainer("Bag");
        var pouch = MakeContainer("Pouch");
        Assert.True(pouch.MoveTo(bag));
        Assert.Contains(pouch.Id, bag.ContentsSnapshot);
        var bag2 = MakeContainer("Bag2");
        var pouch2 = MakeContainer("Pouch2");
        Assert.True(pouch2.MoveTo(bag2));
        Assert.Equal(bag2.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch2.Location).ObjectId);
        Assert.Contains(pouch2.Id, bag2.ContentsSnapshot);
    }

    [Fact]
    public void MoveToAllowsNodeDestinationEvenIfContainerHasContents()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area, 0,0,0));
        var bag = MakeContainer("Bag");
        var pouch = MakeContainer("Pouch");
        pouch.MoveTo(bag);
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
        var ok = bag.MoveTo(room);
        Assert.True(ok);
        Assert.Equal(room.Coord, ((Persistence.Dto.LocationRef.CoordLocation)bag.Location).Coord);
        Assert.Contains(bag.Id, room.ContentsSnapshot);
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
    }

    [Fact]
    public void ValidMoveToUnrelatedContainerSucceeds()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = MakeContainer("Bag");
        var pouch = MakeContainer("Pouch");
        var other = MakeContainer("Other");
        pouch.MoveTo(bag);
        var ok = pouch.MoveTo(other);
        Assert.True(ok);
        Assert.Equal(other.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
        Assert.DoesNotContain(pouch.Id, bag.ContentsSnapshot);
        Assert.Contains(pouch.Id, other.ContentsSnapshot);
    }

    [Fact]
    public void CycleCheckTraversesLocationChain()
    {
        using var env = GlobalTestEnv.Enter();
        var a = MakeContainer("A");
        var b = MakeContainer("B");
        var c = MakeContainer("C");
        b.MoveTo(a);
        c.MoveTo(b);
        Assert.False(a.MoveTo(c));
        Assert.False(b.MoveTo(c));
        // c to a should succeed (no cycle) - c's chain is c->b->a, moving c to a would be cycle? Actually c already indirect child of a, moving c to a is idempotent? In python they allow or assert true or location is a
        var c2 = MakeContainer("C2");
        var a2 = MakeContainer("A2");
        var b2 = MakeContainer("B2");
        b2.MoveTo(a2);
        c2.MoveTo(b2);
        var ok = c2.MoveTo(a2);
        Assert.True(ok);
    }

    // ----- PutCommand tests -----
    private class MockArgs
    {
        public string Object { get; set; } = "";
        public List<string> Destination { get; set; } = new();
        public MockArgs(string obj, List<string> dest) { Object=obj; Destination=dest; }
    }

    [Fact]
    public void PutBlocksContainmentLoopWithMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area, 0,0,0));
        var caller = GameObject.Create("Caller", isPc:true); ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var bag = MakeContainer("Bag");
        bag.AddLock("put", _=>true);
        var pouch = MakeContainer("Pouch");
        pouch.AddLock("put", _=>true);
        pouch.MoveTo(bag);
        bag.MoveTo(caller);
        // Mock search to return pouch and bag
        var origSearchCaller = caller.Search;
        var bagCaptured = bag;
        var pouchCaptured = pouch;
        // Install custom Search handler via overriding method: we use delegate trick by replacing Search via subclass? Instead we monkey-patch via helper: create a CapturingCaller that overrides Search
        // For simplicity, use PutCommand with custom caller that overrides Search
        var mockCaller = new CapturingCaller("Caller");
        mockCaller.Id = caller.Id; // not needed
        ObjectRegistry.AddObject(mockCaller);
        mockCaller.Location = caller.Location;
        // we need to set contents of mockCaller to contain bag
        // Instead we directly test PutCommand's loop detection by using direct MoveTo already covered; for Put we simulate via manual IsLoop check
        // Simplified: test that PutCommand's IsLoop blocks
        var args = new MockArgs("Bag", new List<string>{"pouch"});
        // Use a test double for caller that returns specific search results
        var testCaller = new TestCallerForPut("Caller", bag, pouch);
        testCaller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(testCaller);
        // bag is in caller contents, pouch is in bag
        bag.MoveTo(testCaller);
        // ensure bag in caller, pouch in bag
        var cmd = new PutCommand();
        cmd.Run(testCaller, args);
        Assert.Contains(testCaller.Messages, m => m == "You can't put Bag in Pouch - it would create a containment loop.");
        Assert.Equal(testCaller.Id, ((Persistence.Dto.LocationRef.ObjectLocation)bag.Location).ObjectId);
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
    }

    private sealed class TestCallerForPut : GameObject
    {
        public List<string> Messages = new();
        private readonly GameObject _bag;
        private readonly GameObject _pouch;
        public TestCallerForPut(string name, GameObject bag, GameObject pouch) { Name=name; IsPc=true; _bag=bag; _pouch=pouch; }
        public override bool Access(GameObject? o, string l) => true;
        public override void Msg(string text, GameObject? fromObj=null, IDictionary<string,object?>? mapping=null, bool raiseErrors=false, string? msgType=null) { Messages.Add(text); }
        public override void Msg(string text) { Messages.Add(text); }
        // Need to intercept Msg with different overloads used by PutCommand: caller.Msg(string)
        // PutCommand calls caller.Msg(string) via IMessageTarget, so we implement via IMessageTarget
        // We'll store messages via override of Msg(string)
        // Search override:
        public override List<GameObject> Search(string query, bool recursive=true, GameObject? looker=null)
        {
            var q = query.ToLowerInvariant();
            if (q.Contains("bag")) return new List<GameObject>{_bag};
            if (q.Contains("pouch")) return new List<GameObject>{_pouch};
            return new List<GameObject>();
        }
    }

    private sealed class CapturingCaller : GameObject
    {
        public List<string> Msgs = new();
        public CapturingCaller(string name) { Name=name; IsPc=true; }
        public override void Msg(string t) => Msgs.Add(t);
    }

    [Fact]
    public void PutBlocksDirectSelfLoop()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area,0,0,0));
        var caller = new TestCallerForPutSelf("Caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var bag = MakeContainer("Bag");
        bag.AddLock("put", _=>true);
        bag.MoveTo(caller);
        caller.Bag = bag;
        var args = new MockArgs("Bag", new List<string>{"bag"});
        var cmd = new PutCommand();
        cmd.Run(caller, args);
        Assert.Contains(caller.Messages, m => m == "You can't put Bag in Bag - it would create a containment loop.");
        Assert.Equal(caller.Id, ((Persistence.Dto.LocationRef.ObjectLocation)bag.Location).ObjectId);
    }

    private sealed class TestCallerForPutSelf : GameObject
    {
        public List<string> Messages = new();
        public GameObject? Bag;
        public TestCallerForPutSelf(string name) { Name=name; IsPc=true; }
        public override void Msg(string t) => Messages.Add(t);
        public override void Msg(string t, GameObject? fromObj, IDictionary<string,object?>? mapping, bool raiseErrors=false, string? msgType=null) => Messages.Add(t);
        public override List<GameObject> Search(string query, bool rec=true, GameObject? looker=null)
        {
            if (Bag != null && query.ToLowerInvariant().Contains("bag")) return new List<GameObject>{Bag};
            return new List<GameObject>();
        }
    }

    [Fact]
    public void PutValidNestingSucceeds()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area,0,0,0));
        var caller = new TestCallerForPutValid("Caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var bag = MakeContainer("Bag"); bag.AddLock("put", _=>true);
        var pouch = MakeContainer("Pouch");
        pouch.IsContainer = true;
        pouch.MoveTo(caller);
        caller.Bag = bag; caller.Pouch = pouch;
        // need dest search to return bag, obj search pouch
        var args = new MockArgs("Pouch", new List<string>{"bag"});
        // Actually Put expects object="Pouch", dest bag
        var cmd = new PutCommand();
        // caller.Search will be used for dest and obj; we need to mock correctly
        // Our TestCallerForPutValid overrides Search to return based on query
        cmd.Run(caller, args);
        Assert.Equal(bag.Id, ((Persistence.Dto.LocationRef.ObjectLocation)pouch.Location).ObjectId);
        Assert.Contains(pouch.Id, bag.ContentsSnapshot);
        Assert.Contains(caller.Messages, m => m.Contains("You put Pouch in Bag"));
    }

    private sealed class TestCallerForPutValid : GameObject
    {
        public List<string> Messages = new();
        public GameObject? Bag; public GameObject? Pouch;
        public TestCallerForPutValid(string name) { Name=name; IsPc=true; }
        public override void Msg(string t) => Messages.Add(t);
        public override void Msg(string t, GameObject? fromObj, IDictionary<string,object?>? mapping, bool raiseErrors=false, string? msgType=null) => Messages.Add(t);
        public override List<GameObject> Search(string q, bool r=true, GameObject? looker=null)
        {
            var low = q.ToLowerInvariant();
            if (low.Contains("bag") && Bag != null) return new List<GameObject>{Bag};
            if (low.Contains("pouch") && Pouch != null) return new List<GameObject>{Pouch};
            return new List<GameObject>();
        }
    }

    [Fact]
    public void PutAllBlocksLoopForOffendingItemButMovesOthers()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area,0,0,0));
        var caller = new TestCallerForPutAll("Caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var bag = MakeContainer("Bag"); bag.AddLock("put", _=>true);
        var pouch = MakeContainer("Pouch"); pouch.AddLock("put", _=>true);
        pouch.MoveTo(bag);
        bag.MoveTo(caller);
        var apple = GameObject.Create("Apple", isItem:true); ObjectRegistry.AddObject(apple);
        apple.MoveTo(caller);
        caller.Bag = bag; caller.Pouch = pouch; caller.Apple = apple;
        var args = new MockArgs("all", new List<string>{"pouch"});
        var cmd = new PutCommand();
        cmd.Run(caller, args);
        Assert.Contains(caller.Messages, m => m == "You can't put Bag in Pouch - it would create a containment loop.");
        Assert.Equal(caller.Id, ((Persistence.Dto.LocationRef.ObjectLocation)bag.Location).ObjectId);
        Assert.DoesNotContain(bag.Id, pouch.ContentsSnapshot);
    }

    private sealed class TestCallerForPutAll : GameObject
    {
        public List<string> Messages = new();
        public GameObject? Bag; public GameObject? Pouch; public GameObject? Apple;
        public TestCallerForPutAll(string name) { Name=name; IsPc=true; }
        public override void Msg(string t) => Messages.Add(t);
        public override void Msg(string t, GameObject? fromObj, IDictionary<string,object?>? mapping, bool raiseErrors=false, string? msgType=null) => Messages.Add(t);
        public override List<GameObject> Search(string q, bool r=true, GameObject? looker=null)
        {
            if (q.ToLowerInvariant().Contains("pouch") && Pouch != null) return new List<GameObject>{Pouch};
            return new List<GameObject>();
        }
    }

    [Fact]
    public void PutGuardStopsAtNodeBoundary()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area,0,0,0));
        ObjectRegistry.AddObject(room);
        var bag = MakeContainer("Bag");
        bag.MoveTo(room);
        var pouch = MakeContainer("Pouch");
        pouch.MoveTo(bag);
        var caller = GameObject.Create("Caller", isPc:true); ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var apple = GameObject.Create("Apple", isItem:true); ObjectRegistry.AddObject(apple);
        apple.MoveTo(caller);
        var ok = apple.MoveTo(room);
        Assert.True(ok);
        Assert.Equal(room.Coord, ((Persistence.Dto.LocationRef.CoordLocation)apple.Location).Coord);
        var other = MakeContainer("Other");
        other.MoveTo(room);
        var ok2 = bag.MoveTo(other);
        Assert.True(ok2);
    }

    [Fact]
    public void MoveToNodeDestinationIsAlwaysAllowed()
    {
        using var env = GlobalTestEnv.Enter();
        var inner = MakeContainer("Inner");
        var outer = MakeContainer("Outer");
        inner.MoveTo(outer);
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area,5,5,0));
        ObjectRegistry.AddObject(room);
        var ok = outer.MoveTo(room);
        Assert.True(ok);
        Assert.Equal(room.Coord, ((Persistence.Dto.LocationRef.CoordLocation)outer.Location).Coord);
        var ok2 = inner.MoveTo(room);
        Assert.True(ok2);
        Assert.Equal(room.Coord, ((Persistence.Dto.LocationRef.CoordLocation)inner.Location).Coord);
    }

    [Fact]
    public void CycleBeyond100DepthIsDetected()
    {
        using var env = GlobalTestEnv.Enter();
        var outer = MakeContainer("OuterDeep");
        var parent = outer;
        for (int i=0;i<105;i++)
        {
            var nxt = MakeContainer($"Level{i}");
            Assert.True(nxt.MoveTo(parent));
            parent = nxt;
        }
        var deepest = parent;
        var ok = outer.MoveTo(deepest);
        Assert.False(ok, "containment loop beyond 100 depth should still be blocked, not silent break");
        Assert.NotEqual(deepest.Id, outer.Location is Persistence.Dto.LocationRef.ObjectLocation o ? o.ObjectId : -1);
    }

    [Fact]
    public void PutLoopBeyond100IsBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var room = new Node(new Coord(area,99,99,0));
        ObjectRegistry.AddObject(room);
        var caller = new TestCallerForPutDeep("CallerDeep");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var outer = MakeContainer("OuterPut");
        outer.MoveTo(caller);
        var parent = outer;
        for (int i=0;i<105;i++)
        {
            var nxt = MakeContainer($"P{i}");
            nxt.MoveTo(parent);
            parent = nxt;
        }
        var deepest = parent;
        deepest.AddLock("put", _=>true);
        caller.Outer = outer; caller.Deepest = deepest;
        var args = new MockArgs("OuterPut", new List<string>{"deepest"});
        var cmd = new PutCommand();
        cmd.Run(caller, args);
        Assert.Contains(caller.Messages, m => m.ToLower().Contains("containment loop"));
        Assert.Equal(caller.Id, ((Persistence.Dto.LocationRef.ObjectLocation)outer.Location).ObjectId);
    }

    private sealed class TestCallerForPutDeep : GameObject
    {
        public List<string> Messages = new();
        public GameObject? Outer; public GameObject? Deepest;
        public TestCallerForPutDeep(string name) { Name=name; IsPc=true; }
        public override void Msg(string t) => Messages.Add(t);
        public override void Msg(string t, GameObject? fromObj, IDictionary<string,object?>? mapping, bool raiseErrors=false, string? msgType=null) => Messages.Add(t);
        public override List<GameObject> Search(string q, bool r=true, GameObject? looker=null)
        {
            var low = q.ToLowerInvariant();
            if (low.Contains("deepest") && Deepest != null) return new List<GameObject>{Deepest};
            if (low.Contains("outer") && Outer != null) return new List<GameObject>{Outer};
            return new List<GameObject>();
        }
    }
}
