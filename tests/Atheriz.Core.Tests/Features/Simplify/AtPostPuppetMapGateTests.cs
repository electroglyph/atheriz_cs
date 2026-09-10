using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Map gates in AtPostPuppet: the global flag is read once per gate (two
// observations, never hoisted), so disabling either gate suppresses the
// map_enable send while the rest of the login still runs.
[Collection("Ported")]
public class AtPostPuppetMapGateTests
{
    private static TestConnection RunPostPuppet(GameObject obj)
    {
        var conn = new TestConnection("mapgate");
        obj.Session = new Session(conn);
        obj.AtPostPuppet();
        return conn;
    }

    [Fact]
    public void AtPostPuppet_GlobalMapDisabled_SendsNoMapEnable()
    {
        bool prev = AtherizSettings.Global.MapEnabled;
        AtherizSettings.Global.MapEnabled = false;
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var obj = GameObject.Create("pc");
            ObjectRegistry.AddObject(obj);
            room.AddObject(obj);

            var conn = RunPostPuppet(obj);

            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
            Assert.DoesNotContain(conn.Sent, s => s.Cmd == "map_enable");
        }
        finally
        {
            AtherizSettings.Global.MapEnabled = prev;
            ObjectRegistry.ClearAll();
        }
    }

    [Fact]
    public void AtPostPuppet_SelfMapDisabled_SendsNoMapEnable()
    {
        // The listener gate still resolves a handler (kept side-effect free
        // with an unloadable instance), but the second gate fails first.
        using var env = GlobalTestEnv.Enter();
        GlobalServices.SetMapHandler(new MapHandler(AtherizSettings.Default, autoLoad: false));
        bool prev = AtherizSettings.Global.MapEnabled;
        AtherizSettings.Global.MapEnabled = true;
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var obj = GameObject.Create("pc");
            ObjectRegistry.AddObject(obj);
            obj.MapEnabled = false;
            room.AddObject(obj);

            var conn = RunPostPuppet(obj);

            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
            Assert.DoesNotContain(conn.Sent, s => s.Cmd == "map_enable");
        }
        finally
        {
            AtherizSettings.Global.MapEnabled = prev;
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }
}
