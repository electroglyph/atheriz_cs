using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Cross-area moves share one snapshot+create resolve: each kind removes from
// the source map and installs on the destination map.
[Collection("Ported")]
public sealed class B15MoveResolveTests
{
    private static GameObject MakeObj(string name)
    {
        var obj = GameObject.Create(name);
        ObjectRegistry.AddObject(obj);
        return obj;
    }

    [Fact]
    public void MoveListener_CrossArea_MovesListener()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var handler = new MapHandler(autoLoad: false);
            var fromCoord = new Coord("areaA", 0, 0, 0);
            var toCoord = new Coord("areaB", 5, 5, 0);
            var fromMap = handler.EnsureMapInfo("areaA", 0);
            var obj = MakeObj("listener");
            fromMap.AddListener(obj);
            Assert.True(fromMap.Listeners.ContainsKey(obj.Id));

            handler.MoveListener(obj, toCoord, fromCoord);

            Assert.False(fromMap.Listeners.ContainsKey(obj.Id));
            var toMap = handler.GetMapInfo("areaB", 0);
            Assert.NotNull(toMap);
            Assert.True(toMap!.Listeners.ContainsKey(obj.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void MoveMapable_CrossArea_MovesMapable()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var handler = new MapHandler(autoLoad: false);
            var fromCoord = new Coord("areaA", 1, 1, 0);
            var toCoord = new Coord("areaB", 2, 2, 0);
            var fromMap = handler.EnsureMapInfo("areaA", 0);
            var obj = MakeObj("mapable");
            fromMap.AddMapable(obj, notify: false);
            Assert.True(fromMap.Objects.ContainsKey(obj.Id));

            handler.MoveMapable(obj, toCoord, fromCoord);

            Assert.False(fromMap.Objects.ContainsKey(obj.Id));
            var toMap = handler.GetMapInfo("areaB", 0);
            Assert.NotNull(toMap);
            Assert.True(toMap!.Objects.ContainsKey(obj.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void MoveListenerAndMapable_CrossArea_MovesBoth()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var handler = new MapHandler(autoLoad: false);
            var fromCoord = new Coord("areaA", 3, 3, 0);
            var toCoord = new Coord("areaB", 4, 4, 0);
            var fromMap = handler.EnsureMapInfo("areaA", 0);
            var obj = MakeObj("both");
            fromMap.AddListener(obj);
            fromMap.AddMapable(obj, notify: false);
            Assert.True(fromMap.Listeners.ContainsKey(obj.Id));
            Assert.True(fromMap.Objects.ContainsKey(obj.Id));

            handler.MoveListenerAndMapable(obj, toCoord, fromCoord);

            Assert.False(fromMap.Listeners.ContainsKey(obj.Id));
            Assert.False(fromMap.Objects.ContainsKey(obj.Id));
            var toMap = handler.GetMapInfo("areaB", 0);
            Assert.NotNull(toMap);
            Assert.True(toMap!.Listeners.ContainsKey(obj.Id));
            Assert.True(toMap!.Objects.ContainsKey(obj.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }
}
