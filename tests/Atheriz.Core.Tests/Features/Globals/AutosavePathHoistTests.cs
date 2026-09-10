using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// Resolving the save path once per autosave tick keeps the checkpoint
// identical: the tick still journals through the explicit-settings database,
// every section commits independently, and the journal ends clean.
[Collection("Ported")]
public class AutosavePathHoistTests
{
    [Fact]
    public void AutosaveTick_ExplicitHandlers_CompletesAndMarksClean()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var settings = new AtherizSettings { TimeSystemEnabled = false };
            var mh = new MapHandler(autoLoad: false);
            var nh = new NodeHandler(autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gameTime: null);
            Assert.False(CheckpointJournal.IsDirty(env.TempPath));
            // A second tick over the same path stays clean and failure-free.
            Autosave.AutosaveTick(settings, mh, nh, gameTime: null);
            Assert.False(CheckpointJournal.IsDirty(env.TempPath));
        }
        finally { Autosave.Reset(); }
    }
}
