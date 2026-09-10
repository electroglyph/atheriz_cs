using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Regression;

// Pins for help/exam/menu/validation behavior.
[Collection("Ported")]
public class HelpExamMenuTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    // Both help listings share one shape: no per-call dedup (GetAll already
    // dedupes) and the blank-line prefix on the full listing.
    [Fact]
    public void HelpListings_ShareOneShape()
    {
        var logged = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "HelpCommand.cs");
        var unlogged = SourceScan.Read("src", "Atheriz.Core", "Commands", "UnloggedIn", "HelpCommand.cs");
        Assert.DoesNotContain(".GetAll().Distinct()", logged);
        Assert.DoesNotContain(".GetAll().Distinct()", unlogged);
        Assert.Contains("sb.ToString()", logged);
    }

    [Fact]
    public void LoggedInHelp_FullListing_StartsWithBlankLine()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("helped", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var job = CommandDispatcher.DispatchLoggedIn(admin, "help", immediate: true);
            RunJob(job);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.StartsWith("\n", msgs);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Exam branches on the node type directly; the key list keeps insertion
    // order under an honest name.
    [Fact]
    public void Exam_NodeBranch_TestsTypeDirectly()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "ExamCommand.cs");
        var region = SourceScan.Region(src, "public override void Run(");
        Assert.DoesNotContain("target.IsNode && target is Node", region);
        Assert.Contains("target is Node nodeTarget", region);
        Assert.Contains("keysInOrder", region);
        Assert.DoesNotContain("var sorted", region);
    }

    [Fact]
    public void Exam_Node_ShowsAreaLine_Object_ShowsPlainLine()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("ExamArea", 0, 0, 0));
            ObjectRegistry.AddObject(node);
            var admin = GameObject.Create("examiner", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(admin);
            Assert.True(admin.MoveTo(node, announce: false));
            var jn = CommandDispatcher.DispatchLoggedIn(admin, "exam here", immediate: true);
            RunJob(jn);
            Assert.Contains("area 'ExamArea'", string.Join("\n", admin.PeekMessages()));
            admin.ClearMessages();
            var jo = CommandDispatcher.DispatchLoggedIn(admin, "exam me", immediate: true);
            RunJob(jo);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("Examining", msgs);
            Assert.DoesNotContain("area 'ExamArea'", msgs);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Null names fail like empty names: a message, never a throw — so every
    // caller keeps its uniform Msg(err) handling.
    [Fact]
    public void ValidateName_Null_ReturnsEmptyMessage()
    {
        Assert.Equal("Name cannot be empty.", Validation.ValidateName(null, 20));
        Assert.Equal("Name cannot be empty.", Validation.ValidateAccountName(null));
        Assert.Equal("Name cannot be empty.", Validation.ValidateCharacterName(null));
        var s = new AtherizSettings();
        Assert.Equal("Name cannot be empty.", Validation.ValidateAccountName(null, s));
    }

    // The connection screen never advertises a command the dispatch gate
    // demotes, under any settings spelling: passed, snapshot, or Global.
    // The gate check lives in the shared HintText helper both hint lines use.
    [Fact]
    public void ConnectionScreen_NeverAdvertisesDemotedGuest()
    {
        var origSnapshot = new AtherizSettings();
        CommandDispatcher.SetSettings(new AtherizSettings { GuestEnabled = false });
        try
        {
            var custom = new AtherizSettings { GuestEnabled = true };
            var screen = ConnectionScreen.Render(custom, session: null);
            Assert.DoesNotContain("enter 'guest'", screen.ToLowerInvariant());
            var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
            Assert.Contains("IsUnloggedInEnabled", SourceScan.Region(src, "private static string HintText"));
        }
        finally { CommandDispatcher.SetSettings(origSnapshot); }
    }

    // Self-ban stays refused by the equal-or-higher rule, on purpose: the
    // refusal is the guardrail, not a missing exemption.
    [Fact]
    public void Ban_SelfBan_RefusedByPrivilegeRule()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "BanCommand.cs");
        var region = SourceScan.Region(src, "public override void Run(");
        Assert.Contains("No self-exempt idiom on purpose", region);
        Assert.Contains("target.PrivilegeLevel >= go.PrivilegeLevel", region);
    }

    // Unknown menu keys are logged, not silently swallowed; the handler
    // result reads as keep-going under its own name.
    [Fact]
    public void Menu_UnknownKey_Logged_ResultNamedKeepGoing()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        var region = SourceScan.Region(src, "public async Task<bool> Run(Session session");
        Assert.Contains("menu unknown key", region);
        Assert.Contains("keepGoing", region);
        Assert.DoesNotContain("var keep=", region);
    }
}
