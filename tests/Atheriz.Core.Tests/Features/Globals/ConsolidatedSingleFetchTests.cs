// Consolidated single-fetch sites: the five single-fetch sites that still allocated a list via
// ObjectRegistry.Get — GameObject.AtMapUpdate, GameObject.TryGetScript,
// GameObject.HasScriptType, Session.AtDisconnect temp cleanup, and
// GameObject.AtPostPuppet channel re-subscribe. Each site now uses
// ObjectRegistry.GetSingle with a null-check; fallback logic is unchanged
// (missing and wrong-typed ids stay unresolvable exactly as before, and
// HasScriptType keeps counting non-Script objects whose type name matches).
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

[Collection("Ported")]
public sealed class ConsolidatedSingleFetchTests
{
    private sealed class SingleFetchProbeScript : Script
    {
        [Before]
        public void at_probe_hook() { }
    }

    private static List<int> MapPos(TestConnection conn)
    {
        var sent = Assert.Single(conn.Sent, s => s.Cmd == "map");
        var payload = Assert.IsType<Dictionary<string, object?>>(sent.Args[0]);
        return Assert.IsType<List<int>>(payload["pos"]);
    }

    private static void AtMapUpdateWithLocation(GameObject obj, LocationRef loc, TestConnection conn)
    {
        obj.Location = loc;
        obj.Session = new Session(conn);
        obj.AtMapUpdate("m", new List<(string sym, string desc, (int x, int y) coord)>(), 10, 20, false, "a");
    }

    [Fact]
    public void AtMapUpdate_ObjectLocationOfNode_ResolvesNodeCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("probemap", 12, 18, 0));
        ObjectRegistry.AddObject(node);
        var obj = GameObject.Create("viewer");
        ObjectRegistry.AddObject(obj);
        var conn = new TestConnection();

        AtMapUpdateWithLocation(obj, new LocationRef.ObjectLocation(node.Id), conn);

        Assert.Equal(new List<int> { 2, 2 }, MapPos(conn));
    }

    [Fact]
    public void AtMapUpdate_MissingTarget_FallsBackToOrigin()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("viewer");
        ObjectRegistry.AddObject(obj);
        var conn = new TestConnection();

        AtMapUpdateWithLocation(obj, new LocationRef.ObjectLocation(299991), conn);

        Assert.Equal(new List<int> { 0, 0 }, MapPos(conn));
    }

    [Fact]
    public void AtMapUpdate_WrongTypeTarget_FallsBackToOrigin()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = GameObject.Create("plain");
        ObjectRegistry.AddObject(plain);
        var obj = GameObject.Create("viewer");
        ObjectRegistry.AddObject(obj);
        var conn = new TestConnection();

        AtMapUpdateWithLocation(obj, new LocationRef.ObjectLocation(plain.Id), conn);

        Assert.Equal(new List<int> { 0, 0 }, MapPos(conn));
    }

    [Fact]
    public void AddScript_ByIdResolves_InstallsHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var child = GameObject.Create("child");
        var script = new SingleFetchProbeScript();
        ObjectRegistry.AddObject(child);
        ObjectRegistry.AddObject(script);

        child.AddScript(script.Id);

        Assert.True(child.HasHook("at_probe_hook"));
        Assert.True(child.HasScriptType("SingleFetchProbe"));
        Assert.Single(child.GetScriptsByType("probe"));
    }

    [Fact]
    public void AddScript_MissingId_SilentlySkips()
    {
        using var env = GlobalTestEnv.Enter();
        var child = GameObject.Create("child");
        ObjectRegistry.AddObject(child);

        var ex = Record.Exception(() =>
        {
            child.AddScript(299990);
            child.RemoveScript(299990);
        });

        Assert.Null(ex);
        Assert.False(child.HasHook("at_probe_hook"));
        Assert.Empty(child.GetScriptsByType("probe"));
    }

    [Fact]
    public void AddScript_WrongTypeId_SilentlySkips()
    {
        using var env = GlobalTestEnv.Enter();
        var child = GameObject.Create("child");
        var plain = GameObject.Create("plain");
        ObjectRegistry.AddObject(child);
        ObjectRegistry.AddObject(plain);

        var ex = Record.Exception(() =>
        {
            child.AddScript(plain.Id);
            child.RemoveScript(plain.Id);
        });

        Assert.Null(ex);
        Assert.False(child.HasHook("at_probe_hook"));
        Assert.Empty(child.GetScriptsByType("SingleFetchProbe"));
    }

    [Fact]
    public void HasScriptType_FoundScript_ReturnsTrue()
    {
        using var env = GlobalTestEnv.Enter();
        var child = GameObject.Create("child");
        var script = new SingleFetchProbeScript();
        ObjectRegistry.AddObject(child);
        ObjectRegistry.AddObject(script);
        child.AddScript(script.Id);

        Assert.True(child.HasScriptType("SingleFetchProbe"));
    }

    [Fact]
    public void HasScriptType_MissingId_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        var child = GameObject.Create("child");
        ObjectRegistry.AddObject(child);
        child.AddScriptId(299989);

        Assert.False(child.HasScriptType("SingleFetchProbe"));
    }

    [Fact]
    public void HasScriptType_NonScriptNameMatch_ReturnsTrue()
    {
        using var env = GlobalTestEnv.Enter();
        var child = GameObject.Create("child");
        var plain = GameObject.Create("plain");
        ObjectRegistry.AddObject(child);
        ObjectRegistry.AddObject(plain);
        child.AddScriptId(plain.Id);

        // Unlike TryGetScript, HasScriptType keeps its direct fetch: a
        // non-Script object whose type name contains the needle still counts.
        Assert.True(child.HasScriptType("gameobject"));
        Assert.Empty(child.GetScriptsByType("gameobject"));
    }

    [Fact]
    public void AtDisconnect_ObjectLocationOfHolder_RemovesPuppetFromHolder()
    {
        using var env = GlobalTestEnv.Enter();
        var holder = GameObject.Create("holder");
        ObjectRegistry.AddObject(holder);
        var puppet = GameObject.Create("temp");
        puppet.IsTemporary = true;
        ObjectRegistry.AddObject(puppet);
        Assert.True(puppet.MoveTo(holder));
        var session = new Session { Puppet = puppet };

        session.AtDisconnect();

        Assert.DoesNotContain(puppet.Id, holder.ContentsSnapshot);
        Assert.IsType<LocationRef.NullLocation>(puppet.Location);
        Assert.True(puppet.IsDeleted);
    }

    [Fact]
    public void AtDisconnect_MissingLocationTarget_CompletesCleanupSilently()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("temp");
        puppet.IsTemporary = true;
        puppet.Location = new LocationRef.ObjectLocation(299988);
        ObjectRegistry.AddObject(puppet);
        var session = new Session { Puppet = puppet };

        var ex = Record.Exception(() => session.AtDisconnect());

        Assert.Null(ex);
        Assert.IsType<LocationRef.NullLocation>(puppet.Location);
        Assert.True(puppet.IsDeleted);
    }

    [Fact]
    public void AtDisconnect_NonHolderObjectTarget_CompletesCleanupSilently()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("b18target");
        var puppet = GameObject.Create("temp");
        puppet.IsTemporary = true;
        puppet.Location = new LocationRef.ObjectLocation(channel.Id);
        ObjectRegistry.AddObject(puppet);
        var session = new Session { Puppet = puppet };

        // No type gate on this path: an existing non-holder target resolves
        // and RemoveObject runs against it, exactly as before the conversion.
        var ex = Record.Exception(() => session.AtDisconnect());

        Assert.Null(ex);
        Assert.IsType<LocationRef.NullLocation>(puppet.Location);
        Assert.True(puppet.IsDeleted);
    }

    [Fact]
    public void AtPostPuppet_LiveChannelId_ResubscribesListener()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("b18chan");
        var puppet = GameObject.Create("puppet");
        ObjectRegistry.AddObject(puppet);
        puppet.AddChannelId(channel.Id);

        puppet.AtPostPuppet();

        Assert.Contains(puppet.Id, channel.Listeners);
    }

    [Fact]
    public void AtPostPuppet_MissingChannelId_SilentlySkips()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("puppet");
        ObjectRegistry.AddObject(puppet);
        puppet.AddChannelId(299987);

        var ex = Record.Exception(() => puppet.AtPostPuppet());

        Assert.Null(ex);
    }

    [Fact]
    public void AtPostPuppet_NonChannelId_IgnoresWithoutThrow()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = GameObject.Create("plain");
        ObjectRegistry.AddObject(plain);
        var puppet = GameObject.Create("puppet");
        ObjectRegistry.AddObject(puppet);
        puppet.AddChannelId(plain.Id);

        var ex = Record.Exception(() => puppet.AtPostPuppet());

        Assert.Null(ex);
    }
}
