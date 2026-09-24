using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Atheriz.Core;
using System.Reflection;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from MakeIterGenericTests.cs
// Single generic shape MakeIter<T>(T?): matching values yield one, typed
// sequences pass through, strings never enumerate as char sequences, and
// null yields one default (never empty — callers iterate unconditionally).
public class MakeIterGenericTests
{
    [Fact]
    public void MakeIter_MatchingValue_YieldsSingle()
    {
        Assert.Equal(new[] { 5 }, GameUtils.MakeIter(5));
        Assert.Equal(new[] { "hi" }, GameUtils.MakeIter("hi"));
    }

    [Fact]
    public void MakeIter_Null_YieldsSingleNull()
    {
        var strings = GameUtils.MakeIter<string?>(null).ToList();
        Assert.Single(strings);
        Assert.Null(strings[0]);
        var nullables = GameUtils.MakeIter<int?>(null).ToList();
        Assert.Single(nullables);
        Assert.Null(nullables[0]);
    }

    [Fact]
    public void MakeIter_String_YieldsSingle_NotChars()
    {
        Assert.Equal(new[] { "ab" }, GameUtils.MakeIter("ab"));
    }

    [Fact]
    public void MakeIter_TypedSequence_PassesThrough()
    {
        // Explicit T: inference from the sequence itself would wrap it.
        // (Only element-compatible sequences pass through: a string[] is
        // not an IEnumerable<string> value, so strings always yield single.)
        Assert.Equal(new object[] { 1, 2 }, GameUtils.MakeIter<object>(new object[] { 1, 2 }));
    }

    [Fact]
    public void MakeIter_NoFuncParserCopy_SingleShape()
    {
        // The object?-shaped twin in FuncParserHelpers had zero callers;
        // GameUtils.MakeIter<T> is the only iteration helper left.
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "FuncParser", "FuncParserHelpers.cs");
        Assert.DoesNotContain("IsIter", src);
        Assert.DoesNotContain("MakeIter", src);
    }
}

// Merged from SingleFetchHelperTests.cs
[Collection("Ported")]
public sealed class SingleFetchHelperTests
{
    [Fact]
    public void GetSingle_MatchesListFetch_AndNullWhenMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("Solo");
        ObjectRegistry.AddObject(o);
        Assert.Same(o, ObjectRegistry.GetSingle(o.Id));
        Assert.Same(ObjectRegistry.Get(o.Id).FirstOrDefault(), ObjectRegistry.GetSingle(o.Id));
        Assert.Null(ObjectRegistry.GetSingle(-999));
        Assert.True(ObjectRegistry.TryGetSingle(o.Id, out var found) && ReferenceEquals(o, found));
        Assert.False(ObjectRegistry.TryGetSingle(-999, out _));
    }

    [Fact]
    public void Puppet_HashId_KeepsOwnMessages_NotBanWording()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("Builder", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var prop = GameObject.Create("Rock", isItem: true);
        ObjectRegistry.AddObject(prop);
        // Session first: puppet without one reports "no active session"
        // (established PuppetWithoutSessionMessages behavior).
        var sess = new Session();
        caller.Session = sess;
        sess.Puppet = caller;
        // Malformed id keeps the puppet-specific format message...
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["#x"]));
        Assert.Contains(caller.PeekMessages(), m => m == "Invalid ID format. Use #<number>.");
        caller.ClearMessages();
        // ...unknown id keeps the plain not-found message...
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["#999999"]));
        Assert.Contains(caller.PeekMessages(), m => m == "No object found with ID 999999.");
        caller.ClearMessages();
        // ...and a non-PC #id is NOT refused with ban wording (puppet
        // accepts any object; the IsPc gate lives in BanHelper only).
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["#" + prop.Id]));
        Assert.DoesNotContain(caller.PeekMessages(), m => m.Contains("ban"));
    }

    [Fact]
    public void Channel_AliasInsert_EvictsOnlyStaleKey()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var caller = GameObject.Create("Chatter", isPc: true);
            ObjectRegistry.AddObject(caller);
            caller.ClearMessages();
            var channel = Channel.Create("public");
            var cmd = new ChannelCommand();
            cmd.Run(caller, cmd.Parser!.ParseArgs(["-c", "public"]));
            Assert.True(ChannelCommand.TryGetCached("public", out _));
            // Rename + resolve under the new name: the stale key goes away,
            // the new key caches — the same surviving entry as the old scan.
            channel.Name = "pub2";
            cmd.Run(caller, cmd.Parser!.ParseArgs(["-c", "pub2"]));
            Assert.False(ChannelCommand.TryGetCached("public", out _));
            Assert.True(ChannelCommand.TryGetCached("pub2", out var cached));
            Assert.Same(channel, cached);
        }
        finally { ChannelCommand.ClearCache(); }
    }
}

// Merged from SaltFetchSingleCallTests.cs
// One salt fetch: the catch-retry invoked the identical read with no backoff,
// no state change and no logging between attempts, so failure throws
// identically either way. Setup behavior is covered by the setup suites.
[Collection("Ported")]
public class SaltFetchSingleCallTests
{
    [Fact]
    public void SaltFetch_HasNoCatchRetry()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        var region = SourceScan.Region(src, "// Account + character");
        Assert.Contains("string saltVal = SaltProvider.GetSalt(absSecret);", region);
        Assert.DoesNotContain("catch { saltVal", region);
        Assert.Equal(1, SourceScan.Count(region, "GetSalt(absSecret)"));
    }
}

// Merged from FollowerDrainHelperTests.cs
[Collection("Ported")]
public sealed class FollowerDrainHelperTests
{
    private static (Node room, GameObject leader, GameObject follower) Setup(string area)
    {
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var room = new Node(new Coord(area, 0, 0, 0));
        grid.AddNode(room);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(room);
        GameObject MakePc(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            o.Location = new LocationRef.CoordLocation(room.Coord);
            room.AddObject(o);
            o.ClearMessages();
            return o;
        }
        return (room, MakePc("Leader"), MakePc("Follower"));
    }

    [Fact]
    public void Unfollow_DrainedLeader_LosesFollowScript()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("drain1");
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        Assert.Single(leader.GetScriptsByType("FollowScript"));
        new UnfollowCommand().Run(follower, null);
        Assert.Equal("You stop following.", follower.PeekMessages().Last());
        Assert.Empty(leader.GetScriptsByType("FollowScript"));
    }

    [Fact]
    public void Nofollow_DrainedSelf_LosesFollowScript()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("drain2");
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        Assert.Single(leader.GetScriptsByType("FollowScript"));
        new NofollowCommand().Run(leader, null);
        Assert.Empty(leader.GetScriptsByType("FollowScript"));
        Assert.Null(follower.Following);
    }

    [Fact]
    public void Nofollow_WithRemainingBuilderFollower_KeepsFollowScript()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, leader, follower) = Setup("drain3");
        var builder = GameObject.Create("Arch", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        // Colocated + connected like the established nofollow fixtures:
        // search runs from the caller's location under the view lock.
        builder.IsConnected = true;
        builder.Location = new LocationRef.CoordLocation(room.Coord);
        room.AddObject(builder);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        new FollowCommand().Run(builder, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        Assert.Equal(2, leader.FollowersSnapshot.Count);
        new NofollowCommand().Run(leader, null);
        // The builder follower is kept, so the set never drains and the script stays.
        Assert.Contains(builder.Id, leader.FollowersSnapshot);
        Assert.Single(leader.GetScriptsByType("FollowScript"));
    }
}

// Merged from FollowScriptGetSingleTests.cs
// at_post_move resolves each follower id with a single lookup: a stale id
// (follower gone from the registry) is skipped exactly like the old
// empty-list branch, while live colocated followers still auto-move.
[Collection("Ported")]
public sealed class FollowScriptGetSingleTests
{
    private static (Node n1, Node n2, GameObject leader, GameObject follower) Setup(string area)
    {
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0)));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1);
        grid.AddNode(n2);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(n1);
        ObjectRegistry.AddObject(n2);
        GameObject MakePc(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            o.Location = LocationRef.FromCoord(n1.Coord);
            n1.AddObject(o);
            o.ClearMessages();
            return o;
        }
        return (n1, n2, MakePc("Leader"), MakePc("Follower"));
    }

    [Fact]
    public void PostMove_SkipsStaleFollowerId_MovesLiveFollower()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, n2, leader, follower) = Setup("stalefollow");
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(["Leader"]));
        // Stale id: created but never registered, so no object resolves.
        var ghost = GameObject.Create("Ghost");
        leader.AddFollower(ghost.Id);
        Assert.True(leader.MoveTo(n2, toExit: "north"));
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)leader.Location).Coord);
    }

    [Fact]
    public void PostMove_RemovedFollowerId_LeaderStillMoves()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, n2, leader, follower) = Setup("removedfollow");
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(["Leader"]));
        ObjectRegistry.RemoveObject(follower);
        Assert.True(leader.MoveTo(n2, toExit: "north"));
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)leader.Location).Coord);
    }
}

// Merged from FactoryPassThroughTests.cs
// Collapsed pass-through wrappers: the ctor guard, null-means-default paths,
// and the optional upsert configuration keep identical runtime behavior.
[Collection("Ported")]
public class FactoryPassThroughTests
{
    [Fact]
    public void ClosedFactory_Create_ThrowsClosedWithoutReopening()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizDbContextFactory.CloseDatabase();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => AtherizDbContextFactory.Create(env.TempPath));
            Assert.Contains("database is closed", ex.Message);
        }
        finally { AtherizDbContextFactory.ReopenDatabase(); }
    }

    [Fact]
    public void Journal_ParameterlessOverloads_UseDefaultPath()
    {
        using var env = GlobalTestEnv.Enter();
        CheckpointJournal.MarkDirty();
        Assert.True(CheckpointJournal.IsDirty());
        CheckpointJournal.MarkClean();
        Assert.False(CheckpointJournal.IsDirty());
    }

    [Fact]
    public void UpsertJson_WithoutConfigure_InsertsAndUpdates()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        DbTransactionHelper.UpsertJson(db.GameTime,
            () => db.GameTime.Find(0), () => new GameTimeRow { Id = 0 }, "{}");
        db.SaveChanges();
        Assert.Equal("{}", db.GameTime.Find(0)!.Data);
        DbTransactionHelper.UpsertJson(db.GameTime,
            () => db.GameTime.Find(0), () => new GameTimeRow { Id = 0 }, """{"t":1}""");
        db.SaveChanges();
        db.ChangeTracker.Clear();
        Assert.Equal("""{"t":1}""", db.GameTime.Find(0)!.Data);
    }

    [Fact]
    public void UpsertJson_OptionalConfigure_SetsDiscriminator()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        DbTransactionHelper.UpsertJson(db.Objects,
            () => db.Objects.Find(9), () => new ObjectRow { Id = 9, Type = "object", Version = 1 },
            "{}", row => row.Type = "script");
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var row = db.Objects.Find(9);
        Assert.NotNull(row);
        Assert.Equal("{}", row!.Data);
        Assert.Equal("script", row.Type);
    }
}

// Merged from DelegateInvokerCacheTests.cs
// Delegate invocation: parameter metadata is snapshotted on first use, so
// repeat calls reuse it and arity/type mismatches keep throwing the same
// exception as the uncached path.
[Collection("Ported")]
public class DelegateInvokerCacheTests
{
    [Fact]
    public void Invoke_ReusesMetadata_AcrossCalls()
    {
        Func<int, int> f = x => x * 2;

        Assert.Equal(42, DelegateInvoker.Invoke(f, new object?[] { 21 }));
        Assert.Equal(42, DelegateInvoker.Invoke(f, new object?[] { 21 }));
    }

    [Fact]
    public void Invoke_MismatchedArityOrType_ThrowsParameterCount()
    {
        Func<int, int> f = x => x * 2;

        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, Array.Empty<object?>()));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { 1, 2 }));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { "x" }));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { null }));
    }
}
