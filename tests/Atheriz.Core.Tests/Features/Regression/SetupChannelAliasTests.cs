using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Regression;

// Pins for setup/channel-alias/command-shape behavior.
[Collection("Ported")]
public class SetupChannelAliasTests
{
    // One MapInfo name going forward: EnsureMapInfo does the lookup, the old
    // name survives only as an obsolete alias.
    [Fact]
    public void MapHandler_AliasRetired_KeptNameServes()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = new MapHandler(autoLoad: false);
        var a = mh.EnsureMapInfo("setupalias", 0);
        Assert.Same(a, mh.EnsureMapInfo("setupalias", 0));
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        Assert.Contains("[Obsolete(\"Use EnsureMapInfo", src);
    }

    // The tick re-registration sweep uses the snapshotting accessor directly
    // instead of nesting the same read lock around it.
    [Fact]
    public void StartStop_TickSweep_UsesSnapshottingAccessor()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = SourceScan.Region(src, "Port of startstop.py:103-122 node handler grids");
        Assert.Contains("nh.GetAreas()", region);
        Assert.DoesNotContain("nh.Lock.EnterReadLock", region);
    }

    // Fresh-world setup resets the registry once (ClearAll already resets the
    // Id generator) and reports seed faults instead of swallowing them.
    [Fact]
    public void InitialSetup_ResetOnce_SeedFaultsLogged()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        var region = SourceScan.Region(src, "public static void DoSetup(");
        Assert.DoesNotContain("IdGenerator.SetId(-1)", region);
        Assert.DoesNotContain("GlobalServices.Reset(); } catch { }", region);
        Assert.DoesNotContain("GetSalt(absSecret); } catch {}", region);
        Assert.DoesNotContain("SetSalt(SaltProvider.GetSalt(absSecret)); } catch {}", region);
        Assert.Contains("Singleton reset warning", region);
        // The salt warnings live in the ReseedForGame funnel that owns them.
        var reseed = SourceScan.Region(SourceScan.Read("src", "Atheriz.Core", "Globals", "SaltProvider.cs"), "public static void ReseedForGame(");
        Assert.Contains("Salt re-seed warning", reseed);
        Assert.Contains("Default salt seed warning", reseed);
    }

    // The seed world is published through the singleton setters so
    // GlobalServices readers and GetCurrent readers agree.
    [Fact]
    public void InitialSetup_SeedWorld_PublishedThroughSingletons()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        var region = SourceScan.Region(src, "public static void DoSetup(");
        Assert.Contains("GlobalServices.SetNodeHandler(nh)", region);
        Assert.Contains("GlobalServices.SetMapHandler(mh)", region);
        Assert.DoesNotContain("NodeHandler.SetCurrent(nh)", region);
    }

    // A property whose type can never come from text says so, instead of
    // borrowing the read-only message. (Location stays read-only: its entry
    // has no setter at all. Node.Coord is settable but never text-
    // convertible, so it reaches the cast path.)
    [Fact]
    public void Set_NeverConvertible_ReportsTextLimitation()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("setupalias", 0, 0, 0));
            ObjectRegistry.AddObject(node);
            var admin = GameObject.Create("setter", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            Assert.True(admin.MoveTo(node, announce: false));
            var j = CommandDispatcher.DispatchLoggedIn(admin, "set here Coord nonsense", immediate: true);
            RunJob(j);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("cannot be set from text", msgs);
            Assert.DoesNotContain("read-only attribute", msgs);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Spam registers each account once (Create already registers) and the
    // closing message names the names-only file for what it is.
    [Fact]
    public void Spam_RegistersOnce_NamesFileMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "spam");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("spammer", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var job = CommandDispatcher.DispatchLoggedIn(admin, "spam 1", immediate: true);
            RunJob(job);
            var accounts = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.StartsWith("account"));
            Assert.Single(accounts);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("Names saved to", msgs);
            Assert.DoesNotContain("Credentials saved", msgs);
            var content = File.ReadAllText(Path.Combine(tmp, "spam_accounts.txt"));
            Assert.Contains("account1", content);
            Assert.DoesNotContain("password1", content);
            // Create already registers: no second add in the command body.
            var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "SpamCommand.cs");
            Assert.DoesNotContain("AddObject(account)", src);
        }
        finally
        {
            AtherizSettings.Global.SavePath = origSave;
            ObjectRegistry.ClearAll();
        }
    }

    // Renaming a channel command after registration orphans it from lookup:
    // the key must be set before adding, as documented on SetKey.
    [Fact]
    public void ChannelCommand_RenameAfterAdd_OrphansLookup()
    {
        var cmd = new BaseChannelCommand();
        cmd.SetKey("chan");
        var set = new CmdSet();
        set.Add(cmd);
        Assert.Same(cmd, set.Get("chan"));
        cmd.SetKey("chan-renamed");
        Assert.Null(set.Get("chan-renamed"));
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "BaseChannelCommand.cs");
        Assert.Contains("before the command is added", src);
    }

    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }
}
