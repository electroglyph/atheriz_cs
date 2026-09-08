using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Objects.VerbConjugation;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Audit;

// Fourth-pass regression tests for audit2.md §1 (Objects/).
// Each test FAILS while the finding is present and PASSES once fixed,
// except STRUCK findings (O-P0-1, O-P3-4, N1) which pin the verified-correct
// behavior and PASS. No production code touched.
[Collection("Ported")]
public class AuditObjectsTests
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
        public override bool AtDelete(GameObject caller) => throw new InvalidOperationException("veto-boom");
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

    // O-P0-1 STRUCK (pin: setter is sequential, no ABBA; delete-then-subscribe refused).
    [Fact]
    public void O_P0_1_ChannelDeleteSetter_IsSequentialAndSubscribeRefused()
    {
        Reset();
        try
        {
            var ch = Channel.Create("audit_ch_p01");
            ch.IsDeleted = true;
            Assert.True(ch.IsDeleted);
            var obj = GameObject.Create("o");
            ObjectRegistry.AddObject(obj);
            obj.Subscribe(ch);
            Assert.DoesNotContain(ch.Id, obj.ChannelsSnapshot);
        }
        finally { Reset(); }
    }

    // O-P1-1: container MsgContents must reach live sessions, not just the msg log.
    [Fact]
    public void O_P1_1_ContainerMsgContents_ForwardsToLiveSession()
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

    // O-P1-2: Equals-by-Id must imply equal hash codes.
    [Fact]
    public void O_P1_2_SameIdObjects_HashEqual()
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

    // O-P1-3: a throwing after-hook must propagate, not null the hooked result.
    [Fact]
    public void O_P1_3_ThrowingAfterHook_Propagates()
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

    // O-P1-4 PARTIAL: Node.Coord must be lock-guarded, not a bare auto-property.
    [Fact]
    public void O_P1_4_NodeCoord_IsLockGuarded()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Node.cs");
        Assert.DoesNotContain("public Coord Coord { get; set; }", src);
    }

    // O-P1-5: exit installation must run after the two location locks release.
    [Fact]
    public void O_P1_5_ExitInstall_RunsOutsideLocationLocks()
    {
        Reset();
        try
        {
            var nodeA = new Node(new Coord("auditmove", 0, 0, 0));
            var nodeB = new Node(new Coord("auditmove", 1, 0, 0));
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

    // O-P1-6: value equality must hold for ==/!=, and no single-slot move state.
    [Fact]
    public void O_P1_6_FollowIdentity_UsesValueEquality()
    {
        Assert.NotNull(typeof(GameObject).GetMethod("op_Equality", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(typeof(FollowScript).GetField("_oldLoc", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    // O-P1-7: Unpuppet stack-pop + puppet-rewire must be one critical section.
    [Fact]
    public void O_P1_7_Unpuppet_IsSingleCriticalSection()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Puppet.cs");
        var region = AuditScan.Region(src, "public bool Unpuppet(Session session)");
        Assert.Equal(1, AuditScan.Count(region, "lock (session.Lock)"));
    }

    // O-P1-8: AtDisconnect puppet unwind must run inside the session lock.
    [Fact]
    public void O_P1_8_AtDisconnect_UnwindsInsideLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Session.cs");
        int snap = src.IndexOf("stack = new List<(GameObject? Prev", StringComparison.Ordinal);
        int loop = src.IndexOf("while (stack.Count > 0)", StringComparison.Ordinal);
        Assert.True(snap >= 0 && loop > snap);
        var between = src.Substring(snap, loop - snap);
        Assert.DoesNotContain("        }", between);
    }

    // O-P1-9 + N9: Node.Delete must emit its own delete op, detach, and unregister.
    [Fact]
    public void O_P1_9_NodeDelete_RemovesSelfOpAndRegistry()
    {
        Reset();
        try
        {
            var node = new Node(new Coord("auditdel", 0, 0, 0));
            var res = node.Delete(null, true);
            Assert.NotNull(res);
            Assert.Contains(res.Value.ops, o => o is ValueTuple<string, object[]> t && t.Item1.StartsWith("DELETE"));
            Assert.Empty(ObjectRegistry.Get(node.Id));
        }
        finally { Reset(); }
    }

    // O-P1-10: Account.ToDto must snapshot fields under SyncRoot.
    [Fact]
    public void O_P1_10_AccountToDto_TakesLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var region = AuditScan.Region(src, "public override GameObjectDto ToDto()");
        Assert.True(region.Contains("EnterReadLock") || region.Contains("ReadScope") || region.Contains("ReadChars()"));
    }

    // O-P1-11: failed Login must leave a live session's flag untouched.
    [Fact]
    public void O_P1_11_FailedLogin_PreservesLoggedIn()
    {
        Reset();
        try
        {
            var acc = Account.Create("auditlogin", "pw", saltOverride: "testsalt");
            Assert.True(acc.Login("auditlogin", "pw", saltOverride: "testsalt"));
            Assert.True(acc.LoggedIn);
            Assert.False(acc.Login("auditlogin", "wrong", saltOverride: "testsalt"));
            Assert.True(acc.LoggedIn);
        }
        finally { Reset(); }
    }

    // O-P1-12: rehydrated pc-view predicate must match the in-memory one.
    [Fact]
    public void O_P1_12_PcView_RoundTripsUnchanged()
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

    // O-P1-13: failed subclass construction must not publish half-built nodes.
    [Fact]
    public void O_P1_13_ThrowingNodeCtor_LeavesRegistryUnchanged()
    {
        Reset();
        try
        {
            int before = ObjectRegistry.FilterBy(_ => true).Count;
            Assert.Throws<InvalidOperationException>(() => new BoomNode(new Coord("auditctor", 0, 0, 0)));
            Assert.Equal(before, ObjectRegistry.FilterBy(_ => true).Count);
        }
        finally { Reset(); }
    }

    // O-P1-14: unknown verbs get the documented generic +s third-person fallback.
    [Fact]
    public void O_P1_14_UnknownVerb_ThirdPersonAddsS()
    {
        var (second, third) = Conjugate.VerbActorStanceComponents("florp");
        Assert.Equal("florp", second);
        Assert.Equal("florps", third);
    }

    // O-P1-15: equal grids (same-Id nodes) must hash equal.
    [Fact]
    public void O_P1_15_EqualGrids_HashEqual()
    {
        Reset();
        try
        {
            var g1 = new NodeGrid("auditgrid", 0);
            var g2 = new NodeGrid("auditgrid", 0);
            var n1 = new Node(new Coord("auditgrid", 0, 0, 0));
            var n2 = new Node(new Coord("auditgrid", 0, 0, 0));
            n2.Id = n1.Id;
            g1.AddNode(n1);
            g2.AddNode(n2);
            Assert.Equal(g1, g2);
            Assert.Equal(g1.GetHashCode(), g2.GetHashCode());
        }
        finally { Reset(); }
    }

    // O-P2-1: non-recursive Delete must unregister exactly once.
    [Fact]
    public void O_P2_1_NonRecursiveDelete_UnregistersOnce()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Delete.cs");
        var region = AuditScan.Region(src, "now delete self");
        Assert.Equal(1, AuditScan.Count(region, "ObjectRegistry.RemoveObject(this)"));
    }

    // O-P2-2: hook marker classification must be cached, not reflected per dispatch.
    [Fact]
    public void O_P2_2_HookDispatch_UsesNoPerCallReflection()
    {
        var script = AuditScan.Read("src", "Atheriz.Core", "Objects", "Script.cs");
        Assert.DoesNotContain("GetType().GetMethods", script);
        var hooks = AuditScan.Read("src", "Atheriz.Core", "Objects", "Hooks.cs");
        Assert.DoesNotContain("GetCustomAttributes", hooks);
    }

    // O-P2-3 PARTIAL (fourth pass): only the GetNoun fallback scan is dead;
    // AddNoun/RemoveNoun sweeps normalize mixed-case keys from the load path.
    [Fact]
    public void O_P2_3_GetNounFallbackScan_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Node.Links.cs");
        var region = AuditScan.Region(src, "public string? GetNoun(string key)");
        Assert.DoesNotContain("foreach (var kv in Nouns)", region);
    }

    // O-P2-4: EmitSound pool-reject path must log instead of an empty block.
    [Fact]
    public void O_P2_4_EmitSoundReject_Logs()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Hear.cs");
        var region = AuditScan.Region(src, "public void EmitSound(");
        Assert.DoesNotContain("// log warning", region);
    }

    // O-P2-5: JSON serialization must happen after the write lock releases.
    [Fact]
    public void O_P2_5_SaveSerializes_AfterLockRelease()
    {
        var channel = AuditScan.Read("src", "Atheriz.Core", "Objects", "Channel.cs");
        var buildOps = AuditScan.Region(channel, "private (string Sql, object[] Params) BuildSaveOps");
        Assert.True(buildOps.IndexOf("ExitWriteLock", StringComparison.Ordinal) < buildOps.IndexOf("ToJson", StringComparison.Ordinal));
        var account = AuditScan.Read("src", "Atheriz.Core", "Objects", "Account.cs");
        var getOps = AuditScan.Region(account, "public override (string Sql, object[] Params) GetSaveOps()");
        Assert.True(getOps.IndexOf("ExitWriteLock", StringComparison.Ordinal) < getOps.IndexOf("ToJson", StringComparison.Ordinal));
    }

    // O-P2-6: failed MoveTo in AtPostPuppet must not render the wrong room's map.
    [Fact]
    public void O_P2_6_FailedReentry_SendsNoMap()
    {
        Reset();
        try
        {
            var node = new Node(new Coord("auditmap", 0, 0, 0));
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
            Assert.DoesNotContain(conn.SentCommands, c => c == "map_enable");
        }
        finally { Reset(); }
    }

    // O-P2-7: verb table must not depend on process CWD probes.
    [Fact]
    public void O_P2_7_VerbTable_HasNoCwdProbes()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "VerbConjugation", "Conjugate.cs");
        Assert.DoesNotContain("\"atheriz/objects/verb_conjugation/verbs.txt\"", src);
        Assert.DoesNotContain("Path.Combine(\"src\", \"Atheriz.Core\"", src);
    }

    // O-P2-8: Pronouns must reuse GameUtils.CopyWordCase (no private duplicate).
    [Fact]
    public void O_P2_8_Pronouns_ReusesSharedCopyWordCase()
    {
        Assert.Null(typeof(Pronouns).GetMethod("CopyWordCase", BindingFlags.Static | BindingFlags.NonPublic));
    }

    // O-P2-9: RemoveHooks must be a single target-equality pass (no self-assign).
    [Fact]
    public void O_P2_9_RemoveHooks_IsSinglePass()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Script.cs");
        var region = AuditScan.Region(src, "public void RemoveHooks(");
        Assert.DoesNotContain("hooksDict[name] = set;", region);
        Assert.DoesNotContain("var toRemove = set.Where(d => d.Method == method", region);
    }

    // O-P2-10: move validation must be a shared O(n) validator, not duplicated O(n^2).
    [Fact]
    public void O_P2_10_MoveValidation_IsSharedLinear()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "NodeGrid.cs");
        Assert.DoesNotContain("sources.Count(s =>", src);
    }

    // O-P2-11: sub-second sessions must accrue playtime (monotonic clock).
    [Fact]
    public void O_P2_11_SubSecondSession_AccruesPlaytime()
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

    // O-P2-12: underscore aliases must be complete across flags.
    [Fact]
    public void O_P2_12_FlagAliases_AreComplete()
    {
        Assert.True(new Flags().TrySet("_isNpc", true));
    }

    // O-P2-13: type failures must surface as arity failures, not NRE/InvalidCast.
    [Fact]
    public void O_P2_13_InvokerTypeFailure_IsArityFailure()
    {
        Action<int> f = _ => { };
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { null }));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { "x" }));
    }

    // O-P2-14: mismatched generic callables must fail as ParsingError, not InvalidCast.
    [Fact]
    public void O_P2_14_MismatchedCallable_RaisesParsingError()
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

    // O-P2-15 PARTIAL: AtMapUpdate must not stamp success when delivery failed.
    [Fact]
    public void O_P2_15_FailedMapSend_LeavesLastMapTime()
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

    // O-P2-16: fallback selection must be the collapsed single expression.
    [Fact]
    public void O_P2_16_FallbackBranch_IsCollapsed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Node.cs");
        Assert.DoesNotContain("else if (loc == obj)", src);
    }

    // O-P2-17: dead Session.PuppetRestore must be removed (or wired; removal chosen).
    [Fact]
    public void O_P2_17_DeadPuppetRestore_Removed()
    {
        Assert.Null(typeof(Session).GetProperty("PuppetRestore"));
    }

    // O-P2-18 PARTIAL: throwing AtCreate must be contained like GameObject.Create.
    [Fact]
    public void O_P2_18_ThrowingAtCreate_StillRegisters()
    {
        Reset();
        try
        {
            var acc = Account.Create<BoomAccount>("auditboom" + Guid.NewGuid().ToString("N"), "pw", saltOverride: "testsalt");
            Assert.NotEmpty(ObjectRegistry.Get(acc.Id));
        }
        finally { Reset(); }
    }

    // O-P2-19: Puppet pre-check + mutate must be one critical section.
    [Fact]
    public void O_P2_19_Puppet_IsSingleCriticalSection()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Puppet.cs");
        var region = AuditScan.Region(src, "public bool Puppet(Session session, GameObject npc)");
        Assert.Equal(1, AuditScan.Count(region, "lock (session.Lock)"));
    }

    // O-P3-1: dead null checks on non-nullable fields + snake_case statics.
    [Fact]
    public void O_P3_1_DeadNullChecksAndStatics_Removed()
    {
        var go = AuditScan.Read("src", "Atheriz.Core", "Objects", "GameObject.cs");
        Assert.DoesNotContain("_tags == null", go);
        Assert.DoesNotContain("_scripts == null", go);
        Assert.DoesNotContain("public static bool _is_thread_safe", go);
        foreach (var f in new[] { "Node.cs", "Account.cs", "Channel.cs", "Script.cs" })
        {
            var src = AuditScan.Read("src", "Atheriz.Core", "Objects", f);
            Assert.DoesNotContain("new static bool _is_thread_safe", src);
        }
    }

    // O-P3-2: load path must not assign through the no-op Node.Name setter.
    [Fact]
    public void O_P3_2_NodeLoad_DoesNotWriteNameProperty()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Persistence", "Dto", "NodeDtos.cs");
        Assert.DoesNotContain("node.Name =", src);
    }

    // O-P3-3: FollowScript.Delete must not silently hide the base member.
    [Fact]
    public void O_P3_3_FollowDelete_DoesNotHideBase()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "FollowScript.cs");
        Assert.True(
            !Regex.IsMatch(src, @"public\s+bool\s+Delete\s*\(") ||
            Regex.IsMatch(src, @"public\s+new\s+bool\s+Delete\s*\("));
    }

    // O-P3-4 STRUCK (pin: Transition.Lock IS taken in RemapTransitions).
    [Fact]
    public void O_P3_4_TransitionLock_IsUsed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.Partial.cs");
        Assert.Contains("trans.Lock.EnterWriteLock()", src);
    }

    // O-P3-5: channel timestamps must be 64-bit (no 2038 truncation).
    [Fact]
    public void O_P3_5_ChannelTimestamps_Are64Bit()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Channel.cs");
        Assert.DoesNotContain("(int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()", src);
        Assert.DoesNotContain("record ChannelHistoryEntry(int Timestamp", src);
    }

    // O-P3-6: Session state must not be bare public mutable fields.
    [Fact]
    public void O_P3_6_SessionState_IsEncapsulated()
    {
        var fields = typeof(Session).GetFields(BindingFlags.Instance | BindingFlags.Public);
        Assert.Empty(fields);
    }

    // N1 STRUCK by fourth-pass experiment: FixedTimeEquals returns false on
    // length mismatch (verified 2026-09-07 via scratch net8.0 console), so
    // empty-hash auth fails closed. Pin that behavior.
    [Fact]
    public void N1_EmptyHashCheckPassword_FailsClosed()
    {
        Reset();
        try
        {
            var acc = new Account();
            Assert.False(acc.CheckPassword("anything", saltOverride: "testsalt"));
        }
        finally { Reset(); }
    }

    // N2: ClearHistory must not nest _histLock -> SyncRoot.
    [Fact]
    public void N2_ClearHistory_SetsFlagOutsideHistLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Channel.cs");
        var region = AuditScan.Region(src, "public void ClearHistory()");
        Assert.Matches(@"lock\s*\(_histLock\)\s*\{\s*_history\.Clear\(\);\s*\}", region);
    }

    // N3: HasLinkName and GetLinkByName must agree on casing.
    [Fact]
    public void N3_LinkNameChecks_AgreeOnCase()
    {
        Reset();
        try
        {
            var node = new Node(new Coord("auditlink", 0, 0, 0));
            node.AddLink(new NodeLink("North", new Coord("auditlink", 0, 1, 0)));
            Assert.True(node.HasLinkName("north"));
            Assert.NotNull(node.GetLinkByName("NORTH"));
        }
        finally { Reset(); }
    }

    // N4: RemoveHooks must fetch the hook dict under the child write lock.
    [Fact]
    public void N4_RemoveHooks_FetchesDictUnderLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Script.cs");
        var region = AuditScan.Region(src, "public void RemoveHooks(");
        Assert.True(region.IndexOf("EnterWriteLock", StringComparison.Ordinal) < region.IndexOf("HooksRawNoLock", StringComparison.Ordinal));
    }

    // N5: throwing AtDelete in recursive collect must be logged, not swallowed.
    [Fact]
    public void N5_ThrowingAtDeleteChild_IsLogged()
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

    // N6: the public PuppetStack surface must not expose the live list.
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

    // N7: Msg must log + forward in a single critical section.
    [Fact]
    public void N7_Msg_UsesSingleCriticalSection()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Messaging", "GameObjectMessaging.cs");
        var region = AuditScan.Region(src, "public virtual void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors");
        Assert.DoesNotContain("EnterReadLock", region);
    }

    // N8: TeardownDeleted must leave no dangling log/hook/content residue.
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

    // N10: MaxMessageSize must respect raiseErrors:false like the rest of the parser.
    [Fact]
    public void N10_OversizeInput_RespectsRaiseErrorsFalse()
    {
        var big = new string('y', FuncParser.MaxMessageSize + 1);
        Assert.Equal(big, new FuncParser().Parse(big, raiseErrors: false));
    }

    // N11: coord-destination lookup must use an index, not a full registry scan.
    [Fact]
    public void N11_MoveLookup_UsesNoFullScan()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Move.cs");
        Assert.DoesNotContain("FilterBy(o => o.IsNode)", src);
    }

    // N12: one container's resolver failure must not drop remaining siblings.
    [Fact]
    public void N12_GatherContents_ContinuesPastFailure()
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

    // N13 (fourth pass): NodeLink equality must include Aliases (ToString shows them).
    [Fact]
    public void N13_NodeLinksWithDifferentAliases_AreNotEqual()
    {
        var c = new Coord("auditn13", 0, 0, 0);
        Assert.NotEqual(new NodeLink("n", c, ["a"]), new NodeLink("n", c, ["b"]));
    }

    // N14 (fourth pass): GetDisplayName must read the looker outside the node lock.
    [Fact]
    public void N14_GetDisplayName_ReadsLookerOutsideNodeLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Node.Links.cs");
        var region = AuditScan.Region(src, "public override string GetDisplayName(");
        Assert.True(region.IndexOf("looker.IsBuilder", StringComparison.Ordinal) < region.IndexOf("EnterReadLock", StringComparison.Ordinal));
    }

    // N15 (fourth pass): msg-log bound comments disagree with the code (50 vs 200).
    [Fact]
    public void N15_MsgLogBoundComments_AgreeWithCode()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Objects", "Messaging", "GameObjectMessaging.cs");
        Assert.DoesNotContain("(limit 50)", src);
    }
}
