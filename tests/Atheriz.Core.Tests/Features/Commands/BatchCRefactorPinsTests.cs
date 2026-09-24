using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins for the section-4 Batch C refactors: registry family builders,
// the ScreenReaderCommand move to Common, data-driven HelpFormatter widths,
// $-interpolation in the exam/socials/ban renderers, and the throwing
// name validator. Each pin fails if its refactor regresses behavior.
[Collection("Ported")]
public sealed class BatchCRefactorPinsTests
{
    private sealed class LongKeyCommand : Command
    {
        public override string Key => "superlongcommandkey";
        public override string Desc => "Long key.";
        public override string Category => "General";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void RegistryLoggedIn_FamilyKeySetMatchesOriginalTotals()
    {
        CommandRegistry.Reset();
        try
        {
            var all = CommandRegistry.LoggedIn.GetAll();
            Assert.Equal(47, all.Count);
            var keys = all.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(
            [
                "ban", "build", "channel", "close", "create", "delete", "desc",
                "door", "drop", "emote", "examine", "follow", "get", "give",
                "group", "help", "inventory", "lock", "look", "map", "mapedit",
                "maze", "move", "nofollow", "none", "noun", "open", "puppet",
                "put", "quell", "quit", "reload", "save", "say", "screenreader",
                "set", "shutdown", "socials", "spam", "time", "unban", "unfollow",
                "unlock", "unpuppet", "unquell", "unset", "wander",
            ], keys);
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void RegistryUnloggedIn_FamilyKeySetMatchesOriginalTotals()
    {
        CommandRegistry.Reset();
        try
        {
            var all = CommandRegistry.UnloggedIn.GetAll();
            Assert.Equal(8, all.Count);
            var keys = all.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(
                ["connect", "create", "guest", "help", "new", "none", "quit", "screenreader"],
                keys);
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void ScreenReader_BothNamespacesShareKeyAliasesAndToggle()
    {
        var common = new Atheriz.Core.Commands.Common.ScreenReaderCommand();
        var legacy = new ScreenReaderCommand();
        Assert.Equal("screenreader", common.Key);
        Assert.Equal(common.Key, legacy.Key);
        Assert.Equal(common.Aliases, legacy.Aliases);
        Assert.Contains("sr", legacy.Aliases);
        Assert.Equal(common.Category, legacy.Category);
        CommandRegistry.Reset();
        try
        {
            Assert.IsType<Atheriz.Core.Commands.Common.ScreenReaderCommand>(
                CommandRegistry.LoggedIn.Get("screenreader"));
            Assert.IsType<ScreenReaderCommand>(
                CommandRegistry.UnloggedIn.Get("screenreader"));
        }
        finally { CommandRegistry.Reset(); }
        using var env = GlobalTestEnv.Enter();
        var conn = new FakeConnection();
        var sess = new Session { Connection = conn };
        var go = GameObject.Create("srtoggle", isPc: true);
        go.Session = sess;
        sess.Puppet = go;
        go.ClearMessages();
        legacy.Run(go, null);
        Assert.True(sess.ScreenReader);
        Assert.Contains(go.PeekMessages(), m => m.Contains("Screenreader mode on."));
    }

    [Fact]
    public void HelpFormatter_PinnedOutputsStayByteIdentical()
    {
        Command[] screenreaderCmds = [new OpenCommand(), new CloseCommand()];
        Assert.Equal(
            "General      close        Close doors.\n" +
            "General      open         Open doors.\n",
            HelpFormatter.Format(screenreaderCmds, screenreader: true, termWidth: 80));
        Command[] tableCmds = [new OpenCommand()];
        Assert.Equal(
            "Category     Command      Description\n" +
            new string('-', 60) + "\n" +
            "General      open         Open doors.\n",
            HelpFormatter.Format(tableCmds, screenreader: false, termWidth: 80));
        Assert.Equal(
            "Category     Command      Description\n" +
            new string('-', 28) + "\n" +
            "General      open         O...\n",
            HelpFormatter.Format(tableCmds, screenreader: false, termWidth: 30));
    }

    [Fact]
    public void HelpFormatter_LongKeyKeepsDescriptionColumnAligned()
    {
        Command[] cmds = [new LongKeyCommand(), new OpenCommand()];
        string table = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines.First(l => l.Contains("Description"));
        var longRow = lines.First(l => l.Contains("superlongcommandkey"));
        // The full key survives (never cut to fit) and both descriptions
        // start in the same column: the header widened with the data.
        Assert.Contains("superlongcommandkey Long key.", longRow);
        Assert.Equal(header.IndexOf("Description", StringComparison.Ordinal),
            longRow.IndexOf("Long key.", StringComparison.Ordinal));
        string list = HelpFormatter.Format(cmds, screenreader: true, termWidth: 80);
        var listRows = list.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var openListRow = listRows.First(l => l.Contains("Open doors."));
        var longListRow = listRows.First(l => l.Contains("superlongcommandkey"));
        Assert.Equal(
            openListRow.IndexOf("Open doors.", StringComparison.Ordinal),
            longListRow.IndexOf("Long key.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateNameOrThrow_ValidName_ReturnsTrimmedName()
    {
        Assert.Equal("Alice", Validation.ValidateNameOrThrow("  Alice  ", 20));
        Assert.Equal("O'Brien", Validation.ValidateNameOrThrow("O'Brien", 30));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData("", 20)]
    [InlineData("ab", 20)]
    [InlineData("this name is way too long for sure", 10)]
    [InlineData("Bad!!Name", 20)]
    [InlineData("123", 20)]
    [InlineData("a  b", 20)]
    [InlineData("here", 20)]
    public void ValidateNameOrThrow_InvalidName_ThrowsExactValidateNameMessage(string? name, int maxLen)
    {
        string? expected = Validation.ValidateName(name, maxLen);
        Assert.NotNull(expected);
        var ex = Assert.Throws<ArgumentException>(() => Validation.ValidateNameOrThrow(name, maxLen));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void ExamFormatter_BraceBracketInterpolation_MatchesLegacyShapes()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        ObjectRegistry.AddObject(alice);
        try
        {
            Assert.Equal($"{{#{alice.Id} (Alice)}}",
                ExamFormatter.FormatValue(new List<int> { alice.Id }, "followers") as string);
            var cs = new CmdSet();
            cs.Adds([new OpenCommand(), new CloseCommand()]);
            Assert.Equal("[open, close]", ExamFormatter.FormatValue(cs, "external_cmdset") as string);
            Assert.Equal("Session(w=78, h=45, sr=True)",
                ExamFormatter.FormatValue(new Session { ScreenReader = true }, "session") as string);
            Assert.Equal("{a: b}",
                ExamFormatter.FormatValue(new Dictionary<string, object?> { ["a"] = "b" }, null) as string);
            Assert.Equal("[x]", ExamFormatter.FormatValue(new List<string> { "x" }, "scripts") as string);
            Assert.Equal("None", ExamFormatter.FormatValue(null, "session") as string);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
