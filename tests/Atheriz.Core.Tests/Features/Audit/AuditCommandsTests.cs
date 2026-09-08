using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Audit;

// Fourth-pass regression tests for audit2.md §3 (Commands/ + Menu/etc).
// Each test FAILS while the finding is present and PASSES once fixed,
// except STRUCK findings (C-P1-2 as NRE, C-P1-6 as unenforced, C-P2-10,
// C-P2-20, C-P2-33) which pin the verified-correct behavior and PASS.
// C-P3-6/C-P3-12 are documented observations (Python-fidelity / functional
// asymmetry): no failing test, see audit2.md notes. No production code touched.
[Collection("Ported")]
public class AuditCommandsTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        CommandDispatcher.SetSettings(new AtherizSettings());
    }

    private sealed class ReqArgCommand : Command
    {
        public override string Key => "audit_reqarg";
        public override IReadOnlyList<string> Aliases => ["ra"];
        public override void Run(IMessageTarget caller, object? args) => caller.Msg("ok");
        protected override void SetupParser(GameArgumentParser p) => p.AddArgument("target", help: "t");
    }

    private sealed class BoomName : GameObject
    {
        public BoomName() { Id = IdGenerator.GetUniqueId(); }
        public override string Name { get => throw new InvalidOperationException("name-boom"); set => base.Name = value; }
    }

    private sealed class VetoPut : GameObject
    {
        public VetoPut() { Id = IdGenerator.GetUniqueId(); }
        public override bool AtPrePut(GameObject putter, GameObject dest) => false;
        public override bool AtPreDrop(GameObject dropper) => false;
    }

    private sealed class BoomMap : MapHandler
    {
        public BoomMap() : base(autoLoad: false) { }
        public override void Save(bool force = false) => throw new InvalidOperationException("map-boom");
    }

    // C-P0-1: nested secrets must stay redacted through dict/enumerable recursion.
    [Fact]
    public void C_P0_1_NestedSecret_StaysRedacted()
    {
        var rendered = ExamFormatter.FormatValue(
            new Dictionary<string, object?> { ["password"] = "s3cret" }, "extra") as string ?? "";
        Assert.DoesNotContain("s3cret", rendered);
    }

    // C-P1-1: give must require possession; room-ground items stay put.
    [Fact]
    public void C_P1_1_GiveRoomGroundItem_RequiresPossession()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditgive", 0, 0, 0));
            nh.AddNode(node);
            var giver = GameObject.Create("giver", isPc: true);
            ObjectRegistry.AddObject(giver);
            giver.IsConnected = true;
            Assert.True(giver.MoveTo(node));
            var receiver = GameObject.Create("receiver", isPc: true);
            ObjectRegistry.AddObject(receiver);
            receiver.IsContainer = true;
            receiver.IsConnected = true;
            Assert.True(receiver.MoveTo(node));
            var rock = GameObject.Create("rock", isItem: true);
            ObjectRegistry.AddObject(rock);
            Assert.True(rock.MoveTo(node)); // room ground, NOT inventory
            var cmd = new GiveCommand();
            cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "rock", "receiver" }));
            Assert.Contains(rock.Id, node.ContentsSnapshot);
            Assert.DoesNotContain(rock.Id, receiver.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P1-2 STRUCK as NRE (pin: Prompt completes with "" on cancel, never null).
    [Fact]
    public async Task C_P1_2_PromptCancel_YieldsEmptyString()
    {
        Reset();
        try
        {
            var sess = new Session(new TestConnection());
            var task = sess.Prompt("Enter name:");
            Assert.True(sess.CancelPrompt(sess.InputFuture));
            Assert.Equal("", await task);
        }
        finally { Reset(); }
    }

    // C-P1-3: negative-number positionals keep type/Choices/list shape.
    [Fact]
    public void C_P1_3_NegativePositional_ConvertsToInt()
    {
        var p = new GameArgumentParser("audit");
        p.AddArgument("count", type: typeof(int));
        var args = p.ParseArgs(new[] { "-5" });
        Assert.Equal(-5, args.Get<int>("count", -999));
    }

    // C-P1-4: float failures must error like int failures (no silent strings).
    [Fact]
    public void C_P1_4_BadFloat_ThrowsCommandError()
    {
        var p = new GameArgumentParser("audit");
        p.AddArgument("--ratio", type: typeof(float));
        Assert.Throws<CommandError>(() => p.ParseArgs(new[] { "--ratio", "abc" }));
    }

    // C-P1-5: one throwing session accessor must not abort the whole exam list.
    [Fact]
    public void C_P1_5_ThrowingSessionMember_DoesNotAbortExam()
    {
        Reset();
        try
        {
            var sess = new Session(new TestConnection());
            sess.Puppet = new BoomName();
            var rendered = ExamFormatter.FormatValue(sess, "session") as string;
            Assert.NotNull(rendered);
        }
        finally { Reset(); }
    }

    // C-P1-6 STRUCK as "ban unenforced" (pin: banned IPs refused at registration).
    [Fact]
    public void C_P1_6_BannedIp_RefusedAtRegistration()
    {
        Reset();
        var prev = ConnectionManager.GlobalInstance;
        try
        {
            ObjectRegistry.BanIp("9.9.9.9");
            var mgr = new ConnectionManager();
            Assert.False(mgr.RegisterConnection("auditban", new TestConn("auditban", "9.9.9.9")));
        }
        finally { ObjectRegistry.UnbanIp("9.9.9.9"); ConnectionManager.GlobalInstance = prev; Reset(); }
    }

    // C-P1-7: a failing save must not silently skip the rest; feedback required.
    [Fact]
    public void C_P1_7_FailingSave_ReportsAndContinues()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            GlobalServices.SetMapHandler(new BoomMap());
            var go = GameObject.Create("saver", isPc: true);
            ObjectRegistry.AddObject(go);
            var ex = Record.Exception(() => new SaveCommand().Run(go, null));
            Assert.Null(ex);
            var savMsgs = string.Join("\n", go.PeekMessages()).Split('\n').Count(m => m.Contains("Sav"));
            Assert.True(savMsgs >= 2);
        }
        finally { Reset(); }
    }

    // C-P1-8: spam must not persist plaintext credentials (nor save per-account).
    [Fact]
    public void C_P1_8_Spam_PersistsNoPlaintextCredentials()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "spamaudit");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        try
        {
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var pa = new GameArgumentParser.ParsedArgs();
            pa["count"] = 1;
            new SpamCommand().Run(admin, pa);
            var creds = File.ReadAllText(Path.Combine(tmp, "spam_accounts.txt"));
            Assert.DoesNotContain("password1", creds);
        }
        finally { AtherizSettings.Global.SavePath = origSave; Reset(); }
    }

    // C-P1-9: disabled-verb demotion must reset args to the full stripped input.
    [Fact]
    public void C_P1_9_DisabledVerb_SuggestsFullInput()
    {
        Reset();
        CommandDispatcher.SetSettings(new AtherizSettings { AccountCreationEnabled = false });
        try
        {
            var conn = new TestConnection();
            var job = CommandDispatcher.ResolveUnloggedIn(conn, "create foo");
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Contains(conn.SentCommandsBag, t => t.Json.Contains("create foo"));
        }
        finally { Reset(); }
    }

    // C-P1-10: socials must resolve via the shared fallback (here-coords-ids).
    [Fact]
    public void C_P1_10_SocialHere_ResolvesLocation()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditsocial", 0, 0, 0));
            nh.AddNode(node);
            var go = GameObject.Create("smiler", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsConnected = true;
            var conn = new TestConnection();
            var sess = new Session(conn);
            go.Session = sess;
            sess.Puppet = go;
            Assert.True(go.MoveTo(node));
            conn.ClearSent();
            var cmd = new SocialsCommand();
            var pa = cmd.Parser!.ParseArgs(new[] { "here" });
            pa.CmdString = "smile";
            cmd.Run(go, pa);
            Assert.Contains(conn.SentCommandsBag, t => t.Json.Contains("smile"));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P1-11 PARTIAL: the hit path must check view BEFORE invoking AtLook.
    // (AtLook gates one layer down: its view-denial branch returns before
    // AtDesc, so no behavioral delta exists — this pins the requested
    // defense-in-depth call-site check instead.)
    [Fact]
    public void C_P1_11_LookHit_ChecksViewBeforeAtLook()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "LookCommand.cs");
        int hit = src.IndexOf("puppet.Msg(puppet.AtLook(found[0]))", StringComparison.Ordinal);
        Assert.True(hit >= 0);
        var before = src.Substring(0, hit);
        int found = before.LastIndexOf("SearchWithFallback", StringComparison.Ordinal);
        Assert.True(found >= 0);
        Assert.Contains("Access(puppet", before.Substring(found));
    }

    // C-P2-1: parser diagnostics must reach the caller, not just generic help.
    // NOTE: three existing pins assert the help-only shape; they must be
    // updated with this fix (see audit2.md notes).
    [Fact]
    public void C_P2_1_ParserError_SurfacesDiagnosis()
    {
        Reset();
        try
        {
            var go = GameObject.Create("caller");
            ObjectRegistry.AddObject(go);
            var cmd = new ReqArgCommand();
            var (func, _, _) = cmd.Execute(go, "", "audit_reqarg");
            Assert.Null(func);
            Assert.Contains(go.PeekMessages(), m => m.Contains("required"));
        }
        finally { Reset(); }
    }

    // C-P2-2: -h/--help inside free-text values must stay data.
    [Fact]
    public void C_P2_2_HelpTokenInValue_StaysData()
    {
        var p = new GameArgumentParser("say");
        p.AddArgument("message", nargs: "REMAINDER");
        var args = p.ParseArgs(new[] { "hello", "--help" });
        Assert.Equal(new List<string> { "hello", "--help" }, args.GetList("message"));
    }

    // C-P2-3: unknown -flags in list position must error, not be swallowed.
    // (The swallowing loop is the `*`-option value consumer: `-n -x` eats
    // the unknown `-x` as `-n`'s value. Bare `-x` already throws.)
    [Fact]
    public void C_P2_3_UnknownFlagInList_Errors()
    {
        var p = new GameArgumentParser("audit");
        p.AddArgument("-n", "", "*");
        Assert.Throws<CommandError>(() => p.ParseArgs(new[] { "-n", "-x" }));
    }

    // C-P2-4: negative-number allowlist must cover trailing-dot forms.
    [Fact]
    public void C_P2_4_NegativeTrailingDot_ParsesAsValue()
    {
        var p = new GameArgumentParser("audit");
        p.AddArgument("val");
        var args = p.ParseArgs(new[] { "-5." });
        Assert.Equal("-5.", args.GetString("val"));
    }

    // C-P2-5: RemoveByTag must re-validate tags under the write lock.
    [Fact]
    public void C_P2_5_RemoveByTag_RevalidatesUnderWriteLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "CmdSet.cs");
        var region = AuditScan.Region(src, "public virtual void RemoveByTag(");
        Assert.Equal(2, AuditScan.Count(region, "kv.Value.Tag == tag"));
    }

    // C-P2-6: GetAll must not return per-alias duplicates (Distinct trap).
    [Fact]
    public void C_P2_6_GetAll_HasNoAliasDuplicates()
    {
        var cs = new CmdSet();
        var cmd = new ReqArgCommand();
        cs.Add(cmd, tag: "t");
        Assert.Equal(cs.GetAll().Count, cs.GetAll().Distinct().Count());
    }

    // C-P2-7: Remove after SetKey must not leak originally-registered entries.
    [Fact]
    public void C_P2_7_RemoveAfterSetKey_RemovesOriginalEntries()
    {
        var cs = new CmdSet();
        var cmd = new BaseChannelCommand();
        cmd.SetKey("audit_chan_a");
        cs.Add(cmd);
        cmd.SetKey("audit_chan_b");
        cs.Remove(cmd);
        Assert.Null(cs.Get("audit_chan_a"));
    }

    // C-P2-8: AutoAlias must match mixed-case keys (input is lowercased).
    [Fact]
    public void C_P2_8_AutoAlias_MatchesMixedCaseKeys()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "CommandDispatcher.cs");
        Assert.DoesNotContain("StartsWith(rawCmdKey, StringComparison.Ordinal)", src);
    }

    // C-P2-9: glued single-char path must consult internal/local verb sets.
    [Fact]
    public void C_P2_9_GluedPath_ConsultsLocalVerbs()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "CommandDispatcher.cs");
        int s = src.IndexOf("glued single-char non-alpha", StringComparison.Ordinal);
        int e = src.IndexOf("check location and inventory", s, StringComparison.Ordinal);
        Assert.True(s >= 0 && e > s);
        Assert.Contains("InternalCmdSet", src.Substring(s, e - s));
    }

    // C-P2-10 STRUCK (pin: unlogged Execute IS lag-gated at run time).
    [Fact]
    public void C_P2_10_UnloggedExecute_AppliesLagGate()
    {
        Reset();
        Command.GlobalLagCheck = _ => true;
        try
        {
            var conn = new TestConnection();
            var job = CommandDispatcher.ResolveUnloggedIn(conn, "quit");
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Empty(conn.SentCommandsBag);
        }
        finally { Command.GlobalLagCheck = null; Reset(); }
    }

    // C-P2-11: direct Run must honor the same gates as dispatch.
    [Fact]
    public void C_P2_11_DirectRun_HonorsDispatchGate()
    {
        Reset();
        CommandDispatcher.SetSettings(new AtherizSettings { AccountCreationEnabled = false });
        try
        {
            var conn = new TestConnection();
            new CreateAccountCommand().Run(conn, "");
            Assert.Contains(conn.SentCommandsBag, t => t.Json.Contains("not enabled"));
        }
        finally { Reset(); }
    }

    // C-P2-12 DOWNGRADED to consistency nit (fourth pass): "ME" already
    // resolves via ContentUtils.Search's lowercase-then-compare, so the
    // case-sensitive `raw == "me"` fast path has no behavioral delta — the
    // residual issue is the inconsistent comparison itself.
    [Fact]
    public void C_P2_12_MeComparison_IsCaseInsensitive()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "CommandHelpers.cs");
        Assert.DoesNotContain("raw == \"me\"", src);
    }

    // C-P2-13: LocalVerbSets must include the location's own ExternalCmdSet.
    [Fact]
    public void C_P2_13_LocalVerbSets_IncludesLocationSet()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditverbs", 0, 0, 0));
            nh.AddNode(node);
            var locSet = new CmdSet();
            node.ExternalCmdSet = locSet;
            var go = GameObject.Create("visitor");
            ObjectRegistry.AddObject(go);
            Assert.True(go.MoveTo(node));
            Assert.Contains(locSet, CommandHelpers.LocalVerbSets(go));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P2-14: per-channel vs channel-command replay must agree on empty history.
    [Fact]
    public void C_P2_14_ReplayPaths_AgreeOnEmptyHistory()
    {
        Reset();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var ch = Channel.Create("replaychan" + suffix);
            var go = GameObject.Create("listener", isPc: true);
            ObjectRegistry.AddObject(go);
            go.Subscribe(ch);
            var b = new BaseChannelCommand();
            b.Channel = ch;
            b.Run(go, b.Parser!.ParseArgs(new[] { "-r" }));
            var baseReply = string.Join("\n", go.PeekMessages());
            go.ClearMessages();
            var c = new ChannelCommand();
            c.Run(go, c.Parser!.ParseArgs(new[] { "-c", "replaychan" + suffix, "-r" }));
            var chanReply = string.Join("\n", go.PeekMessages());
            Assert.Equal(baseReply, chanReply);
        }
        finally { Reset(); }
    }

    // C-P2-15: channel send with an empty message must give feedback, not silence.
    [Fact]
    public void C_P2_15_EmptyChannelSend_GivesFeedback()
    {
        Reset();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var ch = Channel.Create("sendchan" + suffix);
            var go = GameObject.Create("sender", isPc: true);
            ObjectRegistry.AddObject(go);
            go.Subscribe(ch);
            var c = new ChannelCommand();
            c.Run(go, c.Parser!.ParseArgs(new[] { "-c", "sendchan" + suffix }));
            Assert.NotEmpty(go.PeekMessages());
        }
        finally { Reset(); }
    }

    // C-P2-16: put honors only destList[0] silently; get splits on first from/in.
    [Fact]
    public void C_P2_16_PutMultiDest_ReportsInsteadOfSilentPick()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditput", 0, 0, 0));
            nh.AddNode(node);
            var go = GameObject.Create("porter", isPc: true);
            ObjectRegistry.AddObject(go);
            Assert.True(go.MoveTo(node));
            var box1 = GameObject.Create("box", isContainer: true);
            ObjectRegistry.AddObject(box1);
            Assert.True(box1.MoveTo(node));
            var box2 = GameObject.Create("box", isContainer: true);
            ObjectRegistry.AddObject(box2);
            Assert.True(box2.MoveTo(node));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(go));
            var cmd = new PutCommand();
            cmd.Run(go, cmd.Parser!.ParseArgs(new[] { "coin", "in", "box" }));
            Assert.Contains(coin.Id, go.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    [Fact]
    public void C_P2_16b_GetFrom_ParsesLastFrom()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditget", 0, 0, 0));
            nh.AddNode(node);
            var go = GameObject.Create("getter", isPc: true);
            ObjectRegistry.AddObject(go);
            Assert.True(go.MoveTo(node));
            var box = GameObject.Create("box", isContainer: true);
            ObjectRegistry.AddObject(box);
            Assert.True(box.MoveTo(node));
            var item = GameObject.Create("a from b", isItem: true);
            ObjectRegistry.AddObject(item);
            Assert.True(item.MoveTo(box));
            var cmd = new GetCommand();
            cmd.Run(go, cmd.Parser!.ParseArgs(new[] { "a", "from", "b", "from", "box" }));
            Assert.Contains(item.Id, go.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P2-17: bulk veto paths must report like single-item paths.
    [Fact]
    public void C_P2_17_BulkVeto_Reports()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditveto", 0, 0, 0));
            nh.AddNode(node);
            var go = GameObject.Create("packer", isPc: true);
            ObjectRegistry.AddObject(go);
            Assert.True(go.MoveTo(node));
            var box = GameObject.Create("box", isContainer: true);
            ObjectRegistry.AddObject(box);
            Assert.True(box.MoveTo(node));
            var cursed = new VetoPut();
            cursed.Name = "cursed";
            ObjectRegistry.AddObject(cursed);
            Assert.True(cursed.MoveTo(go));
            var put = new PutCommand();
            put.Run(go, put.Parser!.ParseArgs(new[] { "all", "in", "box" }));
            Assert.Contains(go.PeekMessages(), m => m.Contains("cursed"));
            go.ClearMessages();
            var drop = new DropCommand();
            drop.Run(go, drop.Parser!.ParseArgs(new[] { "all" }));
            Assert.Contains(go.PeekMessages(), m => m.Contains("cursed"));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P2-18: PutCommand must use the standard puppet-denial message.
    [Fact]
    public void C_P2_18_PutDenial_UsesStandardMessage()
    {
        Reset();
        try
        {
            var conn = new TestConnection();
            new PutCommand().Run(conn, null);
            Assert.Contains(conn.SentCommandsBag, t => t.Json.Contains("You can't do that."));
        }
        finally { Reset(); }
    }

    // C-P2-19: member resolution + channel suffix must not guess/collide.
    [Fact]
    public void C_P2_19_GroupResolution_DoesNotGuess()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "GroupCommand.cs");
        Assert.DoesNotContain("\"all \" + targetName", src);
        Assert.DoesNotContain("Next(0, 100)", src);
    }

    // C-P2-20 STRUCK (pin: GetDisplayName(null) is null-safe).
    [Fact]
    public void C_P2_20_GetDisplayName_NullLookerIsSafe()
    {
        Reset();
        try
        {
            var obj = GameObject.Create("named");
            ObjectRegistry.AddObject(obj);
            Assert.Equal("named", obj.GetDisplayName(null));
        }
        finally { Reset(); }
    }

    // C-P2-21: exit routing failures must message the mover, not just stderr.
    [Fact]
    public void C_P2_21_ExitFailure_MessagesMover()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var c = GameObject.Create("walker", isPc: true);
            ObjectRegistry.AddObject(c);
            var cmd = new LoggedInExitCommand();
            cmd.CallerId = c.Id;
            cmd.Location = new Coord("auditexit", 0, 0, 0);
            cmd.Destination = new Coord("auditexit", 9, 9, 9);
            cmd.ExitName = "north";
            cmd.DoMove();
            Assert.NotEmpty(c.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P2-22: unfollow must clean up the drained FollowScript like nofollow.
    [Fact]
    public void C_P2_22_Unfollow_CleansUpScript()
    {
        Reset();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("auditfollow", 0, 0, 0));
            nh.AddNode(node);
            var leader = GameObject.Create("leader", isPc: true);
            ObjectRegistry.AddObject(leader);
            Assert.True(leader.MoveTo(node));
            var fan = GameObject.Create("fan", isPc: true);
            ObjectRegistry.AddObject(fan);
            Assert.True(fan.MoveTo(node));
            var follow = new FollowCommand();
            follow.Run(fan, follow.Parser!.ParseArgs(new[] { "leader" }));
            Assert.True(leader.HasScriptType("FollowScript"));
            new UnfollowCommand().Run(fan, null);
            Assert.False(leader.HasScriptType("FollowScript"));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // C-P2-23: permission check must precede occupancy disclosure.
    [Fact]
    public void C_P2_23_Puppet_ChecksPermissionFirst()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "PuppetCommand.cs");
        Assert.True(src.IndexOf("Access(go, \"puppet\")", StringComparison.Ordinal) < src.IndexOf("already being puppeted", StringComparison.Ordinal));
    }

    // C-P2-24: creation stubs must honor quotes/tabs like SplitArgs.
    [Fact]
    public void C_P2_24_QuotedName_ParsesAsOne()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var conn = new TestConnection();
            new CreateAccountCommand().Run(conn, "\"my name\" secretpw");
            Assert.Contains(conn.SentCommandsBag, t => t.Json.Contains("Account my name created."));
        }
        finally { Reset(); }
    }

    // C-P2-25: distinct non-connection callers must not share one bucket.
    // (The shared "?" bucket additionally bypasses throttling entirely via
    // the `host == "?"` early-true in TryReserveCreationCooldown.)
    [Fact]
    public void C_P2_25_NonConnectionCallers_HaveIndependentBuckets()
    {
        var a = GameObject.Create("a");
        var b = GameObject.Create("b");
        Assert.NotEqual(CreationCooldownHelper.RateKey(a), CreationCooldownHelper.RateKey(b));
    }

    // C-P2-26: AtServerStop must run after the shutdown is confirmed.
    [Fact]
    public void C_P2_26_ServerStop_RunsAfterConfirmation()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "ShutdownCommand.cs");
        Assert.True(src.IndexOf("AtServerStop()", StringComparison.Ordinal) > src.IndexOf("IsSuccessStatusCode", StringComparison.Ordinal));
    }

    // C-P2-27: reload must not block on async work and must surface first causes.
    [Fact]
    public void C_P2_27_Reload_DoesNotBlockOrSwallow()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "ReloadCommand.cs");
        Assert.DoesNotContain("GetAwaiter().GetResult()", src);
        var region = AuditScan.Region(src, "public override void Run(");
        Assert.True(region.IndexOf("GetServerChannel()", StringComparison.Ordinal) > region.IndexOf("try", StringComparison.Ordinal));
    }

    // C-P2-28: suggestions must respect Hide/Access; unlogged format matches.
    [Fact]
    public void C_P2_28_Suggestions_RespectHideAndAccess()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "NoneCommand.cs");
        Assert.Contains("!c.Hide", src);
        var unlogged = AuditScan.Read("src", "Atheriz.Core", "Commands", "UnloggedIn", "NoneCommand.cs");
        Assert.Contains("Huh?", unlogged);
    }

    // C-P2-29: single session lookup, sane width math, distinct locals.
    [Fact]
    public void C_P2_29_HelpCommand_HasNoDuplicatedWork()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "HelpCommand.cs");
        Assert.DoesNotContain("sr2", src);
        Assert.DoesNotContain("locals.AddRange(set.GetAll().Where", src);
    }

    // C-P2-30: stuck wanderers must not fail silently every tick.
    [Fact]
    public void C_P2_30_WanderFailure_IsSurfaced()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "WanderCommand.cs");
        Assert.Contains("if (!MoveTo(node", src);
    }

    // C-P2-31: logged-in quit must close raw connections like the twin.
    [Fact]
    public void C_P2_31_LoggedInQuit_ClosesConnection()
    {
        Reset();
        try
        {
            var conn = new TestConnection();
            new Atheriz.Core.Commands.LoggedIn.QuitCommand().Run(conn, null);
            Assert.True(conn.Closed);
        }
        finally { Reset(); }
    }

    // C-P2-32: Menu.Run must distinguish handler-exit from timeout/exhaustion.
    [Fact]
    public void C_P2_32_MenuRun_DistinguishesOutcomes()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Menu.cs");
        var region = AuditScan.Region(src, "public async Task<bool> Run(");
        Assert.Contains("return true", region);
    }

    // C-P2-33 STRUCK (pin: capture-after-call is safe; token-guarded cancel).
    [Fact]
    public async Task C_P2_33_MenuPromptCapture_IsFreshAndGuarded()
    {
        Reset();
        try
        {
            var sess = new Session(new TestConnection());
            var task = sess.Prompt("display");
            var pending = sess.InputFuture;
            Assert.NotNull(pending);
            Assert.True(sess.CancelPrompt(pending));
            Assert.Equal("", await task);
            Assert.False(sess.CancelPrompt(pending)); // stale token no-ops
        }
        finally { Reset(); }
    }

    // C-P2-34: welcome hints must agree with the dispatch gate.
    [Fact]
    public void C_P2_34_ScreenHints_AgreeWithDispatchGate()
    {
        Reset();
        CommandDispatcher.SetSettings(new AtherizSettings { AccountCreationEnabled = false });
        try
        {
            var screen = ConnectionScreen.Render(new AtherizSettings { AccountCreationEnabled = true });
            Assert.DoesNotContain("enter 'create'", screen);
        }
        finally { Reset(); }
    }

    // C-P2-35: examining an empty container must not echo the viewer's own Desc.
    [Fact]
    public void C_P2_35_EmptyContainerLook_DoesNotEchoSelf()
    {
        Reset();
        try
        {
            var viewer = GameObject.Create("viewer", isPc: true);
            ObjectRegistry.AddObject(viewer);
            viewer.Desc = "viewer-desc-audit";
            var conn = new TestConnection();
            var sess = new Session(conn);
            viewer.Session = sess;
            sess.Puppet = viewer;
            var box = GameObject.Create("emptybox", isContainer: true);
            ObjectRegistry.AddObject(box);
            box.Desc = "";
            viewer.Location = new LocationRef.ObjectLocation(box.Id);
            conn.ClearSent();
            new LookCommand().Run(viewer, null);
            Assert.DoesNotContain(conn.SentCommandsBag, t => t.Json.Contains("viewer-desc-audit"));
        }
        finally { Reset(); }
    }

    // C-P3-1: one location resolve per message.
    [Fact]
    public void C_P3_1_DoorDirection_ResolvesOnce()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "DoorDirectionCommand.cs");
        var region = AuditScan.Region(src, "public sealed override void Run(");
        Assert.Equal(1, AuditScan.Count(region, "ResolveLocationObject()"));
    }

    // C-P3-2: help aliases label is uniform.
    [Fact]
    public void C_P3_2_HelpAliasesLabel_IsUniform()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "Command.cs");
        Assert.DoesNotContain("aliases:", src);
    }

    // C-P3-3: usage reflects positionals; file overloads honor file.
    [Fact]
    public void C_P3_3_HelpUsage_ReflectsPositionals()
    {
        var p = new GameArgumentParser("audit");
        p.AddArgument("target", help: "Object to look at.");
        var usageLine = p.FormatHelp().Split('\n')[0];
        Assert.Contains("target", usageLine);
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "GameArgumentParser.cs");
        var region = AuditScan.Region(src, "public void PrintHelp(object? file)");
        Assert.Contains("file", region);
    }

    // C-P3-4: explicit optional positionals + detectable required flags.
    [Fact]
    public void C_P3_4_OptionalPositionalAndRequiredFlag_Detected()
    {
        var p = new GameArgumentParser("audit");
        p.AddArgument("target", required: false);
        var args = p.ParseArgs(Array.Empty<string>());
        Assert.Null(args["target"]);
        var p2 = new GameArgumentParser("audit2");
        p2.AddArgument("--force", action: "store_true", required: true);
        Assert.Throws<CommandError>(() => p2.ParseArgs(Array.Empty<string>()));
    }

    // C-P3-5 PARTIAL: logged-in None's SetupParser never runs (UseParser=false).
    [Fact]
    public void C_P3_5_LoggedInNone_HasNoDeadParser()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "NoneCommand.cs");
        Assert.DoesNotContain("SetupParser", src);
    }

    // C-P3-7: no always-hidden member emission; no blank locks fragment.
    [Fact]
    public void C_P3_7_ExamOddities_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "ExamFormatter.cs");
        Assert.DoesNotContain("internal_cmdset", src);
        Assert.DoesNotContain("new List<string> { \"\" }", src);
    }

    // C-P3-8: single live arg shape for build.
    [Fact]
    public void C_P3_8_BuildCommand_HasSingleArgShape()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "BuildCommand.cs");
        Assert.DoesNotContain("args is BuildArgs", src);
    }

    // C-P3-9: nullable settings parameter (dead ??= fallback removed).
    [Fact]
    public void C_P3_9_RenderSettings_IsNullable()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Contains("AtherizSettings? settings", src);
    }

    // C-P3-10: sync/async menu paths share one engine (no ctor render asymmetry).
    [Fact]
    public void C_P3_10_MenuEngine_HasSingleRenderPath()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Menu.cs");
        var region = AuditScan.Region(src, "public MenuEngine(object? caller,Func<MenuContext,(string,List<Choice>)> start)");
        Assert.DoesNotContain("_Render()", region);
    }

    // C-P3-11: one spelling for the prompt-with-timeout loop.
    [Fact]
    public void C_P3_11_PromptTimeout_HasSingleSpelling()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "MenuPrompt.cs");
        Assert.DoesNotContain("class MenuHelper", src);
    }
}
