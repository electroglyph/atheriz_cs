using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins for the TargetResolution.ResolveObject merge: ban and puppet resolve
// names through the shared resolver with their own filters and messages.
// Each pin fails if its verb regresses to the other's wording or shape.
[Collection("Ported")]
public sealed class TargetResolutionTests
{
    private static GameObject MakeBuilder(string name = "resbuilder")
    {
        var c = GameObject.Create(name, isPc: true, privilege: Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakePc(string name)
    {
        var pc = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(pc);
        pc.ClearMessages();
        return pc;
    }

    private static void AttachSession(GameObject caller)
    {
        var sess = new Session();
        caller.Session = sess;
        sess.Puppet = caller;
    }

    [Fact]
    public void TargetResolution_BanUnknownName_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeBuilder();
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Ghost"]));
        Assert.Contains(caller.PeekMessages(), m => m == "No player character found named 'Ghost'.");
    }

    [Fact]
    public void TargetResolution_BanMultiMatch_ListsIdsPerLine()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeBuilder();
        var a = MakePc("Dup");
        var b = MakePc("Dup");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Dup"]));
        var msgs = caller.PeekMessages();
        Assert.Contains(msgs, m => m == "Multiple matches for 'Dup':");
        Assert.Contains(msgs, m => m == $"  #{a.Id} Dup");
        Assert.Contains(msgs, m => m == $"  #{b.Id} Dup");
        Assert.False(a.IsBanned);
        Assert.False(b.IsBanned);
    }

    [Fact]
    public void TargetResolution_PuppetUnknownName_ReportsNoMatch()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeBuilder();
        AttachSession(caller);
        caller.ClearMessages();
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["ghost"]));
        Assert.Contains(caller.PeekMessages(), m => m == "No match found for 'ghost'.");
    }

    [Fact]
    public void TargetResolution_PuppetMultiMatch_ListsIdsSingleLine()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("resmerge", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var caller = MakeBuilder();
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        ObjectRegistry.AddObject(caller);
        var one = GameObject.Create("pebble", isItem: true);
        ObjectRegistry.AddObject(one);
        node.AddObject(one);
        var two = GameObject.Create("pebble", isItem: true);
        ObjectRegistry.AddObject(two);
        node.AddObject(two);
        AttachSession(caller);
        caller.ClearMessages();
        // The plural form collects every same-name match (a singular name
        // takes the first match by design, like the old verb did).
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["pebbles"]));
        Assert.Contains(caller.PeekMessages(),
            m => m == $"Multiple matches: #{one.Id} pebble, #{two.Id} pebble. Use #id to pick one.");
    }

    [Fact]
    public void TargetResolution_FilteredIdTarget_ReportsNotFound()
    {
        // The filter also gates the #id leg: a filtered-out id reports the
        // caller's notFound text, while ResolveById errors pass through
        // verbatim.
        using var env = GlobalTestEnv.Enter();
        var caller = MakeBuilder();
        var prop = GameObject.Create("prop", isItem: true);
        ObjectRegistry.AddObject(prop);
        var (t1, e1) = TargetResolution.ResolveObject(caller, $"#{prop.Id}", o => o.IsPc, "nope");
        Assert.Null(t1);
        Assert.Equal("nope", e1);
        var (t2, e2) = TargetResolution.ResolveObject(caller, "#x", o => o.IsPc, "nope");
        Assert.Null(t2);
        Assert.Equal("Invalid ID format. Use #<number>.", e2);
    }

    [Fact]
    public void TargetResolution_BanMultiMatch_PreservesRegistryOrder()
    {
        // The per-line multi list follows the global exact-name order, so a
        // reorder of the resolver union would fail here, not just Contains.
        using var env = GlobalTestEnv.Enter();
        var caller = MakeBuilder();
        var a = MakePc("OrdDup");
        var b = MakePc("OrdDup");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["OrdDup"]));
        var msgs = caller.PeekMessages().ToList();
        int header = msgs.IndexOf("Multiple matches for 'OrdDup':");
        Assert.True(header >= 0);
        Assert.Equal(
            new List<string> { $"  #{a.Id} OrdDup", $"  #{b.Id} OrdDup" },
            msgs.Skip(header + 1).Take(2).ToList());
    }

    [Fact]
    public void TargetResolution_PuppetIgnoresDistantSameName()
    {
        // Unfiltered puppet search is local only: a same-named object two
        // rooms away must not resolve (no global-index leakage).
        using var env = GlobalTestEnv.Enter();
        var here = new Node(new Coord("resfar", 0, 0, 0));
        ObjectRegistry.AddObject(here);
        var far = new Node(new Coord("resfar", 9, 9, 9));
        ObjectRegistry.AddObject(far);
        var caller = MakeBuilder();
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(here.Coord);
        here.AddObject(caller);
        ObjectRegistry.AddObject(caller);
        var distant = GameObject.Create("widget", isItem: true);
        ObjectRegistry.AddObject(distant);
        far.AddObject(distant);
        AttachSession(caller);
        caller.ClearMessages();
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["widget"]));
        Assert.Contains(caller.PeekMessages(), m => m == "No match found for 'widget'.");
    }
}
