// Port of atheriz/tests/test_base_cmd.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Objects;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedBaseCmdTests
{
    // Helpers mirroring Python ConcreteCommand etc
    private sealed class ConcreteCommand : Command
    {
        public override string Key => "test";
        public override IReadOnlyList<string> Aliases => ["t", "tst"];
        public override string Desc => "A test command";
        public override string ExtraDesc => "extra info";
        public override string Category => "Testing";
        protected override void SetupParser(GameArgumentParser p)
        {
            p.AddArgument("target").Help("Target object");
            p.AddArgument("-f", "--flag").Action(GameArgumentParser.ArgAction.StoreTrue);
        }
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class OptionalArgsCommand : Command
    {
        public override string Key => "opt";
        public override IReadOnlyList<string> Aliases => ["o"];
        public override string Desc => "Optional args command";
        protected override void SetupParser(GameArgumentParser p)
        {
            p.AddArgument("-f", "--flag").Action(GameArgumentParser.ArgAction.StoreTrue);
            p.AddArgument("--name").Default("anon");
        }
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class NoParserCommand : Command
    {
        public override string Key => "raw";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class MockCaller : IMessageTarget
    {
        public List<string> Msgs = new();
        public void Msg(string text) => Msgs.Add(text);
        public void Msg(string text, GameObject? fromObj, IDictionary<string,object?>? mapping, bool raiseErrors=false, string? msgType=null) => Msgs.Add(text);
        public bool Access(GameObject? o, string l) => true;
    }

    // ----- TestCommandError -----
    [Fact]
    public void CommandError_IsException()
    {
        Assert.True(typeof(CommandError).IsSubclassOf(typeof(Exception)));
    }

    [Fact]
    public void CommandError_CanBeRaised()
    {
        var ex = Assert.Throws<CommandError>(new Action(() => throw new CommandError("bad arg")));
        Assert.Contains("bad", ex.Message);
    }

    [Fact]
    public void CommandError_MessagePreserved()
    {
        var e = new CommandError("nope");
        Assert.Equal("nope", e.Message);
    }

    // ----- TestGameArgumentParser -----
    [Fact]
    public void GameArgumentParser_CreatesWithProgAndDesc()
    {
        var p = new GameArgumentParser(prog: "foo", description: "Foo command");
        Assert.Equal("foo", p.Prog);
        Assert.Equal("Foo command", p.Description);
    }

    [Fact]
    public void GameArgumentParser_ErrorRaises()
    {
        var p = new GameArgumentParser(prog: "x");
        var ex = Assert.Throws<CommandError>(() => p.Error("bad"));
        Assert.Contains("bad", ex.Message);
    }

    [Fact]
    public void GameArgumentParser_PrintHelpRaises()
    {
        var p = new GameArgumentParser(prog: "x", description: "d");
        var ex = Assert.Throws<CommandError>(() => p.PrintHelp());
        var msg = ex.Message.ToLowerInvariant();
        Assert.True(msg.Contains("usage:") || msg.Contains("options:"));
    }

    [Fact]
    public void GameArgumentParser_PrintUsageRaises()
    {
        var p = new GameArgumentParser(prog: "x");
        var ex = Assert.Throws<CommandError>(() => p.PrintUsage());
        Assert.Contains("usage:", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void GameArgumentParser_ExitRaisesWithMessage()
    {
        var p = new GameArgumentParser(prog: "x");
        var ex = Assert.Throws<CommandError>(() => p.Exit(0, "oops"));
        Assert.Contains("oops", ex.Message);
    }

    [Fact]
    public void GameArgumentParser_ExitNoMessageDoesNotRaise()
    {
        var p = new GameArgumentParser(prog: "x");
        p.Exit(0); // should not throw
    }

    [Fact]
    public void GameArgumentParser_NormalParseStillWorks()
    {
        var p = new GameArgumentParser(prog: "x");
        p.AddArgument("name");
        var ns = p.ParseArgs(new List<string>{"alice"});
        Assert.Equal("alice", ns.GetString("name"));
    }

    // ----- TestCommandClassAttrs -----
    [Fact]
    public void Command_Defaults()
    {
        var c = new ConcreteCommand(); // use concrete to test base defaults via new Command? Use base via anonymous
        var baseCmd = new NoParserCommand(); // actually base defaults: we create a minimal base
        // Create raw base via concrete that doesn't override defaults
        var minimal = new MinimalBase();
        Assert.Equal("base", minimal.Key);
        Assert.Empty(minimal.Aliases);
        Assert.Equal("Base command", minimal.Desc);
        Assert.Equal("", minimal.ExtraDesc);
        Assert.Equal("General", minimal.Category);
        Assert.Equal("", minimal.Tag);
        Assert.False(minimal.Hide);
        Assert.True(minimal.UseParser);
    }

    private sealed class MinimalBase : Command
    {
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void Command_InitSetsParserNone()
    {
        var c = new MinimalBase();
        // Access private _parser via reflection to check null before lazy
        var f = typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Null(f!.GetValue(c));
    }

    [Fact]
    public void Command_InitNoSideEffects()
    {
        var c = new MinimalBase();
        var f = typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Null(f!.GetValue(c));
        var p = c.Parser;
        Assert.IsType<GameArgumentParser>(p);
    }

    [Fact]
    public void Command_AccessReturnsTrue()
    {
        var c = new MinimalBase();
        var caller = new MockCaller();
        // Need IMessageTarget wrapper; use mock that implements Access via GameObject?
        // MinimalBase.Access expects IMessageTarget, we pass mock via adapter
        // Create GameObject to satisfy
        using var env = GlobalTestEnv.Enter();
        var go = GameObject.Create("Test");
        Assert.True(c.Access(go));
    }

    [Fact]
    public void Command_RunIsNoop()
    {
        var c = new MinimalBase();
        using var env = GlobalTestEnv.Enter();
        var go = GameObject.Create("Test");
        var ex = Record.Exception(() => c.Run(go, "anything"));
        Assert.Null(ex);
    }

    // ----- TestCommandParser -----
    [Fact]
    public void Command_ParserLazy()
    {
        var c = new ConcreteCommand();
        var f = typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Null(f!.GetValue(c));
        var p = c.Parser;
        Assert.IsType<GameArgumentParser>(p);
        Assert.Equal("test", p!.Prog);
        Assert.Same(p, c.Parser);
    }

    [Fact]
    public void Command_ParserSetter()
    {
        var c = new ConcreteCommand();
        var custom = new GameArgumentParser(prog: "custom");
        c.Parser = custom;
        Assert.Same(custom, c.Parser);
    }

    [Fact]
    public void Command_SetupParserCalled()
    {
        var c = new ConcreteCommand();
        var p = c.Parser!;
        // Check that target and flag were added via internal defs
        var defsField = typeof(GameArgumentParser).GetField("_defs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defs = defsField!.GetValue(p) as System.Collections.IList;
        // Use reflection to inspect dest names
        bool hasTarget = false, hasFlag = false;
        foreach (var d in defs!)
        {
            var dest = d.GetType().GetField("Dest")!.GetValue(d) as string;
            if (dest == "target") hasTarget = true;
            if (dest == "flag") hasFlag = true;
        }
        Assert.True(hasTarget);
        Assert.True(hasFlag);
    }

    [Fact]
    public void Command_UseParserFalseParserIsNone()
    {
        var c = new NoParserCommand();
        var f = typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Null(f!.GetValue(c));
        Assert.Null(c.Parser);
    }

    [Fact]
    public void Command_UseParserFalseParserSetterWorks()
    {
        var c = new NoParserCommand();
        var custom = new GameArgumentParser(prog: "anything");
        // Parser setter expects GameArgumentParser? but we pass via object
        c.Parser = custom;
        var f = typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Same(custom, f!.GetValue(c));
    }

    // ----- TestPrintHelp -----
    [Fact]
    public void PrintHelp_IncludesProg()
    {
        var c = new ConcreteCommand();
        var h = c.PrintHelp();
        Assert.Contains("test", h);
    }

    [Fact]
    public void PrintHelp_IncludesDescription()
    {
        var c = new ConcreteCommand();
        var h = c.PrintHelp();
        Assert.Contains("A test command", h);
    }

    [Fact]
    public void PrintHelp_IncludesAliasesLine()
    {
        var c = new ConcreteCommand();
        var h = c.PrintHelp();
        Assert.Contains("aliases:", h);
        Assert.Contains("test", h);
        Assert.Contains("t", h);
        Assert.Contains("tst", h);
    }

    [Fact]
    public void PrintHelp_IncludesExtraDesc()
    {
        var c = new ConcreteCommand();
        var h = c.PrintHelp();
        Assert.Contains("extra info", h);
    }

    [Fact]
    public void PrintHelp_NoAliasesStillWorks()
    {
        var c = new MinimalBaseNoAlias();
        var h = c.PrintHelp();
        Assert.Contains("aliases: x", h);
    }
    private sealed class MinimalBaseNoAlias : Command
    {
        public override string Key => "x";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void PrintHelp_EmptyExtraDesc()
    {
        var c = new MinimalBase();
        var h = c.PrintHelp();
        Assert.DoesNotContain("extra info", h.ToLowerInvariant());
    }

    // ----- TestExecute (first half) -----
    [Fact]
    public void Execute_UseParserFalseReturnsRawArgs()
    {
        var c = new NoParserCommand();
        var caller = new MockCaller();
        var (runFn, cArg, args) = c.Execute(caller, "any string at all");
        Assert.NotNull(runFn);
        Assert.Equal("any string at all", args as string);
        Assert.Empty(caller.Msgs);
    }

    [Fact]
    public void Execute_WithEmptyArgsOptional()
    {
        var c = new OptionalArgsCommand();
        var caller = new MockCaller();
        var (runFn, cArg, parsed) = c.Execute(caller, "", cmdstring: "opt");
        Assert.NotNull(runFn);
        var pa = parsed as GameArgumentParser.ParsedArgs;
        Assert.NotNull(pa);
        Assert.False(pa!.GetBool("flag"));
        Assert.Equal("anon", pa.GetString("name"));
        Assert.Empty(caller.Msgs);
    }

    [Fact]
    public void Execute_WithRequiredArgOmitted()
    {
        var c = new ConcreteCommand();
        var caller = new MockCaller();
        var (runFn, cArg, parsed) = c.Execute(caller, "", cmdstring: "test");
        Assert.Null(runFn);
        Assert.Null(cArg);
        Assert.Null(parsed);
        Assert.Single(caller.Msgs);
        Assert.Contains("aliases:", caller.Msgs[0]);
    }

    [Fact]
    public void Execute_WithPositionalArg()
    {
        var c = new ConcreteCommand();
        var caller = new MockCaller();
        var (runFn, cArg, parsed) = c.Execute(caller, "alice", cmdstring: "test");
        var pa = parsed as GameArgumentParser.ParsedArgs;
        Assert.NotNull(pa);
        Assert.Equal("alice", pa!.GetString("target"));
        Assert.False(pa.GetBool("flag"));
        Assert.Equal("test", pa.CmdString);
    }

    [Fact]
    public void Execute_WithFlag()
    {
        var c = new ConcreteCommand();
        var caller = new MockCaller();
        var (runFn, _, parsed) = c.Execute(caller, "--flag alice", cmdstring: "test");
        var pa = parsed as GameArgumentParser.ParsedArgs;
        Assert.True(pa!.GetBool("flag"));
        Assert.Equal("alice", pa.GetString("target"));
    }

    [Fact]
    public void Execute_WithShortFlag()
    {
        var c = new ConcreteCommand();
        var caller = new MockCaller();
        var (runFn, _, parsed) = c.Execute(caller, "-f bob", cmdstring: "test");
        var pa = parsed as GameArgumentParser.ParsedArgs;
        Assert.True(pa!.GetBool("flag"));
        Assert.Equal("bob", pa.GetString("target"));
    }

    [Fact]
    public void Execute_InvalidArgsCallsMsgWithHelp()
    {
        var c = new ConcreteCommand();
        var caller = new MockCaller();
        var (runFn, cArg, parsed) = c.Execute(caller, "", cmdstring: "test");
        Assert.Null(runFn);
        Assert.Contains("aliases:", caller.Msgs[0]);
    }

    [Fact]
    public void Execute_CmdstringDefaultEmpty()
    {
        var c = new ConcreteCommand();
        var caller = new MockCaller();
        var (runFn, _, parsed) = c.Execute(caller, "alice");
        var pa = parsed as GameArgumentParser.ParsedArgs;
        Assert.Equal("", pa!.CmdString);
    }
}
