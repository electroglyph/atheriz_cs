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
        // The two map settings are independent and each survives a roundtrip on
        // its own: turning the display off must not drop map tracking.
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("mapper");
        ObjectRegistry.AddObject(o);
        o.IsMapable = true;
        o.MapEnabled = false;
        Assert.True(o.IsMapable);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force: true); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var back = ObjectRegistry.Get(o.Id).FirstOrDefault();
        Assert.NotNull(back);
        Assert.True(back!.IsMapable);
        Assert.False(back.MapEnabled);
    }

    [Fact]
    public void LegacyDto_WithoutMapEnabled_FollowsIsMapable()
    {
        // Saves written before MapEnabled was persisted stored only IsMapable;
        // restore keeps the old single-setting meaning for those saves.
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("legacy");
        var dto = new Atheriz.Core.Persistence.Dto.GameObjectDto
        {
            Id = o.Id,
            Name = "legacy",
            IsMapable = true,
        };
        GameObject.ApplyDtoFields(o, dto, null);
        Assert.True(o.MapEnabled);

        dto.IsMapable = false;
        GameObject.ApplyDtoFields(o, dto, null);
        Assert.False(o.MapEnabled);
    }
}
