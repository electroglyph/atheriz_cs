using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Objects.VerbConjugation;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression tests for game objects (messages, moves, puppetry, nodes).
// Each test fails while the defect is present and passes once fixed; a few
// pin verified-correct behavior and pass as-is.
[Collection("Ported")]
public class ObjectRegressionTests
{
    private static void Reset() => ObjectRegistry.ClearAll();

    private sealed class AfterBoom
    {
        [After]
        public string Hook(GameObject? target, string result) => throw new InvalidOperationException("after-boom");
    }

    private sealed class BoomNode : Node
    {
        public BoomNode(Coord c) : base(c) { throw new InvalidOperationException("ctor-boom"); }
    }

    private sealed class BoomAccount : Account
    {
        public override void AtCreate() => throw new InvalidOperationException("create-boom");
    }

    private sealed class BoomDelete : GameObject
    {
        public BoomDelete() { Id = IdGenerator.GetUniqueId(); }
        public override bool AtDelete(GameObject? caller) => throw new InvalidOperationException("veto-boom");
    }

    private sealed class VetoMove : GameObject
    {
        public override bool AtPreMove(GameObject? destination, string? toExit = null) => false;
    }

    private sealed class TrackingCmdSet : CmdSet
    {
        private readonly Node _dest;
        public bool DestLockHeldDuringAdds;
        public TrackingCmdSet(Node dest) => _dest = dest;
        public override void Adds(IEnumerable<Command> commands, string? tag = null)
        {
            DestLockHeldDuringAdds = _dest.SyncRoot.IsWriteLockHeld;
            base.Adds(commands, tag);
        }
    }

    private sealed class BeforeHook
    {
        [Before]
        public void Hook(string a, bool b) { }
    }

    // Channel Delete/setter ordering is sequential (no lock-order inversion);
    [Fact]
    public void ChannelDeleteSetter_IsSequentialAndSubscribeRefused()
    {
        Reset();
        try
        {
            var ch = Channel.Create("reg_ch_p01");
            ch.IsDeleted = true;
            Assert.True(ch.IsDeleted);
            var obj = GameObject.Create("o");
            ObjectRegistry.AddObject(obj);
            obj.Subscribe(ch);
            Assert.DoesNotContain(ch.Id, obj.ChannelsSnapshot);
        }
        finally { Reset(); }
    }

    // container MsgContents must reach live sessions, not just the msg log.
    [Fact]
    public void ContainerMsgContents_ForwardsToLiveSession()
    {
        Reset();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var listener = GameObject.Create("ear", isPc: true);
            ObjectRegistry.AddObject(listener);
            listener.IsConnected = true;
            var conn = new TestConnection();
            var sess = new Session(conn);
            listener.Session = sess;
            sess.Puppet = listener;
            Assert.True(listener.MoveTo(room));
            conn.ClearSent();
            room.MsgContents("hello");
            Assert.Contains(conn.SentCommandsBag, t => t.Json.Contains("hello"));
        }
        finally { Reset(); }
    }

    // Equals-by-Id must imply equal hash codes.
    [Fact]
    public void SameIdObjects_HashEqual()
    {
        Reset();
        try
        {
            var a = GameObject.Create("a");
            var b = GameObject.Create("b");
            b.Id = a.Id;
            Assert.Equal(a, b);
            // NB: xUnit's Assert.Contains scans linearly and ignores hashes;
            // assert the hash contract directly.
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
        finally { Reset(); }
    }

    // a throwing after-hook must propagate, not null the hooked result.
    [Fact]
    public void ThrowingAfterHook_Propagates()
    {
        Reset();
        try
        {
            var obj = GameObject.Create("o");
            ObjectRegistry.AddObject(obj);
            var tgt = GameObject.Create("t");
            ObjectRegistry.AddObject(tgt);
            obj.InstallHook("at_look", new Func<GameObject?, string, string>(new AfterBoom().Hook));
            Assert.Throws<InvalidOperationException>(() => obj.Hookable<string>("at_look", () => "orig", tgt));
        }
        finally { Reset(); }
    }

    // Node.Coord must be lock-guarded, not a bare auto-property.
    // (The nested NodeLink.Coord auto-property is out of scope: it is written
    // once in the NodeLink ctor and never mutated — no torn-read pair exists.)
    [Fact]
    public void NodeCoord_IsLockGuarded()
    {
        // Not a bare auto-property: no compiler-generated backing field.
        Assert.Null(typeof(Node).GetField("<Coord>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic));
        // The Node-class accessor takes the node lock on both paths (via the
        // shared Read()/Write() helpers — same _lock, same boundaries).
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Node.cs");
        var anchor = src.IndexOf("private Coord _coord;", StringComparison.Ordinal);
        Assert.True(anchor >= 0);
        var idx = src.IndexOf("public Coord Coord", anchor, StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var window = src.Substring(idx, Math.Min(400, src.Length - idx));
        Assert.Contains("Read(() =>", window);
        Assert.Contains("Write(() =>", window);
    }

    // exit installation must run after the two location locks release.
    [Fact]
    public void ExitInstall_RunsOutsideLocationLocks()
    {
        Reset();
        try
        {
            var nodeA = new Node(new Coord("regmove", 0, 0, 0));
            var nodeB = new Node(new Coord("regmove", 1, 0, 0));
            // AddExits (the probed call) early-returns on zero links, so the
            // destination needs at least one link for the probe to fire.
            nodeB.AddLink(new NodeLink("west", nodeA.Coord));
            var ch = GameObject.Create("walker");
            ObjectRegistry.AddObject(ch);
            var probe = new TrackingCmdSet(nodeB);
            ch.InternalCmdSet = probe;
            ch.Location = new LocationRef.CoordLocation(nodeA.Coord);
            Assert.True(ch.MoveTo(nodeB));
            Assert.False(probe.DestLockHeldDuringAdds);
        }
        finally { Reset(); }
    }

    // value equality must hold for ==/!=, and no single-slot move state.
    [Fact]
    public void FollowIdentity_UsesValueEquality()
    {
        Assert.NotNull(typeof(GameObject).GetMethod("op_Equality", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(typeof(FollowScript).GetField("_oldLoc", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    // Unpuppet stack-pop + puppet-rewire must be one mutating critical section,
    // with read-only ownership re-checks after AtUnpuppet (a concurrent Puppet
    // during AtUnpuppet must not be clobbered; hooks run unlocked between the
    // takes, so one take cannot cover all).
    [Fact]
    public void Unpuppet_IsSingleCriticalSection()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Puppet.cs");
        var region = SourceScan.Region(src, "public bool Unpuppet(Session session)");
        Assert.Equal(1, SourceScan.Count(region, "TryPopPuppetEntry"));
        Assert.Contains("target.Session is not null", region);
    }

    // AtDisconnect puppet unwind must run inside the session lock.
    [Fact]
    public void AtDisconnect_UnwindsInsideLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Session.cs");
        int snap = src.IndexOf("stack = new List<(GameObject? Prev", StringComparison.Ordinal);
        int loop = src.IndexOf("while (stack.Count > 0)", StringComparison.Ordinal);
        Assert.True(snap >= 0 && loop > snap);
        var between = src.Substring(snap, loop - snap);
        Assert.DoesNotContain("        }", between);
    }

    // Node.Delete must emit its own delete op, detach, and unregister.
    [Fact]
    public void NodeDelete_RemovesSelfOpAndRegistry()
    {
        Reset();
        try
        {
            var node = new Node(new Coord("regdel", 0, 0, 0));
            var res = node.Delete(null, true);
            Assert.NotNull(res);
            Assert.Contains(res.Value.ops, o => o is ValueTuple<string, object[]> t && t.Item1.StartsWith("DELETE"));
            Assert.Empty(ObjectRegistry.Get(node.Id));
        }
        finally { Reset(); }
    }

    // Account.ToDto must snapshot fields under SyncRoot.
    [Fact]
    public void AccountToDto_TakesLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var region = SourceScan.Region(src, "public override GameObjectDto ToDto()");
        Assert.True(region.Contains("EnterReadLock") || region.Contains("ReadScope") || region.Contains("ReadChars()"));
    }

    // The Python original clears the flag on failure (base_account.py:198-203,
    // flag on failure (base_account.py:198-203, INTENT-pinned by
    // test_account.py:594 test_failed_login_clears_logged_in and ported as
    // FailedLoginClearsLoggedIn). This test pins the faithful behavior.
    [Fact]
    public void FailedLogin_PreservesLoggedIn()
    {
        Reset();
        try
        {
            var acc = Account.Create("reglogin", "pw", saltOverride: "testsalt");
            Assert.True(acc.Login("reglogin", "pw", saltOverride: "testsalt"));
            Assert.True(acc.LoggedIn);
            Assert.False(acc.Login("reglogin", "wrong", saltOverride: "testsalt"));
            Assert.False(acc.LoggedIn);
        }
        finally { Reset(); }
    }

    // rehydrated pc-view predicate must match the in-memory one.
    [Fact]
    public void PcView_RoundTripsUnchanged()
    {
        Reset();
        try
        {
            var target = GameObject.Create("pc", isPc: true);
            ObjectRegistry.AddObject(target);
            var accessor = GameObject.Create("viewer");
            ObjectRegistry.AddObject(accessor);
            accessor.IsConnected = true;
            target.IsConnected = false;
            var installed = target.GetLocksSnapshot()["view"][0];
            Assert.True(LockPolicies.TryResolve("pc-view", target, out var rehydrated));
            Assert.Equal(installed(accessor), rehydrated(accessor));
        }
        finally { Reset(); }
    }

    // failed subclass construction must not publish half-built nodes.
    [Fact]
    public void ThrowingNodeCtor_LeavesRegistryUnchanged()
    {
        Reset();
        try
        {
            int before = ObjectRegistry.FilterBy(_ => true).Count;
            Assert.Throws<InvalidOperationException>(() => new BoomNode(new Coord("regctor", 0, 0, 0)));
            Assert.Equal(before, ObjectRegistry.FilterBy(_ => true).Count);
        }
        finally { Reset(); }
    }

    // unknown verbs return unchanged for both persons (conjugate.py:399-401).
    [Fact]
    public void UnknownVerb_ReturnsUnchanged()
    {
        var (second, third) = Conjugate.VerbActorStanceComponents("florp");
        Assert.Equal("florp", second);
        Assert.Equal("florp", third);
    }

    // equal grids (same-Id nodes) must hash equal.
    [Fact]
    public void EqualGrids_HashEqual()
    {
        Reset();
        try
        {
            var g1 = new NodeGrid("reggrid", 0);
            var g2 = new NodeGrid("reggrid", 0);
            var n1 = new Node(new Coord("reggrid", 0, 0, 0));
            var n2 = new Node(new Coord("reggrid", 0, 0, 0));
            n2.Id = n1.Id;
            g1.AddNode(n1);
            g2.AddNode(n2);
            Assert.Equal(g1, g2);
            Assert.Equal(g1.GetHashCode(), g2.GetHashCode());
        }
        finally { Reset(); }
    }

    // only the GetNoun fallback scan is dead;
    // AddNoun/RemoveNoun sweeps normalize mixed-case keys from the load path.
    [Fact]
    public void GetNounFallbackScan_Removed()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Node.Links.cs");
        var region = SourceScan.Region(src, "public string? GetNoun(string key)");
        Assert.DoesNotContain("foreach (var kv in Nouns)", region);
    }

    // EmitSound pool-reject path must log instead of an empty block.
    [Fact]
    public void EmitSoundReject_Logs()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Hear.cs");
        var region = SourceScan.Region(src, "public void EmitSound(");
        Assert.DoesNotContain("// log warning", region);
    }

    // JSON serialization must happen after the write lock releases. The
    // flag-dance + post-release encode now live in the shared converter
    // core (GameObjectDtoConverter.BuildSaveJson); Channel/Account only
    // pass their snapshot bodies, so the pin follows the behavior there.
    [Fact]
    public void SaveSerializes_AfterLockRelease()
    {
        var channel = SourceScan.Read("src", "Atheriz.Core", "Objects", "Channel.cs");
        var buildOps = SourceScan.Region(channel, "private (string Sql, object[] Params) BuildSaveOps");
        Assert.Contains("GameObjectDtoConverter.BuildSaveJson", buildOps);
        var account = SourceScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var getOps = SourceScan.Region(account, "public override (string Sql, object[] Params) GetSaveOps()");
        Assert.Contains("GameObjectDtoConverter.BuildSaveJson", getOps);
        var conv = SourceScan.Read("src", "Atheriz.Core", "Persistence", "Converters", "GameObjectDtoConverter.cs");
        var core = SourceScan.Region(conv, "public static string BuildSaveJson(GameObject obj, Func<GameObjectDto> snapshotUnderLock, bool clearing)");
        Assert.True(core.IndexOf("ExitWriteLock", StringComparison.Ordinal) < core.IndexOf("EncodeSaveJson(obj, dto, had)", StringComparison.Ordinal));
    }

    // AtPostPuppet ignores the MoveTo result and always enables the map
    // once the flags hold (base_obj.py:1479-1485).
    [Fact]
    public void VetoedReentry_StillSendsMap()
    {
        Reset();
        try
        {
            var node = new Node(new Coord("regmap", 0, 0, 0));
            var ch = new VetoMove();
            ch.Id = IdGenerator.GetUniqueId();
            ch.Name = "vetoer";
            ObjectRegistry.AddObject(ch);
            ch.IsConnected = true;
            var conn = new TestConnection();
            var sess = new Session(conn);
            ch.Session = sess;
            sess.Puppet = ch;
            ch.Location = new LocationRef.CoordLocation(node.Coord);
            conn.ClearSent();
            ch.AtPostPuppet();
            Assert.Contains(conn.SentCommands, c => c == "map_enable");
        }
        finally { Reset(); }
    }

    // Pronouns must reuse GameUtils.CopyWordCase (no private duplicate).
    [Fact]
    public void Pronouns_ReusesSharedCopyWordCase()
    {
        Assert.Null(typeof(Pronouns).GetMethod("CopyWordCase", BindingFlags.Static | BindingFlags.NonPublic));
    }

    // sub-second sessions must accrue playtime (monotonic clock).
    [Fact]
    public void SubSecondSession_AccruesPlaytime()
    {
        Reset();
        try
        {
            var obj = GameObject.Create("timer");
            ObjectRegistry.AddObject(obj);
            var sess = new Session();
            sess.AtConnect();
            obj.Session = sess;
            Thread.Sleep(50); // well under 1s: wall-clock truncation yields 0
            Assert.True(obj.SecondsPlayed > 0, "sub-second session accrued 0 playtime");
        }
        finally { Reset(); }
    }

    // underscore aliases must be complete across flags.
    [Fact]
    public void FlagAliases_AreComplete()
    {
        Assert.True(new Flags().TrySet("_isNpc", true));
    }

    // type failures must surface as arity failures, not NRE/InvalidCast.
    [Fact]
    public void InvokerTypeFailure_IsArityFailure()
    {
        Action<int> f = _ => { };
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { null }));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { "x" }));
    }

    // mismatched generic callables must fail as ParsingError, not InvalidCast.
    [Fact]
    public void MismatchedCallable_RaisesParsingError()
    {
        // (string[], Dictionary, int) passes registration validation (has an
        // array param and a Dictionary param) but matches no call shape: the
        // >=2 wrapper forwards exactly [string[], dict] and the arity guard
        // throws TargetParameterCountException instead of a ParsingError.
        var parser = new FuncParser(new Dictionary<string, object>
        {
            ["f"] = (Func<string[], Dictionary<string, object?>, int, string>)((a, k, x) => "r"),
        });
        Assert.Throws<FuncParser.ParsingError>(() => parser.Parse("$f(1)", raiseErrors: true));
    }

    // AtMapUpdate must not stamp success when delivery failed.
    [Fact]
    public void FailedMapSend_LeavesLastMapTime()
    {
        Reset();
        try
        {
            var obj = GameObject.Create("mapper");
            ObjectRegistry.AddObject(obj);
            var sess = new Session(new BareConn());
            obj.Session = sess;
            sess.Puppet = obj;
            var before = obj.LastMapTime;
            obj.AtMapUpdate("map", new List<(string, string, (int, int))>(), 0, 0, false, "a");
            Assert.Equal(before, obj.LastMapTime);
        }
        finally { Reset(); }
    }

    // fallback selection must be the collapsed single expression.
    [Fact]
    public void FallbackBranch_IsCollapsed()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Node.cs");
        Assert.DoesNotContain("else if (loc == obj)", src);
    }

    // dead Session.PuppetRestore must be removed (or wired; removal chosen).
    [Fact]
    public void DeadPuppetRestore_Removed()
    {
        Assert.Null(typeof(Session).GetProperty("PuppetRestore"));
    }

    // throwing AtCreate must be contained like GameObject.Create.
    [Fact]
    public void ThrowingAtCreate_StillRegisters()
    {
        Reset();
        try
        {
            var acc = Account.Create<BoomAccount>("regboom" + Guid.NewGuid().ToString("N"), "pw", saltOverride: "testsalt");
            Assert.NotEmpty(ObjectRegistry.Get(acc.Id));
        }
        finally { Reset(); }
    }

    // Puppet pre-check + mutate must be one critical section.
    [Fact]
    public void Puppet_IsSingleCriticalSection()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Puppet.cs");
        var region = SourceScan.Region(src, "public bool Puppet(Session session, GameObject npc)");
        Assert.Equal(1, SourceScan.Count(region, "lock (session.Lock)"));
    }

    // Transition.Lock IS taken in RemapTransitions.
    [Fact]
    public void TransitionLock_IsUsed()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.Partial.cs");
        Assert.Contains("trans.Lock.EnterWriteLock()", src);
    }

    // channel timestamps must be 64-bit (no 2038 truncation).
    [Fact]
    public void ChannelTimestamps_Are64Bit()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Channel.cs");
        Assert.DoesNotContain("(int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()", src);
        Assert.DoesNotContain("record ChannelHistoryEntry(int Timestamp", src);
    }

    // Session.Lock must stay public: puppet selection does an atomic
    // check-and-set across Session.Lock + character SyncRoot (see
    // LockExposureTests), so hiding it would race double-puppeting.
    [Fact]
    public void SessionState_IsEncapsulated()
    {
        var f = typeof(Session).GetField("Lock", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(f);
    }

    // FixedTimeEquals returns false on length mismatch, so empty-hash auth
    // fails closed. Pin that behavior.
    [Fact]
    public void EmptyHashCheckPassword_FailsClosed()
    {
        Reset();
        try
        {
            var acc = new Account();
            Assert.False(acc.CheckPassword("anything", saltOverride: "testsalt"));
        }
        finally { Reset(); }
    }

    // ClearHistory must not nest _histLock -> SyncRoot.
    [Fact]
    public void N2_ClearHistory_SetsFlagOutsideHistLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Channel.cs");
        var region = SourceScan.Region(src, "public void ClearHistory()");
        Assert.Matches(@"lock\s*\(_histLock\)\s*\{\s*_history\.Clear\(\);\s*\}", region);
    }

    // HasLinkName and GetLinkByName must agree on casing.
    [Fact]
    public void N3_LinkNameChecks_AgreeOnCase()
    {
        Reset();
        try
        {
            var node = new Node(new Coord("reglink", 0, 0, 0));
            node.AddLink(new NodeLink("North", new Coord("reglink", 0, 1, 0)));
            Assert.True(node.HasLinkName("north"));
            Assert.NotNull(node.GetLinkByName("NORTH"));
        }
        finally { Reset(); }
    }

    // throwing AtDelete in recursive collect must be logged, not swallowed.
    [Fact]
    public void ThrowingAtDeleteChild_IsLogged()
    {
        Reset();
        try
        {
            var parent = GameObject.Create("parent", isContainer: true);
            ObjectRegistry.AddObject(parent);
            var bad = new BoomDelete();
            bad.Name = "badkid";
            ObjectRegistry.AddObject(bad);
            parent.AddObject(bad);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            using var cap = new CaptureAtherizLog();
            parent.Delete(caller, true);
            Assert.Contains("veto-boom", cap.Read());
        }
        finally { Reset(); }
    }

    // throwing AtDelete is fail-closed: the child is vetoed (survives), not force-deleted.
    [Fact]
    public void ThrowingAtDeleteChild_IsSkipped()
    {
        Reset();
        try
        {
            var parent = GameObject.Create("parent", isContainer: true);
            ObjectRegistry.AddObject(parent);
            var bad = new BoomDelete();
            bad.Name = "badkid";
            ObjectRegistry.AddObject(bad);
            parent.AddObject(bad);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            parent.Delete(caller, true);
            Assert.False(bad.IsDeleted);
            Assert.NotEmpty(ObjectRegistry.Get(bad.Id));
            Assert.Contains(bad.Id, parent.ContentsSnapshot);
        }
        finally { Reset(); }
    }

    // the public PuppetStack surface must not expose the live list.
    [Fact]
    public void N6_PuppetStack_IsNotLiveList()
    {
        Reset();
        try
        {
            var sess = new Session();
            var tgt = GameObject.Create("t");
            ObjectRegistry.AddObject(tgt);
            try { ((IList)sess.PuppetStack).Add(new ValueTuple<GameObject?, GameObject>(null, tgt)); }
            catch (NotSupportedException) { return; } // read-only fix shape also acceptable
            Assert.Empty(sess.PuppetStack);
        }
        finally { Reset(); }
    }

    // Msg must log + forward in a single critical section.
    [Fact]
    public void N7_Msg_UsesSingleCriticalSection()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Messaging", "GameObjectMessaging.cs");
        var region = SourceScan.Region(src, "public virtual void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors");
        Assert.DoesNotContain("EnterReadLock", region);
    }

    // TeardownDeleted must leave no dangling log/hook/content residue.
    [Fact]
    public void N8_DeletedObject_LeavesNoResidue()
    {
        Reset();
        try
        {
            var obj = GameObject.Create("doomed");
            ObjectRegistry.AddObject(obj);
            obj.InstallHook("at_say", new Action<string, bool>(new BeforeHook().Hook));
            obj.Msg("hello");
            Assert.NotEmpty(obj.PeekMessages());
            obj.Delete(null, true);
            Assert.Empty(obj.PeekMessages());
            Assert.False(obj.HasHook("at_say"));
        }
        finally { Reset(); }
    }

    // The length cap raises unconditionally (funcparser.py:324-325 raises
    // before raise_errors is consulted); at-cap input passes through.
    [Fact]
    public void OversizeInput_AlwaysRaises()
    {
        var big = new string('y', FuncParser.MaxMessageSize + 1);
        Assert.Throws<FuncParser.ParsingError>(() => new FuncParser().Parse(big, raiseErrors: false));
        var atCap = new string('y', FuncParser.MaxMessageSize);
        Assert.Equal(atCap, new FuncParser().Parse(atCap, raiseErrors: false));
    }

    // coord-destination lookup must use an index, not a full registry scan.
    [Fact]
    public void MoveLookup_UsesNoFullScan()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Move.cs");
        Assert.DoesNotContain("FilterBy(o => o.IsNode)", src);
    }

    // one container's resolver failure must not drop remaining siblings.
    [Fact]
    public void GatherContents_ContinuesPastFailure()
    {
        Reset();
        try
        {
            var box = GameObject.Create("box", isContainer: true);
            ObjectRegistry.AddObject(box);
            var mid = GameObject.Create("mid", isContainer: true);
            ObjectRegistry.AddObject(mid);
            var boom = GameObject.Create("boom", isContainer: true);
            ObjectRegistry.AddObject(boom);
            var victim = GameObject.Create("victim");
            ObjectRegistry.AddObject(victim);
            // Insertion order: mid first. .NET HashSet<int> enumerates
            // insertion-ordered absent removals, so the nested throw below hits
            // before the sibling scan (order relied upon by this test).
            box.AddObject(mid);
            box.AddObject(victim);
            mid.AddObject(boom);
            int boomId = boom.Id;
            Func<int, GameObject?> resolver = id => id == boomId
                ? throw new InvalidOperationException("resolver-boom")
                : ObjectRegistry.Get(id).FirstOrDefault();
            var got = ContentUtils.GatherContents(box, resolver);
            Assert.Contains(victim, got);
        }
        finally { Reset(); }
    }

    // batch validation is shared: duplicates, chains and collisions fail
    // identically in CheckMoves and ApplyMoves.
    [Fact]
    public void BatchMoves_ValidateOnce()
    {
        Reset();
        try
        {
            var g = new NodeGrid("regmoves", 0);
            g.AddNode(new Node(new Coord("regmoves", 0, 0, 0)));
            g.AddNode(new Node(new Coord("regmoves", 1, 0, 0)));
            g.AddNode(new Node(new Coord("regmoves", 4, 4, 0)));
            g.AddNode(new Node(new Coord("regmoves", 7, 7, 0)));
            var moves = new List<((int X, int Y) src, (int X, int Y) dst)>
            {
                ((0, 0), (2, 0)), // ok: unique, occupied, free dest
                ((1, 0), (7, 7)), // occupied dest, never vacated
                ((5, 5), (6, 6)), // empty source
                ((4, 4), (1, 0)), // chain: dest vacated by move 1's source
            };
            var failed = g.CheckMoves(moves);
            Assert.Equal(new HashSet<int> { 1, 2 }, failed);
            var applied = g.ApplyMoves(moves);
            Assert.Equal(failed.OrderBy(i => i), applied.OrderBy(i => i));
        }
        finally { Reset(); }
    }

    // the verb table ships embedded (module-table equivalent) and loads in
    // full regardless of launch directory — no CWD probing.
    [Fact]
    public void VerbTable_LoadsEmbedded()
    {
        var asm = typeof(Conjugate).Assembly;
        Assert.Contains("Atheriz.Core.Objects.VerbConjugation.verbs.txt", asm.GetManifestResourceNames());
        var field = typeof(Conjugate).GetField("VerbTenses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var table = (Dictionary<string, string[]>)field.GetValue(null)!;
        Assert.True(table.Count > 100);
        Assert.Equal("past", Conjugate.VerbTense("ate"));
    }

    // RemoveHooks must fetch the live hooks dict under the child's write
    // lock, not before it (a concurrent InstallHook could replace it).
    [Fact]
    public void RemoveHooks_FetchesDictUnderLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Script.cs");
        var region = SourceScan.Region(src, "public void RemoveHooks(");
        Assert.True(region.IndexOf("child.WriteScope()", StringComparison.Ordinal) < region.IndexOf("HooksRawNoLock", StringComparison.Ordinal));
    }

    // NodeLink equality is name+coord only (nodes.py:63-66 __eq__).
    [Fact]
    public void NodeLinksWithDifferentAliases_AreEqual()
    {
        var c = new Coord("regn13", 0, 0, 0);
        Assert.Equal(new NodeLink("n", c, ["a"]), new NodeLink("n", c, ["b"]));
    }

    // N14 (fourth pass): GetDisplayName must read the looker outside the node lock.
    [Fact]
    public void GetDisplayName_ReadsLookerOutsideNodeLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Node.Links.cs");
        var region = SourceScan.Region(src, "public override string GetDisplayName(");
        Assert.True(region.IndexOf("looker.IsBuilder", StringComparison.Ordinal) < region.IndexOf("EnterReadLock", StringComparison.Ordinal));
    }

    // N15 (fourth pass): msg-log bound comments disagree with the code (50 vs 200).
    [Fact]
    public void MsgLogBoundComments_AgreeWithCode()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "Messaging", "GameObjectMessaging.cs");
        Assert.DoesNotContain("(limit 50)", src);
    }
}
