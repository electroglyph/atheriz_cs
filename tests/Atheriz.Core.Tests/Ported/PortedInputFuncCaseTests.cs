// Port of atheriz/tests/test_inputfunc_case.py:65 — 1 def faithful
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedInputFuncCaseTests
{
    // Mirrors test_inputfunc_case.py:16 _StubCmd
    private sealed class _StubCmd : Command
    {
        public override string Key => "none";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
        public override bool Access(IMessageTarget caller) => true;
    }
    // Mirrors test_inputfunc_case.py:24 _StubCmdSet — only knows fallback 'none' (nothing starts with 'n')
    private sealed class _StubCmdSet : CmdSet
    {
        private readonly Command _none = new _StubCmd();
        public override Command? Get(string key) => key == "none" ? _none : null;
        public override IReadOnlyList<string> GetKeys() => new[] { "none" };
    }
    // Mirrors test_inputfunc_case.py:38 _ShortAliasCmd
    private sealed class _ShortAliasCmd : Command
    {
        public override string Key => "b";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
        public override bool Access(IMessageTarget caller) => true;
    }
    // Mirrors test_inputfunc_case.py:46 _ShortAliasCmdSet — single-letter 'b' alias; only short-alias path can resolve inputs starting with 'b'
    private sealed class _ShortAliasCmdSet : CmdSet
    {
        private readonly Command _cmd = new _ShortAliasCmd();
        public override Command? Get(string key) => key == "b" ? _cmd : null;
        public override IReadOnlyList<string> GetKeys() => Array.Empty<string>();
    }

    [Fact]
    public void CapitalFirstLetterObeysNoAliasGuard()
    {
        using var env = GlobalTestEnv.Enter();
        // Patch settings.AUTO_COMMAND_ALIASING True — mirrors monkeypatch.setattr(settings, "AUTO_COMMAND_ALIASING", True)
        var origAliasing = AtherizSettings.Global.AutoCommandAliasing;
        var origCmdSet = CommandRegistry.LoggedIn;
        var field = typeof(CommandRegistry).GetField("_loggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        try
        {
            AtherizSettings.Global.AutoCommandAliasing = true;
            CommandDispatcher.SetSettings(AtherizSettings.Global);
            // Patch atheriz.inputfuncs.get_loggedin_cmdset to return _StubCmdSet() — mirrors monkeypatch.setattr("atheriz.inputfuncs.get_loggedin_cmdset", lambda: _StubCmdSet())
            var stub = new _StubCmdSet();
            field.SetValue(null, stub);

            var puppet = GameObject.Create("walker");
            puppet.Location = null!;

            // lowercase 'n' is blocked by the _NO_ALIAS_COMMANDS guard — assert dispatch_loggedin(puppet,"n")==None verbatim
            var lower = CommandDispatcher.DispatchLoggedIn(puppet, "n", immediate: true);
            Assert.Null(lower);

            // capitalized 'N' must be blocked identically — assert dispatch_loggedin(puppet,"N")==None verbatim
            var result = CommandDispatcher.DispatchLoggedIn(puppet, "N", immediate: true);
            Assert.Null(result);
            // Verbatim: both "n" and "N" are None — ensures case-insensitive guard
        }
        finally
        {
            AtherizSettings.Global.AutoCommandAliasing = origAliasing;
            CommandDispatcher.SetSettings(AtherizSettings.Global);
            field.SetValue(null, origCmdSet);
            CommandRegistry.ResetForTesting();
            var _ = CommandRegistry.LoggedIn;
            field.SetValue(null, origCmdSet);
        }
    }
}
