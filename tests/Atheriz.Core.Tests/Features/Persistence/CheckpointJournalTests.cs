using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Persistence;

// Checkpoint journal torn-write detection.
[Collection("Ported")]
public class CheckpointJournalTests
{
    private static AtherizSettings CrashSettings(string temp) =>
        new() { SavePath = temp, TimeSystemEnabled = false, AutosaveMinutes = 0 };

    // --- Torn multi-table checkpoint ---

    [Fact]
    public void TornCheckpoint_SurfacesError_NotSilent()
    {
        // Objects/map/nodes/time save in separate transactions; a crash
        // between tables leaves objects-new/map-old, and the next startup
        // must validate and report it instead of loading silently.
        using var env = GlobalTestEnv.Enter();
        var settings = CrashSettings(env.TempPath);
        var mh = GlobalServices.GetMapHandler();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var o = GameObject.Create("settler");
            ObjectRegistry.AddObject(o);
            mh.SetMapInfo("zona", 0, new MapInfo());
            var node = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            var gt = new GameTime(settings, autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gt);
            // Crash between tables: map committed, then process dies before the rest.
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var rows = db.MapData.ToList();
                Assert.NotEmpty(rows);
                db.MapData.RemoveRange(rows);
                db.SaveChanges();
            }
            // Crash residue: the checkpoint journal still says dirty.
            CheckpointJournal.MarkDirty(env.TempPath);
            Assert.True(CheckpointJournal.IsDirty(env.TempPath), "journal roundtrip must report dirty");
            GlobalServices.Reset();
            NodeHandler.SetCurrent(null);
            Exception? ex;
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                ex = Record.Exception(() => StartStop.DoStartup(settings: settings));
                log = cap.Read();
            }
            Assert.True(ex != null || !string.IsNullOrWhiteSpace(log),
                "torn checkpoint (map rows missing) must surface an error, not load silently");
        }
        finally
        {
            GlobalServices.Reset();
            NodeHandler.SetCurrent(null);
            try { StartStop.Reset(); } catch { }
        }
    }

    [Fact]
    public void IntactCheckpoint_LoadsSilently()
    {
        // Pin: a complete checkpoint must NOT trip the torn detector above.
        using var env = GlobalTestEnv.Enter();
        var settings = CrashSettings(env.TempPath);
        var mh = GlobalServices.GetMapHandler();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var o = GameObject.Create("settler");
            ObjectRegistry.AddObject(o);
            mh.SetMapInfo("zona", 0, new MapInfo());
            var node = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            var gt = new GameTime(settings, autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gt);
            GlobalServices.Reset();
            NodeHandler.SetCurrent(null);
            Exception? ex;
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                ex = Record.Exception(() => StartStop.DoStartup(settings: settings));
                log = cap.Read();
            }
            Assert.Null(ex);
            Assert.True(string.IsNullOrWhiteSpace(log), "intact load must stay silent, got: " + log);
        }
        finally
        {
            GlobalServices.Reset();
            NodeHandler.SetCurrent(null);
            try { StartStop.Reset(); } catch { }
        }
    }
}
