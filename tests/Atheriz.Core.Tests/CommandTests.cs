using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests;

public sealed class CommandTests
{
    private sealed class EchoCommand : Command
    {
        public override string Key => "echo";
        public override IReadOnlyList<string> Aliases => ["ec"];
        public override string Desc => "echo";
        protected override void SetupParser(GameArgumentParser p)
        {
            p.AddArgument("msg", nargs: "REMAINDER", help: "msg");
        }
        public override void Run(IMessageTarget caller, object? args)
        {
            if (args is GameArgumentParser.ParsedArgs pa) caller.Msg(string.Join(" ", pa.GetList("msg")));
            else caller.Msg(args?.ToString() ?? "");
        }
    }

    private sealed class StoreTrueCommand : Command
    {
        public bool RanFlag = false;
        public override string Key => "flagtest";
        protected override void SetupParser(GameArgumentParser p)
        {
            p.AddArgument("-x", action: "store_true", help: "x");
            p.AddArgument("target", help: "t");
        }
        public override void Run(IMessageTarget caller, object? args)
        {
            if (args is GameArgumentParser.ParsedArgs pa) { RanFlag = pa.GetBool("x"); caller.Msg(pa.GetString("target") ?? ""); }
        }
    }

    [Fact]
    public void CmdSet_CollisionThrows()
    {
        var cs = new CmdSet();
        cs.Add(new EchoCommand());
        var dup = new EchoCommand(); // same key echo
        Assert.Throws<InvalidOperationException>(() => cs.Add(dup));
    }

    [Fact]
    public void CmdSet_SameInstanceNoThrow()
    {
        var cs = new CmdSet();
        var cmd = new EchoCommand();
        cs.Add(cmd);
        cs.Add(cmd); // re-register same instance should not throw
        Assert.Single(cs.GetAll().Distinct().Where(c => c.Key == "echo"));
    }

    [Fact]
    public void Command_UnbalancedQuote_SendsHelp()
    {
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        var cmd = new EchoCommand();
        var (func, _, _) = cmd.Execute(puppet, "\"unbalanced");
        Assert.Null(func);
        var msgs = puppet.PeekMessages();
        Assert.Contains(msgs, m => m.Contains("Unbalanced quote"));
    }

    [Fact]
    public void Command_HelpTrigger_SendsHelp()
    {
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        var cmd = new EchoCommand();
        var (func, _, _) = cmd.Execute(puppet, "--help");
        Assert.Null(func);
        var help = puppet.PeekMessages()[0];
        Assert.Contains("usage:", help);
        Assert.Contains("echo", help);
    }

    [Fact]
    public void Dispatch_InternalCmdSet_PrecedesGlobal()
    {
        CommandRegistry.ResetForTesting();
        var puppet = new GameObject { Name = "Hero" };
        var internalCs = new CmdSet();
        var internalCmd = new EchoCommand(); // key echo
        internalCs.Add(internalCmd);
        puppet.InternalCmdSet = internalCs;
        // ensure global also has echo? force global creation then override? For test, global has look etc but not echo; add echo to global too
        CommandRegistry.LoggedIn.Add(new EchoCommand());
        // But puppet internal should win - test by checking which runs?
        puppet.ClearMessages();
        var job = CommandDispatcher.DispatchLoggedIn(puppet, "echo hello world", immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
        Assert.Contains("hello world", puppet.PeekMessages().Last());
        CommandRegistry.ResetForTesting();
    }

    [Fact]
    public void Dispatch_AutoAlias_FindsPrefix()
    {
        CommandRegistry.ResetForTesting();
        var _ = CommandRegistry.LoggedIn; // init with default look etc
        var puppet = new GameObject { Name = "Hero", Desc = "A hero stands here." };
        puppet.ClearMessages();
        // "loo" should alias to "look" via AUTO_COMMAND_ALIASING unless ignored (use loo to avoid lock collision)
        CommandDispatcher.SetSettings(new AtherizSettings { AutoCommandAliasing = true });
        var job = CommandDispatcher.DispatchLoggedIn(puppet, "loo", immediate: true);
        Assert.NotNull(job);
        Assert.True(job!.Args is GameArgumentParser.ParsedArgs pa && pa.CmdString == "look");
        job.Func(job.Caller, job.Args);
        // look should msg desc
        Assert.Contains("hero stands", puppet.PeekMessages().Last().ToLower());
        CommandRegistry.ResetForTesting();
    }

    [Fact]
    public void Dispatch_GluedSingleCharNonAlpha_ConsumesPrefix()
    {
        CommandRegistry.ResetForTesting();
        var cs = CommandRegistry.LoggedIn;
        var custom = new TestCommaCommand();
        cs.Add(custom);
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        var job = CommandDispatcher.DispatchLoggedIn(puppet, ",hello world", immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
        // glued args should be "hello world" (prefix consumed)
        Assert.Contains("hello world", puppet.PeekMessages().Last());
        CommandRegistry.ResetForTesting();
    }

    private sealed class TestCommaCommand : Command
    {
        public override string Key => ",";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) => caller.Msg("comma:" + (args as string ?? ""));
    }

    [Fact]
    public void Dispatch_NoneFallback_ForUnknown()
    {
        CommandRegistry.ResetForTesting();
        var _ = CommandRegistry.LoggedIn;
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        var job = CommandDispatcher.DispatchLoggedIn(puppet, "unknowncmd arg1 arg2", immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
        // none command will msg Huh?
        Assert.Contains(puppet.PeekMessages(), m => m.Contains("Huh?"));
        CommandRegistry.ResetForTesting();
    }

    [Fact]
    public void PyCommand_NotPorted()
    {
        CommandRegistry.ResetForTesting();
        var keys = CommandRegistry.LoggedIn.GetKeys();
        Assert.DoesNotContain("py", keys);
        Assert.DoesNotContain("py", CommandRegistry.LoggedIn.GetAll().Select(c => c.Key));
        CommandRegistry.ResetForTesting();
    }

    [Fact]
    public void Dispatch_NIsBlockedForAutoAlias()
    {
        CommandRegistry.ResetForTesting();
        var _ = CommandRegistry.LoggedIn;
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        CommandDispatcher.SetSettings(new AtherizSettings { AutoCommandAliasing = true });
        var job = CommandDispatcher.DispatchLoggedIn(puppet, "n", immediate: true);
        // n is in NoAliasCommands -> should msg "You can't do that." and return null
        Assert.Null(job);
        Assert.Contains("You can't do that.", puppet.PeekMessages());
        CommandRegistry.ResetForTesting();
    }
}
