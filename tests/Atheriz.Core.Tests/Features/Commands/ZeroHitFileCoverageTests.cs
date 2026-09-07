using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;

namespace Atheriz.Core.Tests.Features.Commands;

// P3: direct tests for previously zero-hit files — CommandPermissions,
// HelpFormatter, BanHelper (internal, via reflection), DoorDirectionCommand
// (abstract path via OpenCommand), hook marker attributes, IJsonEntity.
public sealed class ZeroHitFileCoverageTests
{
    private static GameObject MakeCaller(string name, Privilege priv = Privilege.Player, bool isPc = false)
    {
        var c = GameObject.Create(name, isPc: isPc, privilege: priv);
        try { ObjectRegistry.AddObject(c); } catch { }
        c.ClearMessages();
        return c;
    }

    [Fact]
    public void CommandPermissions_BuilderFlag()
    {
        using var _ = GlobalTestEnv.Enter();
        var builder = GameObject.Create("b1", privilege: Privilege.Builder);
        var player = GameObject.Create("p1", privilege: Privilege.Player);
        Assert.True(CommandPermissions.IsBuilder(builder));
        Assert.False(CommandPermissions.IsBuilder(player));
    }

    [Fact]
    public void CommandPermissions_SuperUserFlag()
    {
        using var _ = GlobalTestEnv.Enter();
        var admin = GameObject.Create("a1", privilege: Privilege.Admin);
        var builder = GameObject.Create("b2", privilege: Privilege.Builder);
        Assert.True(CommandPermissions.IsSuperUser(admin));
        Assert.False(CommandPermissions.IsSuperUser(builder));
    }

    [Fact]
    public void HelpFormatter_ScreenreaderListsRows()
    {
        Command[] cmds = [new OpenCommand(), new CloseCommand()];
        var text = HelpFormatter.Format(cmds, screenreader: true, termWidth: 80);
        Assert.Contains("open", text);
        Assert.Contains("close", text);
        Assert.DoesNotContain("Category", text);
    }

    [Fact]
    public void HelpFormatter_TableHeaderAndTruncation()
    {
        Command[] cmds = [new OpenCommand()];
        var wide = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        Assert.Contains("Category", wide);
        Assert.Contains("Command", wide);
        Assert.Contains("Description", wide);
        var narrow = HelpFormatter.Format(cmds, screenreader: false, termWidth: 30);
        Assert.Contains("...", narrow);
    }

    private static object? InvokeBanHelper(string method, params object?[] args)
    {
        var t = typeof(OpenCommand).Assembly.GetType("Atheriz.Core.Commands.LoggedIn.BanHelper");
        Assert.NotNull(t);
        var m = t.GetMethod(method, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.NotNull(m);
        return m.Invoke(null, args);
    }

    [Fact]
    public void BanHelper_InvalidIdFormat()
    {
        using var _ = GlobalTestEnv.Enter();
        var caller = MakeCaller("bancaller");
        var result = InvokeBanHelper("ResolveTarget", caller, "#abc");
        Assert.Null(result);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Invalid ID format"));
    }

    [Fact]
    public void BanHelper_UnknownName()
    {
        using var _ = GlobalTestEnv.Enter();
        var caller = MakeCaller("bancaller2");
        var result = InvokeBanHelper("ResolveTarget", caller, "nosuchplayer");
        Assert.Null(result);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("No player character found named 'nosuchplayer'"));
    }

    [Fact]
    public void BanHelper_FindsPcAndHostNull()
    {
        using var _ = GlobalTestEnv.Enter();
        var caller = MakeCaller("bancaller3");
        var victim = GameObject.Create("victim1", isPc: true);
        try { ObjectRegistry.AddObject(victim); } catch { }
        var result = InvokeBanHelper("ResolveTarget", caller, "victim1");
        Assert.Same(victim, result);
        Assert.Null(InvokeBanHelper("GetHost", victim));
    }

    [Fact]
    public void DoorDirectionCommand_NoArgsPromptsWhat()
    {
        using var _ = GlobalTestEnv.Enter();
        var caller = MakeCaller("opener");
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("ZeroHit");
        var grid = new NodeGrid("ZeroHit", 0);
        grid.Nodes[(0, 0)] = new Node(new Coord("ZeroHit", 0, 0, 0));
        area.AddGrid(grid);
        nh.AddArea(area);
        caller.Location = new LocationRef.CoordLocation(new Coord("ZeroHit", 0, 0, 0));
        new OpenCommand().Run(caller, null);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Open what?"));
    }

    [Fact]
    public void DoorDirectionCommand_ExtraDescPinned()
    {
        Assert.Equal("Also accepts n,s,e,w,u,d as arguments.", new OpenCommand().ExtraDesc);
    }

    [Fact]
    public void HookAttributes_AreMethodMarkers()
    {
        var asm = typeof(GameObject).Assembly;
        foreach (var name in new[] { "Atheriz.Core.Objects.BeforeAttribute", "Atheriz.Core.Objects.AfterAttribute", "Atheriz.Core.Objects.ReplaceAttribute" })
        {
            var t = asm.GetType(name);
            Assert.NotNull(t);
            Assert.True(typeof(Attribute).IsAssignableFrom(t));
            var usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute));
            Assert.NotNull(usage);
            Assert.True((usage.ValidOn & AttributeTargets.Method) != 0);
        }
    }

    [Fact]
    public void IJsonEntity_ContractHoldsForAllRows()
    {
        IJsonEntity[] rows = [new ObjectRow(), new MapDataRow(), new AreaRow(), new TransitionRow(), new DoorRow(), new GameTimeRow()];
        Assert.Equal(6, rows.Length);
        foreach (var r in rows)
        {
            r.Data = "{\"a\":1}";
            Assert.Equal("{\"a\":1}", r.Data);
        }
    }
}
