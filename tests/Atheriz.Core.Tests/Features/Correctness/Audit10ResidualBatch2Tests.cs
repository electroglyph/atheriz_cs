using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class Audit10ResidualBatch2Tests
{
    [Fact]
    public void ResetHandler_WindowsSweep_SkipsWipeLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        Assert.Contains("OperatingSystem.IsWindows()", src, StringComparison.Ordinal);
        Assert.Contains("WipeLockFileName", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ToInt_OutOfRangeLong_ThrowsInsteadOfWrapping()
    {
        var m = typeof(Atheriz.Core.Network.InputFuncs).GetMethod("ToInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(m);
        // Via reflection the checked overflow surfaces wrapped in
        // TargetInvocationException; the point is it throws instead of
        // wrapping 2^32+1 to 1.
        var ex1 = Assert.Throws<System.Reflection.TargetInvocationException>(() => m!.Invoke(null, new object?[] { 4294967297L }));
        Assert.IsType<System.OverflowException>(ex1.InnerException);
        var ex2 = Assert.Throws<System.Reflection.TargetInvocationException>(() => m!.Invoke(null, new object?[] { -4294967295L }));
        Assert.IsType<System.OverflowException>(ex2.InnerException);
        Assert.Equal(1, (int)m!.Invoke(null, new object?[] { 1L })!);
    }

    [Fact]
    public void MapEditLegend_OutOfRangeLongCoord_Rejects()
    {
        using var env = GlobalTestEnv.Enter();
        Atheriz.Core.Globals.MapEdit.Reset();
        try
        {
            var mh = GlobalServices.GetMapHandler();
            var mi = new MapInfo("TestArea");
            mh.SetMapInfo("TestArea", 0, mi);
            Atheriz.Core.Network.InputFuncs.MapHandlerFactory = () => mh;
            var conn = new TestConnection("legend-range");
            conn.ClientHost = "10.0.0.1";
            var key = Atheriz.Core.Globals.MapEdit.Grant("10.0.0.1", "TestArea", 0);
            new Atheriz.Core.Network.InputFuncs().MapEditLegendHandler(conn, new List<object?> { key, 0, new List<object?>() }, new Dictionary<string, object?>());
            var hk = conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no handshake key");
            conn.ClearSent();
            var legend = new List<object?> { new Dictionary<string, object?> { ["symbol"] = "#", ["coord"] = new List<object?> { 4294967297L, 0L } } };
            new Atheriz.Core.Network.InputFuncs().MapEditLegendHandler(conn, new List<object?> { hk, 1, legend }, new Dictionary<string, object?>());
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
        }
        finally
        {
            Atheriz.Core.Globals.MapEdit.Reset();
            Atheriz.Core.Network.InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        }
    }

    [Fact]
    public void AddArgument_PairWithRequired_KeepsBothAliases()
    {
        var p = new GameArgumentParser("prog", "", addHelp: false);
        p.AddArgument("-n", "--num", type: typeof(int), required: true);
        var a = p.ParseArgs(["-n", "5"]);
        Assert.Equal(5, (int)a["num"]!);
        var b = p.ParseArgs(["--num", "7"]);
        Assert.Equal(7, (int)b["num"]!);
    }

    [Fact]
    public void AddArgument_PairWithDefault_KeepsBothAliases()
    {
        var p = new GameArgumentParser("prog", "", addHelp: false);
        p.AddArgument("-n", "--num", type: typeof(int), defaultValue: 3);
        var a = p.ParseArgs(["-n", "5"]);
        Assert.Equal(5, (int)a["num"]!);
        var b = p.ParseArgs(["--num", "7"]);
        Assert.Equal(7, (int)b["num"]!);
        var c = p.ParseArgs([]);
        Assert.Equal(3, (int)c["num"]!);
    }

    private sealed class ThrowDeleteHooks
    {
        [Replace]
        public bool ThrowDelete(GameObject? c) => throw new InvalidOperationException("cursed-entry");
    }

    [Fact]
    public void Delete_ThrowingAtDeleteEntry_IsVetoNotEscape()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("pin-entry-cursed", isItem: true);
        ObjectRegistry.AddObject(target);
        var builder = GameObject.Create("pin-entry-builder", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(builder);
        var hooks = new ThrowDeleteHooks();
        target.InstallHook("at_delete", (Func<GameObject?, bool>)hooks.ThrowDelete);
        Assert.Throws<InvalidOperationException>(() => target.AtDelete(builder));

        var ex = Record.Exception(() => target.Delete(builder, recursive: false));
        Assert.Null(ex);
        Assert.False(target.IsDeleted);
    }

    [Fact]
    public void DeletePc_OfflineSessionNull_FreesAccountCharacterSlot()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("pin-offline-acc", "pw-offline-xyz");
        ObjectRegistry.AddObject(acc);
        var pc1 = GameObject.Create("pin-off-1", isPc: true);
        var pc2 = GameObject.Create("pin-off-2", isPc: true);
        ObjectRegistry.AddObject(pc1);
        ObjectRegistry.AddObject(pc2);
        Assert.True(acc.TryAddCharacter(pc1, 2));
        Assert.True(acc.TryAddCharacter(pc2, 2));
        Assert.Null(pc1.Session);
        var builder = GameObject.Create("pin-off-builder", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(builder);

        Assert.NotNull(pc1.Delete(builder, recursive: false));

        Assert.DoesNotContain(pc1.Id, acc.Characters);
        var pc3 = GameObject.Create("pin-off-3", isPc: true);
        ObjectRegistry.AddObject(pc3);
        Assert.True(acc.TryAddCharacter(pc3, 2));
    }

    [Fact]
    public void AddObjectUnique_PredicateRunsOutsideRegistryLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = SourceScan.Region(src, "public static void AddObjectUnique(");
        Assert.Contains("snap.Any(predicate)", region, StringComparison.Ordinal);
        Assert.DoesNotContain("AllObjects.Values.Any(predicate)", region, StringComparison.Ordinal);
        Assert.Contains("_version", region, StringComparison.Ordinal);
    }
}
