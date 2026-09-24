using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from CountParseHelperTests.cs
[Collection("Ported")]
public sealed class CountParseHelperTests
{
    private static GameArgumentParser.ParsedArgs SpamArgs(params string[] argv)
        => new SpamCommand().Parser!.ParseArgs(argv);

    [Fact]
    public void TryGetCount_IntPassesThrough_StringParses_OtherDefaults()
    {
        var pa = SpamArgs("7");
        Assert.True(CommandHelpers.TryGetCount(pa, "count", 0, null, out int count));
        Assert.Equal(7, count);
        Assert.True(CommandHelpers.TryGetCount(null, "count", 10, 1, out int dflt));
        Assert.Equal(10, dflt);
    }

    [Fact]
    public void TryGetCount_BelowMin_ReturnsFalse_AtOrAbove_True()
    {
        var zero = SpamArgs("0");
        Assert.True(CommandHelpers.TryGetCount(zero, "count", 0, null, out int spamCount));
        Assert.Equal(0, spamCount);
        Assert.False(CommandHelpers.TryGetCount(zero, "count", 10, 1, out _));
        // A pre-parsed negative int (bypasses flag-like argv tokenizing).
        var neg = new GameArgumentParser.ParsedArgs { ["count"] = -3 };
        Assert.False(CommandHelpers.TryGetCount(neg, "count", 10, 1, out _));
    }

    [Fact]
    public void Spam_ZeroCount_RunsEmptyLoop_WithCountMessages()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("Admin", privilege: Atheriz.Core.Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        admin.ClearMessages();
        new SpamCommand().Run(admin, SpamArgs("0"));
        var msgs = admin.PeekMessages();
        Assert.Contains(msgs, m => m == "Creating 0 accounts and characters...");
        Assert.Contains(msgs, m => m.StartsWith("Created 0 accounts/chars in ", StringComparison.Ordinal));
    }

    [Fact]
    public void Wander_NonPositiveCount_Refuses()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = GameObject.Create("Builder", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        builder.ClearMessages();
        new WanderCommand().Run(builder, new WanderCommand().Parser!.ParseArgs(["0"]));
        Assert.Contains(builder.PeekMessages(), m => m == "Count must be a positive number.");
    }
}

// Merged from ChoiceDedupOrderTests.cs
[Collection("Ported")]
public sealed class ChoiceDedupOrderTests
{
    [Fact]
    public void TryAddChoice_KeepsFirstInsertionOrder_SkipsDuplicates()
    {
        List<string> ordered = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        Assert.True(CommandHelpers.TryAddChoice(ordered, seen, "beta"));
        Assert.True(CommandHelpers.TryAddChoice(ordered, seen, "alpha"));
        Assert.False(CommandHelpers.TryAddChoice(ordered, seen, "beta"));
        Assert.Equal(["beta", "alpha"], ordered);
    }

    [Fact]
    public void NoneCommand_TiedLocalKeys_SuggestsFirstInserted()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("Seeker", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var internalCs = new CmdSet();
        internalCs.Add(new TieAlphaCommand());
        internalCs.Add(new TieBetaCommand());
        caller.InternalCmdSet = internalCs;
        // "aaaz" sits at distance 1 from both locals (and farther from every
        // real command key); the first-inserted wins the stable tie-break.
        new NoneCommand().Run(caller, new NoneCommand().Parser!.ParseArgs(["aaaz"]));
        var msg = string.Join("\n", caller.PeekMessages());
        Assert.Contains("did you mean", msg);
        Assert.Contains("aaax", msg);
    }

    [Fact]
    public void Socials_RuntimeAddedSocial_StaysVisibleInAliases()
    {
        using var env = GlobalTestEnv.Enter();
        const string key = "frobnicate-test-social";
        try
        {
            SocialsCommand.SocialsDict[key] = ("$You() $conj(frobnicate).", "$You() $conj(frobnicate) at $you(target).");
            Assert.Contains(key, new SocialsCommand().Aliases);
        }
        finally
        {
            SocialsCommand.SocialsDict.Remove(key);
        }
        Assert.DoesNotContain(key, new SocialsCommand().Aliases);
    }

    private sealed class TieAlphaCommand : Command
    {
        public override string Key => "aaax";
        public override string Desc => "tie alpha";
        protected override void SetupParser(GameArgumentParser p) { }
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class TieBetaCommand : Command
    {
        public override string Key => "aaay";
        public override string Desc => "tie beta";
        protected override void SetupParser(GameArgumentParser p) { }
        public override void Run(IMessageTarget caller, object? args) { }
    }
}

// Merged from GiveMultiWordResolutionTests.cs
[Collection("Ported")]
public sealed class GiveMultiWordResolutionTests
{
    [Fact]
    public void Give_MultiWordItem_ToMultiWordTarget_ResolvesBoth()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("givemw", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var giver = GameObject.Create("giver", isPc: true);
        var receiver = GameObject.Create("Bob Green", isPc: true);
        ObjectRegistry.AddObject(giver);
        ObjectRegistry.AddObject(receiver);
        giver.IsConnected = true;
        receiver.IsConnected = true;
        var loc = new LocationRef.CoordLocation(node.Coord);
        giver.Location = loc;
        receiver.Location = loc;
        node.AddObject(giver);
        node.AddObject(receiver);
        var item = GameObject.Create("red ball", isItem: true);
        ObjectRegistry.AddObject(item);
        Assert.True(item.MoveTo(giver));
        giver.ClearMessages();
        receiver.ClearMessages();
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(["red", "ball", "Bob", "Green"]));
        Assert.Contains(item.Id, receiver.ContentsSnapshot);
        Assert.DoesNotContain(item.Id, giver.ContentsSnapshot);
        Assert.Contains(giver.PeekMessages(), m => m.Contains("You give red ball to Bob Green."));
    }
}

// Merged from LinkAliasCollisionTests.cs
// Link lookup scoping: HasLinkName/GetLinkByName match names and aliases
// case-insensitively through one shared lookup, while AddLinkIfAbsent guards
// on names only — an alias collision must not block creation.
[Collection("Ported")]
public class LinkAliasCollisionTests
{
    [Fact]
    public void AliasMatch_DoesNotBlockAddLinkIfAbsent()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));
        node.AddLink(new NodeLink("north", new Coord("limbo", 0, 1, 0), new List<string> { "n" }));

        Assert.True(node.HasLinkName("N"));

        bool factoryRan = false;
        bool added = node.AddLinkIfAbsent("n", () =>
        {
            factoryRan = true;
            return new NodeLink("n", new Coord("limbo", 1, 0, 0), new List<string>());
        });

        Assert.True(factoryRan);
        Assert.True(added);
        Assert.Equal(2, node.GetLinks().Count);
    }

    [Fact]
    public void NameMatch_BlocksAddLinkIfAbsent_CaseInsensitively()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));
        node.AddLink(new NodeLink("north", new Coord("limbo", 0, 1, 0), new List<string> { "n" }));

        bool factoryRan = false;
        bool added = node.AddLinkIfAbsent("NORTH", () =>
        {
            factoryRan = true;
            return new NodeLink("NORTH", new Coord("limbo", 0, -1, 0), new List<string>());
        });

        Assert.False(factoryRan);
        Assert.False(added);
        Assert.Single(node.GetLinks());
        Assert.NotNull(node.GetLinkByName("NoRtH"));
    }
}

// Merged from MenuChoiceBuildTests.cs
// One choice-dict build for rendering: case-insensitive map +
// byte-identical ToLowerInvariant().Trim() duplicate-key throw.
[Collection("Ported")]
public class MenuChoiceBuildTests
{
    private static Task<(string, List<Choice>)> DupNode(MenuContext ctx)
        => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("A", "first"), new Choice(" a ", "second")]));

    [Fact]
    public async Task Render_DuplicateKey_Throws()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = new MenuEngine(null, DupNode);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RenderAsync());
        Assert.Equal("duplicate menu key: ' a '", ex.Message);
    }

    [Fact]
    public async Task AsyncRender_DuplicateKey_ThrowsIdenticalMessage()
    {
        using var env = GlobalTestEnv.Enter();
        Task<(string, List<Choice>)> Start(MenuContext ctx) => DupNode(ctx);
        var engine = new MenuEngine(null, Start);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RenderAsync());
        Assert.Equal("duplicate menu key: ' a '", ex.Message);
    }

    [Fact]
    public void ChoiceBuild_LivesInOneHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "BuildChoices(List<Choice>"));
        Assert.Equal(1, SourceScan.Count(src, "BuildChoices(cl)"));
    }

    // The rendered menu text is a property, not a Java-style getter: pure,
    // cheap, and side-effect free, so it reads as state.
    [Fact]
    public async Task Display_IsPropertyNotMethod()
    {
        using var env = GlobalTestEnv.Enter();
        static Task<(string, List<Choice>)> Node(MenuContext ctx)
            => Task.FromResult<(string, List<Choice>)>(("hello", [new Choice("a", "first")]));
        var engine = new MenuEngine(null, Node);
        await engine.RenderAsync();
        Assert.Contains("hello", engine.Display);
        Assert.Contains("[a] first", engine.Display);
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.DoesNotContain("GetDisplay()", src);
        Assert.Contains("string Display", src);
    }
}

// Merged from MenuInputNormalizeStayTests.cs
// Shared normalization + lookup for input handling; a throwing callback logs
// "menu callback failed" and stays.
[Collection("Ported")]
public class MenuInputNormalizeStayTests
{
    private static Task<(string, List<Choice>)> StayNode(MenuContext ctx)
        => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("key", "desc", callback: _ => Task.FromException(new InvalidOperationException("boom")), stay: true)]));

    private static Task<(string, List<Choice>)> StayNodeAsync(MenuContext ctx)
        => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("key", "desc", callback: async _ => { await Task.Delay(1); throw new InvalidOperationException("boom"); }, stay: true)]));

    private static async Task<MenuEngine> Rendered(Func<MenuContext, Task<(string, List<Choice>)>> start)
    {
        var engine = new MenuEngine(null, start);
        await engine.RenderAsync();
        return engine;
    }

    [Fact]
    public async Task SyncThrowingCallback_LogsAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = await Rendered(StayNode);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            Assert.True(await engine.HandleInputAsync("  KEY  "));
            log = cap.Read();
        }
        Assert.Contains("menu callback failed", log);
        Assert.True(engine.HasNode);
    }

    [Fact]
    public async Task AsyncThrowingCallback_LogsAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = await Rendered(StayNodeAsync);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            Assert.True(await engine.HandleInputAsync("  KEY  "));
            log = cap.Read();
        }
        Assert.Contains("menu callback failed", log);
        Assert.True(engine.HasNode);
    }

    [Fact]
    public async Task UnknownKey_Stays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = await Rendered(StayNode);
        Assert.True(await engine.HandleInputAsync("nope"));
    }

    [Fact]
    public void InputPrefix_And_RunLoop_LiveInOneCoreEach()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "NormalizeKey(string"));
        Assert.Equal(1, SourceScan.Count(src, "TryGetChoice(string"));
        Assert.DoesNotContain("RunLoopAsync(", src);
        Assert.DoesNotContain("Task.Run(", src);
        Assert.DoesNotContain("class MenuRunner", src);
        Assert.DoesNotContain("public bool HandleInput(", src);
    }
}

// Merged from MenuLoggerClockShapeTests.cs
// One async menu system, one real log sink, one clock name: no sync/async
// duality, no null logger pair, no BCL type collision.
public class MenuLoggerClockShapeTests
{
    [Fact]
    public void Menu_IsAsyncOnly()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.DoesNotContain("GotoSync", src);
        Assert.DoesNotContain("CallbackSync", src);
        Assert.DoesNotContain("CurrentNodeSync", src);
        Assert.DoesNotContain("class MenuRunner", src);
        Assert.DoesNotContain("static Task RunMenu(", src);
        Assert.DoesNotContain("Task.Run(", src);
        Assert.Contains("public async Task<bool> HandleInputAsync(", src);
        Assert.Contains("public async Task<bool> RunAsync(", src);
    }

    [Fact]
    public void Menu_SingleChoiceShape()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "public sealed class Choice"));
        Assert.Contains("Func<MenuContext, Task<(string, List<Choice>)>>? Goto", src);
        Assert.Contains("Func<MenuContext, Task>? Callback", src);
    }

    [Fact]
    public void Logger_HasRealSink_NoNullPair()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.Contains("AtherizSinkProvider", src);
        Assert.DoesNotContain("NullLoggerProvider", src);
        Assert.DoesNotContain("NullLogger", src);
    }

    [Fact]
    public void Clock_IsGameClock_NotTimeProvider()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "GameClock.cs");
        Assert.Contains("public static class GameClock", src);
        Assert.False(File.Exists("/home/anon/atheriz-cs/src/Atheriz.Core/Utils/TimeProvider.cs"));
    }
}

// Merged from MenuPromptSingleTimerTests.cs
// One armed timer instead of two: WaitAsync owns the single timeout clock
// (no separate delay task to release); the timeout path still releases the
// prompt future. The sync alias stays: the parity suite and external game
// code call that spelling.
[Collection("Ported")]
public class MenuPromptSingleTimerTests
{
    [Fact]
    public async Task Timeout_ReturnsNull_ReleasesFuture()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new Session();
        var r = await MenuPrompt.PromptWithTimeoutAsync(s, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(r);
        Assert.True(s.InputFuture == null || s.InputFuture.Task.IsCompleted);
    }

    [Fact]
    public async Task SyncAlias_ForwardsToAsync()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new Session();
        var r = await MenuPrompt.PromptWithTimeout(s, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(r);
        Assert.True(s.InputFuture == null || s.InputFuture.Task.IsCompleted);
        Assert.NotNull(typeof(MenuPrompt).GetMethod("PromptWithTimeout"));
        Assert.NotNull(typeof(MenuPrompt).GetMethod("PromptWithTimeoutAsync"));
    }

    [Fact]
    public void SingleTimer_KeepsCancelPrompt_ReleasesDelay()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "MenuPrompt.cs");
        var region = SourceScan.Region(src, "public static async Task<string?> PromptWithTimeoutAsync(");
        Assert.DoesNotContain("new CancellationTokenSource(timeout)", region);
        Assert.DoesNotContain("Task.Delay", region);
        Assert.Contains("session.CancelPrompt(token)", region);
        Assert.Contains(".WaitAsync(timeout)", region);
    }
}

// Merged from LookDispatchTests.cs
// Look dispatch: AtLook reaches the Node appearance override through virtual
// dispatch (no type test), and container contents resolve via the snapshot
// path with no caller-side copy.
[Collection("Ported")]
public class LookDispatchTests
{
    [Fact]
    public void AtLook_UsesVirtualDispatch_ForNode()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 1, 2, 3));
            node.Desc = "A plain room.";
            var looker = GameObject.Create("looker");

            var seen = looker.AtLook(node);

            Assert.Equal(node.ReturnAppearance(looker), seen);
            Assert.Contains("A plain room.", seen);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtLook_NullTarget_SeesNothing()
    {
        var looker = GameObject.Create("looker");
        Assert.Equal("You see nothing here.", looker.AtLook(null));
    }

    [Fact]
    public void GetDisplayThings_GroupsSnapshotContents()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var item = GameObject.Create("sword");
            ObjectRegistry.AddObject(item);
            room.AddObject(item);
            var looker = GameObject.Create("looker");

            Assert.Contains("sword", room.GetDisplayThings(looker));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from ExamIdSetRendererTests.cs
[Collection("Ported")]
public sealed class ExamIdSetRendererTests
{
    [Fact]
    public void FormatValue_Followers_RendersNamedIdSet()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        var b = GameObject.Create("Bob");
        ObjectRegistry.AddObject(a);
        ObjectRegistry.AddObject(b);
        var rendered = ExamFormatter.FormatValue(new List<int> { a.Id, b.Id }, "followers") as string;
        Assert.Equal($"{{{ExamId(a, "Alice")}, {ExamId(b.Id, "Bob")}}}", rendered);
    }

    [Fact]
    public void FormatValue_Followers_CoercesNumericStrings_SkipsJunk()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        ObjectRegistry.AddObject(a);
        var rendered = ExamFormatter.FormatValue(new List<object?> { a.Id, a.Id.ToString(), "junk" }, "followers") as string;
        Assert.Equal($"{{{ExamId(a, "Alice")}, {ExamId(a.Id, "Alice")}}}", rendered);
    }

    [Fact]
    public void FormatValue_ScriptsAndContents_ShareStrictRenderer()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        ObjectRegistry.AddObject(a);
        var ids = new List<int> { a.Id };
        Assert.Equal(
            ExamFormatter.FormatValue(ids, "scripts") as string,
            ExamFormatter.FormatValue(ids, "_contents") as string);
        Assert.Equal($"{{{ExamId(a, "Alice")}}}", ExamFormatter.FormatValue(ids, "scripts") as string);
        // Empty renders set() in both...
        Assert.Equal("set()", ExamFormatter.FormatValue(new List<int>(), "scripts") as string);
        Assert.Equal("set()", ExamFormatter.FormatValue(new List<int>(), "_contents") as string);
        Assert.Equal("set()", ExamFormatter.FormatValue(null, "followers") as string);
        // ...while non-int members fall through to the generic renderer, not a partial set.
        var mixed = ExamFormatter.FormatValue(new List<object?> { "junk" }, "scripts") as string;
        Assert.Equal(ExamFormatter.FormatValue(new List<object?> { "junk" }, "_contents") as string, mixed);
        Assert.DoesNotContain("#", mixed);
    }

    [Fact]
    public void BaseMembers_TableShape_EvaluatedPerObject()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        var b = GameObject.Create("Bob");
        var membersA = ExamFormatter.BaseMembers(a).ToList();
        var membersB = ExamFormatter.BaseMembers(b).ToList();
        Assert.Equal(membersA.Count, membersB.Count);
        Assert.Equal(44, membersA.Count);
        Assert.Equal(a.Id, membersA.First(m => m.name == "Id").value);
        Assert.Equal("Alice", membersA.First(m => m.name == "Name").value);
        Assert.Equal("Bob", membersB.First(m => m.name == "Name").value);
        // Order matches the old per-call yield sequence (Id first, extra last).
        Assert.Equal("Id", membersA[0].name);
        Assert.Equal("extra", membersA[^1].name);
    }

    private static string ExamId(GameObject o, string name) => $"#{o.Id} ({name})";
    private static string ExamId(int id, string name) => $"#{id} ({name})";
}
