using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The object-dict AddAlarm path serializes with the implicit defaults, so key
// casing and null payloads survive verbatim: routing it through the shared
// camelCase options (which rename keys and drop nulls) would change alarms.
[Collection("Ported")]
public class GameTimeAlarmDataTests
{
    [Fact]
    public void AddAlarm_ObjectDict_PreservesKeyCasingAndNulls()
    {
        using var env = GlobalTestEnv.Enter();
        var gt = new GameTime(new AtherizSettings { SavePath = env.TempPath }, autoLoad: false);
        var caller = GameObject.Create("datacaller");
        ObjectRegistry.AddObject(caller);
        gt.AddAlarm("3", "4", caller, false, (object)new Dictionary<string, object>
        {
            ["PascalKey"] = "v",
            ["count"] = 7,
            ["NullVal"] = null!,
        });
        var snap = gt.SnapshotAlarms();
        var alarm = Assert.Single(snap[("3", "4")]);
        var data = alarm.Data ?? throw new InvalidOperationException("alarm data missing");
        Assert.Equal("v", data["PascalKey"].GetString());
        Assert.Equal(7, data["count"].GetInt32());
        Assert.True(data.ContainsKey("NullVal"));
        Assert.Equal(JsonValueKind.Null, data["NullVal"].ValueKind);
    }
}
