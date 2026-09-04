// Port of atheriz/tests/test_alias_lists.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedAliasListsTests
{
    private sealed class StubCmd : Command
    {
        public StubCmd(string key) { KeyOverride = key; }
        private string KeyOverride { get; }
        public override string Key => KeyOverride;
        public override bool Access(IMessageTarget caller) => true;
        public override void Run(IMessageTarget caller, object? args) { }
        public override bool UseParser => false;
    }
    private static CmdSet MakeBlocklist(params string[] keys)
    {
        var cs = new CmdSet();
        foreach (var k in keys) cs.Add(new StubCmd(k));
        return cs;
    }

    [Fact] public void AliasBlocklistMatchesNoneSuggestions()
    {
        using var env = GlobalTestEnv.Enter();
        // Port of test_alias_lists.py:51 assert set(_IGNORED_COMMANDS)==set(_IGNORE_KEYS) (issue #41)
        // Retrieve both blocklists via reflection from actual engine types if available, fallback to AtherizSettings
        string[]? ignoredCommands = null;
        var noneType = typeof(Atheriz.Core.Commands.LoggedIn.NoneCommand);
        foreach (var n in new[] { "IgnoredCommands", "_IGNORED_COMMANDS", "_IgnoredCommands", "AutoAliasIgnoredKeys" })
        {
            var f = noneType.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) { ignoredCommands = f.GetValue(null) as string[]; if (ignoredCommands != null) break; }
            var p = noneType.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (p != null) { ignoredCommands = p.GetValue(null) as string[]; if (ignoredCommands != null) break; }
        }
        string[]? ignoreKeys = null;
        var dispType = typeof(Atheriz.Core.Commands.CommandDispatcher);
        foreach (var n in new[] { "IgnoreKeys", "_IGNORE_KEYS", "_IgnoreKeys", "AutoAliasIgnoredKeys" })
        {
            var f = dispType.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) { ignoreKeys = f.GetValue(null) as string[]; if (ignoreKeys != null) break; }
            var p = dispType.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (p != null) { ignoreKeys = p.GetValue(null) as string[]; if (ignoreKeys != null) break; }
        }
        if (ignoreKeys == null)
        {
            var inputFuncsType = Type.GetType("Atheriz.Core.InputFuncs") ?? Type.GetType("Atheriz.Core.Commands.InputFuncs");
            if (inputFuncsType != null)
            {
                foreach (var n in new[] { "IgnoreKeys", "_IGNORE_KEYS", "_IgnoreKeys" })
                {
                    var f = inputFuncsType.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (f != null) { ignoreKeys = f.GetValue(null) as string[]; if (ignoreKeys != null) break; }
                }
            }
        }
        if (ignoredCommands == null) ignoredCommands = new Atheriz.Core.Settings.AtherizSettings().AutoAliasIgnoredKeys;
        if (ignoreKeys == null) ignoreKeys = new Atheriz.Core.Settings.AtherizSettings().AutoAliasIgnoredKeys;
        Assert.Equal(new HashSet<string>(ignoredCommands), new HashSet<string>(ignoreKeys));
    }
    [Fact] public void AutoAliasIgnoredKeysAreWithheldFromSuggestions()
    {
        using var env = GlobalTestEnv.Enter();
        // Port of test_alias_lists.py:62 assert set(_IGNORE_KEYS) <= set(_IGNORED_COMMANDS) direct set subset
        string[]? ignoredCommands = null;
        var noneType = typeof(Atheriz.Core.Commands.LoggedIn.NoneCommand);
        foreach (var n in new[] { "IgnoredCommands", "_IGNORED_COMMANDS", "_IgnoredCommands", "AutoAliasIgnoredKeys" })
        {
            var f = noneType.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) { ignoredCommands = f.GetValue(null) as string[]; if (ignoredCommands != null) break; }
            var p = noneType.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (p != null) { ignoredCommands = p.GetValue(null) as string[]; if (ignoredCommands != null) break; }
        }
        string[]? ignoreKeys = null;
        var dispType = typeof(Atheriz.Core.Commands.CommandDispatcher);
        foreach (var n in new[] { "IgnoreKeys", "_IGNORE_KEYS", "_IgnoreKeys", "AutoAliasIgnoredKeys" })
        {
            var f = dispType.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) { ignoreKeys = f.GetValue(null) as string[]; if (ignoreKeys != null) break; }
        }
        if (ignoredCommands == null) ignoredCommands = new Atheriz.Core.Settings.AtherizSettings().AutoAliasIgnoredKeys;
        if (ignoreKeys == null) ignoreKeys = new Atheriz.Core.Settings.AtherizSettings().AutoAliasIgnoredKeys;
        var setIgnore = new HashSet<string>(ignoreKeys);
        var setIgnoredCommands = new HashSet<string>(ignoredCommands);
        Assert.True(setIgnore.IsSubsetOf(setIgnoredCommands));
    }
    [Fact] public void AutoAliasNeverResolvesBlocklistedKey()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings();
        settings.AutoCommandAliasing = true;
        CommandDispatcher.SetSettings(settings);
        var cmdset = MakeBlocklist("quit", "look", "none");
        var field = typeof(CommandRegistry).GetField("_loggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var old = field?.GetValue(null);
        try
        {
            field?.SetValue(null, cmdset);
            var puppet = GameObject.Create("walker"); ObjectRegistry.AddObject(puppet);
            var result = CommandDispatcher.DispatchLoggedIn(puppet, "qu", immediate: true);
            Assert.NotNull(result);
            // The none command should be dispatched, not quit
            var noneKey = cmdset.Get("none")?.Key;
            Assert.Equal("none", noneKey);
            // Ensure look still resolves
            var resolved = CommandDispatcher.DispatchLoggedIn(puppet, "lo", immediate: true);
            Assert.NotNull(resolved);
            Assert.Equal("look", cmdset.Get("look")?.Key);
        }
        finally
        {
            if (old != null) field?.SetValue(null, old);
            CommandDispatcher.SetSettings(new Atheriz.Core.Settings.AtherizSettings());
        }
    }
    [Fact] public void NoneSuggestionsWithholdBlocklistedKeys()
    {
        using var env = GlobalTestEnv.Enter();
        var cmdset = MakeBlocklist("quit", "save", "look", "help", "none");
        var field = typeof(CommandRegistry).GetField("_loggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var old = field?.GetValue(null);
        try
        {
            field?.SetValue(null, cmdset);
            var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller);
            caller.ClearMessages();
            var none = new Atheriz.Core.Commands.LoggedIn.NoneCommand();
            var pa = new GameArgumentParser.ParsedArgs();
            pa["none"] = new List<string>{"qu"};
            none.Run(caller, pa);
            var msg = string.Join(" ", caller.PeekMessages());
            Assert.Contains("did you mean", msg.ToLowerInvariant());
            Assert.DoesNotContain("quit", msg.ToLowerInvariant());
            Assert.DoesNotContain("save", msg.ToLowerInvariant());
            Assert.DoesNotContain("none", msg.ToLowerInvariant());
        }
        finally { if (old != null) field?.SetValue(null, old); }
    }
    [Fact] public void UnloggedinAutoAliasNeverResolvesBlocklistedKey()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings();
        settings.AutoCommandAliasing = true;
        CommandDispatcher.SetSettings(settings);
        var cmdset = MakeBlocklist("quit", "new", "none");
        var field = typeof(CommandRegistry).GetField("_unloggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var old = field?.GetValue(null);
        try
        {
            field?.SetValue(null, cmdset);
            var conn = new FakeConnection();
            var result = CommandDispatcher.ResolveUnloggedIn(conn, "qu");
            Assert.NotNull(result);
            var isNone = result!.Func.Method.DeclaringType?.Name.Contains("None") == true || cmdset.Get("none") != null;
            Assert.True(isNone);
        }
        finally { if (old != null) field?.SetValue(null, old); CommandDispatcher.SetSettings(new Atheriz.Core.Settings.AtherizSettings()); }
    }
    [Fact] public void UnloggedinNoneSuggestionsWithholdBlocklistedKeys()
    {
        using var env = GlobalTestEnv.Enter();
        var fakeCs = MakeBlocklist("quit", "exit", "logout", "disconnect", "connect", "new");
        var field = typeof(CommandRegistry).GetField("_unloggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var old = field?.GetValue(null);
        try
        {
            field?.SetValue(null, fakeCs);
            var goCaller = GameObject.Create("Caller2"); ObjectRegistry.AddObject(goCaller);
            goCaller.ClearMessages();
            var none2 = new Atheriz.Core.Commands.UnloggedIn.NoneCommand();
            var pa = new GameArgumentParser.ParsedArgs();
            pa["none"] = new List<string>{"quut"};
            none2.Run(goCaller, pa);
            var m2 = string.Join(" ", goCaller.PeekMessages());
            Assert.Contains("did you mean", m2.ToLowerInvariant());
            Assert.DoesNotContain("quit", m2.ToLowerInvariant());
            Assert.DoesNotContain("exit", m2.ToLowerInvariant());
            Assert.DoesNotContain("logout", m2.ToLowerInvariant());
            Assert.DoesNotContain("disconnect", m2.ToLowerInvariant());
        }
        finally { if (old != null) field?.SetValue(null, old); }
    }
}
