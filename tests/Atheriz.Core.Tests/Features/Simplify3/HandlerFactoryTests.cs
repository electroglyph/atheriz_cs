using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The node/map factory pairs share one core each: explicit settings pin the
// boot database, the parameterless overloads keep the ambient path, and both
// overloads publish the same singleton.
[Collection("Ported")]
public sealed class HandlerFactoryTests
{
    [Fact]
    public void GetNodeHandler_Settings_LoadsFromPinnedPath()
    {
        using var env = GlobalTestEnv.Enter();
        var dirA = Path.Combine(env.TempPath, "pinnedA");
        Directory.CreateDirectory(dirA);
        var seed = new NodeHandler(autoLoad: false);
        seed.AddArea(new NodeArea("pinnedA"));
        using (var db = new AtherizDbContext(dirA))
        {
            db.Database.EnsureCreated();
            seed.Save(db, force: true);
        }

        GlobalServices.Reset();
        var settingsA = new AtherizSettings { SavePath = dirA };
        var h = GlobalServices.GetNodeHandler(settingsA);

        Assert.NotNull(h.GetArea("pinnedA"));
        Assert.Same(h, NodeHandler.GetCurrent());
        Assert.Same(h, GlobalServices.TryGetNodeHandler());
    }

    [Fact]
    public void GetNodeHandler_Parameterless_UsesAmbientPath()
    {
        using var env = GlobalTestEnv.Enter();
        var h = GlobalServices.GetNodeHandler();
        Assert.Null(h.GetArea("pinnedA"));
        Assert.Same(h, NodeHandler.GetCurrent());
        Assert.Same(h, GlobalServices.GetNodeHandler(new AtherizSettings { SavePath = env.TempPath }));
    }

    [Fact]
    public void GetMapHandler_Overloads_ShareSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var a = GlobalServices.GetMapHandler(settings);
        var b = GlobalServices.GetMapHandler();
        Assert.Same(a, b);
        Assert.Same(a, GlobalServices.TryGetMapHandler());
        Assert.Null(a.GetMapInfo("no-such-area", 0));
    }

    [Fact]
    public void HandlerFactories_PreserveAmbientVsExplicitShape()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        var nodeCore = SourceScan.Region(src, "private static NodeHandler CreateNodeHandler(");
        Assert.Contains("new NodeHandler(autoLoad: true)", nodeCore);
        Assert.Contains("new NodeHandler(settings, autoLoad: true)", nodeCore);
        Assert.Contains("NodeHandler.SetCurrent(h)", nodeCore);
        var mapParameterless = SourceScan.Region(src, "public static MapHandler GetMapHandler()");
        Assert.Contains("AtherizSettings.Global", mapParameterless);
    }
}
