using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Shared contents resolution: ForContents and MsgContents resolve through
// one core whether the caller passes a resolver or falls back to the
// registry, honoring excludes in both shapes.
[Collection("Ported")]
public class ContentsResolutionTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    private static (GameObject room, GameObject a, GameObject b) Setup()
    {
        var room = GameObject.Create("room", isContainer: true);
        var a = GameObject.Create("alpha");
        var b = GameObject.Create("beta");
        RegisterAll(room, a, b);
        Assert.True(a.MoveTo(room));
        Assert.True(b.MoveTo(room));
        return (room, a, b);
    }

    [Fact]
    public void ForContents_Exclude_SkipsListed()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (room, a, b) = Setup();
            var seen = new List<int>();
            room.ForContents(o => { seen.Add(o.Id); }, exclude: [a]);
            Assert.DoesNotContain(a.Id, seen);
            Assert.Contains(b.Id, seen);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ForContents_NullKwargs_PassesEmptyDict()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (room, _, _) = Setup();
            IDictionary<string, object?>? got = null;
            room.ForContents((o, d) => { got = d; });
            Assert.NotNull(got);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ForContents_ResolverPath_Resolves()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (room, a, _) = Setup();
            var seen = new List<int>();
            room.ForContents(o => { seen.Add(o.Id); }, resolver: id => id == a.Id ? a : null);
            Assert.Equal([a.Id], seen);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MsgContents_ResolverPath_DeliversAndExcludes()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (room, a, b) = Setup();
            a.ClearMessages();
            b.ClearMessages();
            room.MsgContents("hi there", exclude: [a], resolver: id => ObjectRegistry.Get(id).FirstOrDefault());
            Assert.DoesNotContain(a.PeekMessages(), m => m.Contains("hi there"));
            Assert.Contains(b.PeekMessages(), m => m.Contains("hi there"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MsgContents_Substitution_ReachesReceivers()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (room, a, b) = Setup();
            b.ClearMessages();
            var mapping = new Dictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            room.MsgContents("Say {k}.", fromObj: a, mapping: mapping, exclude: [a]);
            Assert.Contains(b.PeekMessages(), m => m.Contains("Say v."));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
