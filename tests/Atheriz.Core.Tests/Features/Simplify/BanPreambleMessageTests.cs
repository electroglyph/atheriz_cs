// Pins for the shared ban/unban preamble (BanHelper.TryResolveBanPreamble):
// both verbs run the same six steps, differing only in verb wording and the
// reason flag (ban reason optional text, unban has none).
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class BanPreambleMessageTests
{
    private static GameObject MakeCaller()
    {
        var c = GameObject.Create("Warden", privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakePc(string name)
    {
        var pc = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(pc);
        pc.PrivilegeLevel = Atheriz.Core.Privilege.Player;
        pc.ClearMessages();
        return pc;
    }

    [Fact]
    public void Ban_AccountWithoutAccount_FallsBackWithBanGerund()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var orphan = MakePc("Orphan");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Orphan", "--account"]));
        Assert.True(orphan.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("banning character only"));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Banned Orphan (character)."));
    }

    [Fact]
    public void Unban_AccountWithoutAccount_FallsBackWithUnbanGerund()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var orphan = MakePc("Orphan");
        orphan.IsBanned = true;
        new UnbanCommand().Run(caller, new UnbanCommand().Parser!.ParseArgs(["Orphan", "--account"]));
        Assert.False(orphan.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("unbanning character only"));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Unbanned Orphan (character)."));
    }

    [Fact]
    public void Ban_ReasonIsOptionalText_NotAGate()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Newbie");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Newbie"]));
        Assert.True(target.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m == "Banned Newbie (character).");
    }

    [Fact]
    public void Ban_ReasonAppendedToScopeMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var target = MakePc("Spammer");
        new BanCommand().Run(caller, new BanCommand().Parser!.ParseArgs(["Spammer", "-r", "spamming"]));
        Assert.True(target.IsBanned);
        Assert.Contains(caller.PeekMessages(), m => m == "Banned Spammer (character, reason: spamming).");
    }
}
