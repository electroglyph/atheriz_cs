using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Objects;

// Session/object flag restore across save/load.
[Collection("Ported")]
public class SessionRestoreTests
{
    // --- IsMapable/MapEnabled split brain ---

    [Fact]
    public void MapEnabled_False_SurvivesSaveLoadRoundtrip()
    {
        // MapEnabled and IsMapable setters stay in sync, and ApplyDtoFields
        // restores both, so MapEnabled=false survives a save/load roundtrip.
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("mapper");
        ObjectRegistry.AddObject(o);
        o.MapEnabled = false;
        Assert.False(o.IsMapable);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force: true); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var back = ObjectRegistry.Get(o.Id).FirstOrDefault();
        Assert.NotNull(back);
        Assert.False(back!.IsMapable);
        Assert.False(back.MapEnabled);
    }
}
