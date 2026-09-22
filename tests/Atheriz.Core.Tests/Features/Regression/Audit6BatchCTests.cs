using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for audit6 findings F14-F19.
[Collection("Ported")]
public sealed class Audit6BatchCTests
{
    private static GameObject MakePlayer(string name, bool isPc = false)
    {
        var c = GameObject.Create(name, "", isPc: isPc, privilege: Privilege.Player);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakeSuperuser(string name = "Root")
    {
        var c = GameObject.Create(name, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    // F14: an unresolvable lock policy must deny (fail closed), never vanish.
    [Fact]
    public void ApplyDtoFields_UnknownPolicy_DeniesAccess()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = MakePlayer("locktest");
        var caller = MakePlayer("caller");
        var dto = new GameObjectDto
        {
            Id = obj.Id,
            Name = "locktest",
            Locks = [new LockDefDto { Name = "view", Policy = "frobnicate_policy" }],
        };
        GameObject.ApplyDtoFields(obj, dto, null);
        Assert.False(obj.Access(caller, "view"));
    }

    // F14: the ad-hoc 'custom' lambda was never persistable — still dropped (allowed), loudly.
    [Fact]
    public void ApplyDtoFields_CustomPolicy_DroppedWithAllow()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = MakePlayer("locktest2");
        var caller = MakePlayer("caller2");
        var dto = new GameObjectDto
        {
            Id = obj.Id,
            Name = "locktest2",
            Locks = [new LockDefDto { Name = "view", Policy = "custom" }],
        };
        GameObject.ApplyDtoFields(obj, dto, null);
        Assert.True(obj.Access(caller, "view"));
    }

    // F15: account delete journals + tears down, with no mid-game DB write.
    [Fact]
    public void Account_Delete_JournalsAndTearsDown()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = new Account();
        ObjectRegistry.AddObject(acc);
        var follower = MakePlayer("follower");
        follower.Following = acc.Id;
        acc.AddFollower(follower.Id);
        var ch = Channel.Create("f15chan");
        acc.Subscribe(ch);
        Assert.True(acc.Delete(null));
        Assert.True(acc.IsDeleted);
        Assert.Empty(ObjectRegistry.Get(acc.Id));
        // Shared teardown ran: follows detached, channel memberships gone.
        Assert.Null(follower.Following);
        Assert.Empty(acc.ChannelsSnapshot);
    }

    // F16: season boundaries derive from MonthsPerYear (6x30 calendar).
    [Fact]
    public void GameTime_CustomMonthsPerYear_ProportionalSeasons()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, DaysPerMonth = 30, MonthsPerYear = 6 };
        var gt = new GameTime(settings, autoLoad: false);
        // 1440 ticks per day (60s ticks, 86400s days). Day 30 of a 180-day
        // year: old month-band code called this winter (month 2); the
        // proportional boundary (180*2/12=30) starts spring here.
        gt.Ticks = 30 * 1440;
        Assert.Equal("spring", gt.GetTime().Season);
    }

    // F16: default 12-month calendar is unchanged (month 3 -> spring, week 1).
    [Fact]
    public void GameTime_DefaultCalendar_SeasonsUnchanged()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(settings, autoLoad: false);
        gt.Ticks = 60 * 1440;
        var info = gt.GetTime();
        Assert.Equal("spring", info.Season);
        Assert.Equal(1, info.WeekOfSeason);
    }

    // F17: teen ordinals test the last two digits (111th, not 111st).
    [Fact]
    public void GameTime_OrdinalDay_TeensUseTh()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, DaysPerMonth = 120, MonthsPerYear = 12 };
        var gt = new GameTime(settings, autoLoad: false);
        gt.Ticks = 110 * 1440; // day-of-month 111
        Assert.Contains("111th", gt.GetTime().FormattedShort);
    }

    // F18: typo fallback must not suggest commands the caller cannot use.
    [Fact]
    public void NoneCommand_DoesNotSuggestInaccessibleCommands()
    {
        using var env = GlobalTestEnv.Enter();
        var low = MakePlayer("low");
        new NoneCommand().Run(low, "setx");
        var text = string.Join("\n", low.PeekMessages());
        Assert.DoesNotContain("\"set\"", text);
    }

    // F18: visible commands are still suggested.
    [Fact]
    public void NoneCommand_StillSuggestsVisibleCommands()
    {
        using var env = GlobalTestEnv.Enter();
        var low = MakePlayer("low2");
        new NoneCommand().Run(low, "lookk");
        var text = string.Join("\n", low.PeekMessages());
        Assert.Contains("\"look\"", text);
    }

    // F19: superuser rename to a taken character name is refused.
    [Fact]
    public void Set_Name_DuplicateBlockedForSuperuser()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var taken = GameObject.Create("TakenName", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(taken);
        var target = GameObject.Create("Target", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(target);
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "TakenName"]));
        Assert.Contains(root.PeekMessages(), m => m == "Character with this name (TakenName) already exists.");
        Assert.Equal("Target", target.Name);
    }

    // F19: rename to the target's own current name is not a collision.
    [Fact]
    public void Set_Name_SameNameAllowed()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = GameObject.Create("TargetSelf", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(target);
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "TargetSelf"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set TargetSelf.name = 'TargetSelf'");
        Assert.Equal("TargetSelf", target.Name);
    }

    // F19: reserved words are rejected on rename (shares F11 validation).
    [Fact]
    public void Set_Name_ReservedWordRejected()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = GameObject.Create("TargetR", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(target);
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "here"]));
        Assert.Contains(root.PeekMessages(), m => m == "That name is reserved.");
        Assert.Equal("TargetR", target.Name);
    }

    // F19: non-superusers still hit the protected gate before validation.
    [Fact]
    public void Set_Name_AsBuilder_HitsProtectedGate()
    {
        using var env = GlobalTestEnv.Enter();
        var bob = MakePlayer("Bob");
        bob.PrivilegeLevel = Privilege.Builder;
        var target = MakePlayer("TargetB");
        target.PrivilegeLevel = Privilege.Player;
        var cmd = new SetCommand();
        cmd.Run(bob, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "NewName"]));
        Assert.Contains(bob.PeekMessages(), m => m == "'name' is protected and cannot be set.");
        Assert.Equal("TargetB", target.Name);
    }
}
