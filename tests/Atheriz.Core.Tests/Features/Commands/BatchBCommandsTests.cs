using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Batch B of the commands.md rollout: page, time extension, info (both
// sets), color, who/doing, sessions.
[Collection("Ported")]
public class BatchBCommandsTests
{
    private static void Run(GameObject caller, string text)
    {
        var job = CommandDispatcher.DispatchLoggedIn(caller, text, immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    // Two online accounts on a fresh manager installed as the global
    // instance. Returns the previous instance for finally-restore.
    private static ConnectionManager? InstallManager(
        out TestConnection connA, out Account accA, out GameObject charA,
        out TestConnection connB, out Account accB, out GameObject charB)
    {
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        ConnectionManager.GlobalInstance = mgr;
        accA = Account.Create("batchb-alice", "password1");
        accB = Account.Create("batchb-bob", "password1");
        charA = GameObject.Create("Alice", isPc: true);
        charB = GameObject.Create("Bob", isPc: true);
        ObjectRegistry.AddObject(charA);
        ObjectRegistry.AddObject(charB);
        accA.AddCharacter(charA);
        accB.AddCharacter(charB);
        connA = new TestConnection("sess-a");
        connB = new TestConnection("sess-b");
        Assert.True(mgr.RegisterConnection("sess-a", connA));
        Assert.True(mgr.RegisterConnection("sess-b", connB));
        connA.Session.Account = accA;
        connA.Session.Puppet = charA;
        charA.Session = connA.Session;
        connB.Session.Account = accB;
        connB.Session.Puppet = charB;
        charB.Session = connB.Session;
        return prev;
    }

    [Fact]
    public void Page_DeliversToOnlineAccountPuppet()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out var charB);
        try
        {
            Run(charA, "page batchb-bob = hello there");
            Assert.Contains(charB.PeekMessages(), m => m.Contains("hello there"));
            Assert.Single(charA.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Page_Bare_ShowsLastReceived()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out var charB);
        try
        {
            Run(charA, "page batchb-bob = hello there");
            charB.ClearMessages();
            Run(charB, "page");
            var messages = charB.PeekMessages();
            Assert.Single(messages);
            Assert.Contains("hello there", messages[0]);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Page_SentSwitch_ShowsLastSent()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out _);
        try
        {
            Run(charA, "page batchb-bob = hello there");
            charA.ClearMessages();
            Run(charA, "page sent");
            var messages = charA.PeekMessages();
            Assert.Single(messages);
            Assert.Contains("hello there", messages[0]);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Page_OfflineAccount_ReportedAndUndelivered()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out var charB);
        try
        {
            var ghost = Account.Create("batchb-ghost", "password1");
            Run(charA, "page batchb-ghost = hi");
            Assert.Single(charA.PeekMessages());
            Assert.Contains(charA.PeekMessages(), m => m.Contains(ghost.Name));
            Assert.Empty(charB.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Page_UnknownTarget_NobodyElseHears()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out var charB);
        try
        {
            Run(charA, "page zzz = hi");
            Assert.Single(charA.PeekMessages());
            Assert.Empty(charB.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Page_CharacterTarget_ReachesOnlinePuppet()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out var charB);
        try
        {
            Run(charA, "page Bob = char hi");
            Assert.Contains(charB.PeekMessages(), m => m.Contains("char hi"));
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Time_Extended_FirstLineIsGameTime()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Tim");
        Run(hero, "time");
        var messages = hero.PeekMessages();
        Assert.Equal(3, messages.Count);
        Assert.Equal(GlobalServices.GetGameTime().GetTime().Formatted, messages[0]);
    }

    [Fact]
    public void Uptime_Alias_AnswersLikeTime()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Tim");
        Run(hero, "uptime");
        var messages = hero.PeekMessages();
        Assert.Equal(3, messages.Count);
        Assert.Equal(GlobalServices.GetGameTime().GetTime().Formatted, messages[0]);
    }

    [Fact]
    public void Info_LoggedIn_ReportsBlock()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Snoopy");
        Run(hero, "info");
        var messages = hero.PeekMessages();
        Assert.Single(messages);
        Assert.Contains("## BEGIN INFO", messages[0]);
        Assert.Contains(AtherizSettings.Global.ServerName, messages[0]);
        Assert.Contains("## END INFO", messages[0]);
    }

    [Fact]
    public void Info_UnloggedIn_ReportsSameBlock()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        new Atheriz.Core.Commands.UnloggedIn.InfoCommand().Run(conn, "");
        var text = string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
        Assert.Contains("## BEGIN INFO", text);
        Assert.Contains(AtherizSettings.Global.ServerName, text);
        Assert.Contains("## END INFO", text);
    }

    [Fact]
    public void Who_ListsOnlineSessions()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out var accA, out var charA, out _, out var accB, out _);
        try
        {
            Run(charA, "who");
            var messages = charA.PeekMessages();
            Assert.Single(messages);
            Assert.Contains(accA.Name, messages[0]);
            Assert.Contains(accB.Name, messages[0]);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Doing_HidesSessionDetails()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out var accA, out var charA, out _, out _, out _);
        try
        {
            Run(charA, "doing");
            var messages = charA.PeekMessages();
            Assert.Single(messages);
            Assert.Contains(accA.Name, messages[0]);
            Assert.DoesNotContain("sess-a", messages[0]);
            Assert.DoesNotContain("sess-b", messages[0]);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Who_Empty_ReportsNobody()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = new ConnectionManager();
        try
        {
            var hero = PortedHelpers.MakeCaller("Lonely");
            Run(hero, "who");
            Assert.Single(hero.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Sessions_ListsOwnOnly()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InstallManager(out _, out _, out var charA, out _, out _, out _);
        try
        {
            Run(charA, "sessions");
            var messages = charA.PeekMessages();
            Assert.Single(messages);
            Assert.Contains("sess-a", messages[0]);
            Assert.DoesNotContain("sess-b", messages[0]);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Sessions_NoAccount_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("NoAcct");
        Run(hero, "sessions");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Color_PrintsAnsiCharts()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Arts");
        Run(hero, "color");
        var messages = hero.PeekMessages();
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.Contains("\x1b[", m));
    }
}
