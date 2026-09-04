// Port of atheriz/tests/test_time.py:1 part2
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedTimeTests2
{
    private static GameTime MakeGt(long ticks = 0, AtherizSettings? s = null)
    {
        s ??= new AtherizSettings();
        var gt = new GameTime(s, autoLoad: false);
        gt.Ticks = ticks;
        var fld = typeof(GameTime).GetField("_alarms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((System.Collections.IDictionary)fld!.GetValue(gt)!).Clear();
        return gt;
    }

    [Fact]
    public void FractionGetTimeHalfMinute()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        Assert.Equal(1, MakeGt(2, s).GetTime().Minute);
        Assert.Equal(1, MakeGt(120, s).GetTime().Hour);
        Assert.Equal(2, MakeGt((int)((s.MinutesPerHour/0.5)*s.HoursPerDay), s).GetTime().Day);
        Assert.Equal(1, MakeGt(240, new AtherizSettings{TickMinutes=0.25}).GetTime().Hour);
        Assert.Equal(2, MakeGt(1, new AtherizSettings{TickMinutes=2.0}).GetTime().Minute);
        Assert.Equal(1, MakeGt(30, new AtherizSettings{TickMinutes=2.0}).GetTime().Hour);
        Assert.Equal(1, MakeGt(12, new AtherizSettings{TickMinutes=5.0}).GetTime().Hour);
    }

    [Fact]
    public void GetTimeSecondsFractional()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        Assert.Equal(30, MakeGt(1, s).GetTime().Second);
        Assert.Equal(0, MakeGt(2, s).GetTime().Second);
    }

    [Fact]
    public void FractionConsistency()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        double tph = s.MinutesPerHour / 0.5;
        double tpd = tph * s.HoursPerDay;
        int ticks = (int)(2*tpd + 3*tph + 20);
        var gt = MakeGt(ticks, s);
        var span = gt.GetTimespan(ticks);
        var td = gt.GetTime();
        Assert.Equal(2, span.Days);
        Assert.Equal(3, span.Hours);
        Assert.True(Math.Abs(span.Minutes - 10) < 0.01);
        Assert.Equal(3, td.Day);
        Assert.Equal(3, td.Hour);
        Assert.Equal(10, td.Minute);
    }

    [Fact]
    public void VeryLargeTickCount()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        double tpy = tpd * s.DaysPerYear;
        Assert.Equal(10, MakeGt((int)(10*tpy), s).GetTimespan((int)(10*tpy)).Years);
        Assert.Equal(s.StartYear+10, MakeGt((int)(10*tpy), s).GetTime().Year);
    }

    // ---- alarms ----

    [Fact]
    public void AddAlarmAndRemove()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var obj = GameObject.Create("alarm_test"); ObjectRegistry.AddObject(obj);
        gt.AddAlarm("6","30", obj);
        Assert.True(gt.SnapshotAlarms().ContainsKey(("6","30")));
        gt.RemoveAlarm("6","30", obj);
        Assert.True(!gt.SnapshotAlarms().TryGetValue(("6","30"), out var lst) || lst.Count==0);
        gt.AddAlarm(6,30,obj);
        Assert.True(gt.SnapshotAlarms().ContainsKey(("6","30")));
        gt.RemoveAlarm("6","30", obj.Id);
        Assert.True(!gt.SnapshotAlarms().TryGetValue(("6","30"), out lst) || lst.Count==0);
    }

    [Fact]
    public void AddAlarmRepeatAndData()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var obj = GameObject.Create("alarm2"); ObjectRegistry.AddObject(obj);
        gt.AddAlarm("12","0", obj, repeat:true);
        Assert.True(gt.SnapshotAlarms()[("12","0")][0].Repeat);
        var data = new Dictionary<string, JsonElement> { ["key"] = JsonDocument.Parse("\"val\"").RootElement };
        gt.AddAlarm("1","1", obj, repeat:false, data:data);
        Assert.NotNull(gt.SnapshotAlarms()[("1","1")][0].Data);
        gt.AddAlarm("1","1", (GameObject)null!);
        Assert.Single(gt.SnapshotAlarms()[("1","1")]);
    }

    [Fact]
    public void RemoveAlarmsByCaller()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var obj = GameObject.Create("alarm3"); ObjectRegistry.AddObject(obj);
        gt.AddAlarm("1","0", obj);
        gt.AddAlarm("2","0", obj);
        gt.RemoveAlarmsByCaller(obj);
        foreach (var v in gt.SnapshotAlarms().Values) foreach(var e in v) Assert.NotEqual(obj.Id, e.CallerId);
    }

    [Fact]
    public void NonDictAlarmDataRejected()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = new GameTime(s, autoLoad:false);
        var caller = GameObject.Create("timer");
        Assert.ThrowsAny<Exception>(() => gt.AddAlarm("7","0", caller, true, (object)new object()));
        Assert.Empty(gt.SnapshotAlarms());
    }

    // ---- save/load ----

    [Fact]
    public void SaveAndLoad()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { TickMinutes = 1.0, SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false); gt.Ticks=500;
        var obj = GameObject.Create("alarmee"); ObjectRegistry.AddObject(obj);
        gt.AddAlarm("6","0", obj);
        gt.Save(new AtherizDbContext(env.TempPath));
        var gt2 = new GameTime(s, autoLoad:false);
        gt2.Load(new AtherizDbContext(env.TempPath));
        Assert.Equal(500, gt2.Ticks);
        Assert.True(gt2.SnapshotAlarms().ContainsKey(("6","0")));
    }

    [Fact]
    public void LoadEmptyDatabaseDefaults()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false);
        gt.Load(new AtherizDbContext(env.TempPath));
        Assert.Equal(0, gt.Ticks);
        Assert.Empty(gt.SnapshotAlarms());
    }

    [Fact]
    public void SaveWritesRow()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false); gt.Ticks=100;
        var obj = GameObject.Create("x"); ObjectRegistry.AddObject(obj);
        gt.AddAlarm("8","0", obj);
        gt.Save(new AtherizDbContext(env.TempPath));
        using var db = new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        var row = db.GameTime.First(r=>r.Id==0);
        Assert.NotNull(row);
        var doc = JsonDocument.Parse(row.Data);
        Assert.True(doc.RootElement.TryGetProperty("ticks", out _ ) || doc.RootElement.ToString().Contains("100"));
    }

    [Fact]
    public void LegacyMigration()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        // Remove seeded row so legacy file path is taken (mirrors python where row is None)
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); var r=db.GameTime.FirstOrDefault(x=>x.Id==0); if(r!=null){db.GameTime.Remove(r); db.SaveChanges();} }
        var path = Path.Combine(env.TempPath, "time");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { ticks=42, alarms=new Dictionary<string, object>{["('9', '0')"]= new object[]{ new object[]{3,false,null!} } } }));
        var gt = new GameTime(s, autoLoad:false);
        gt.Load(new AtherizDbContext(env.TempPath));
        Assert.Equal(42, gt.Ticks);
        Assert.True(gt.SnapshotAlarms().ContainsKey(("9","0")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CorruptLegacyResets()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        File.WriteAllText(Path.Combine(env.TempPath,"time"), "not json {{{");
        var gt = new GameTime(s, autoLoad:false);
        gt.Load(new AtherizDbContext(env.TempPath));
        Assert.Equal(0, gt.Ticks);
    }

    // ---- on_tick events ----

    [Fact]
    public void SolarEventsViaOnTick()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { TickMinutes = 1.0, SavePath = env.TempPath };
        var gt = MakeGt(s.SunriseHour*60 -1, s);
        var receiver = new TestReceiver("recv"); receiver.IsPc=true; receiver.IsConnected=true;
        ObjectRegistry.AddObject(receiver);
        gt.OnTick();
        Assert.Contains(s.SunriseMessage, receiver.SolarMsgs);
        receiver.SolarMsgs.Clear();
        var gt2 = MakeGt(s.SunsetHour*60 -1, s);
        // reuse same receiver, need fresh gt with same settings
        // OnTick on gt2 should trigger sunset
        gt2.OnTick();
        // Need receiver still in registry (it is)
        Assert.Contains(s.SunsetMessage, receiver.SolarMsgs);
    }

    private class TestReceiver : GameObject
    {
        public List<string> SolarMsgs = new();
        public List<string> LunarMsgs = new();
        public TestReceiver(string name) { Name=name; }
        public override void AtSolarEvent(string message) => SolarMsgs.Add(message);
        public override void AtLunarEvent(string message) => LunarMsgs.Add(message);
    }

    [Fact]
    public void LunarEventsViaOnTick()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { TickMinutes = 1.0, SavePath = env.TempPath };
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        var gt = MakeGt((int)tpd -1, s);
        var recv = new TestReceiver("lunar"); recv.IsPc=true; recv.IsConnected=true;
        ObjectRegistry.AddObject(recv);
        string before = gt.GetTime().MoonPhase;
        gt.OnTick();
        string after = gt.GetTime().MoonPhase;
        Assert.Equal("new", before);
        Assert.Equal("waxing crescent", after);
        Assert.Contains("waxing crescent", recv.LunarMsgs[0].ToLower());
    }

    [Fact]
    public void NonRepeatWildcardAlarmRemoved()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false);
        var obj = GameObject.Create("alarmee"); ObjectRegistry.AddObject(obj);
        gt.Ticks = 1;
        string minute = gt.GetTime().Minute.ToString();
        gt.Ticks = 0;
        gt.AddAlarm("?", minute, obj, repeat:false);
        gt.OnTick();
        Assert.True(!gt.SnapshotAlarms().TryGetValue(("?", minute), out var lst) || lst.Count==0);
    }

    [Fact]
    public void RestartAfterStopReregistersClock()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath, TimeUpdateSeconds = 1.0 };
        var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        var gt = new GameTime(s, ticker, null, autoLoad:false);
        gt.Start(ticker);
        var slot = ticker.GetSlot(s.TimeUpdateSeconds);
        Assert.NotNull(slot);
        Assert.Contains(slot!.Coros, d => d.Method.Name.Contains("OnTick"));
        gt.Stop(ticker);
        gt.Start(ticker);
        var slot2 = ticker.GetSlot(s.TimeUpdateSeconds);
        Assert.NotNull(slot2);
        Assert.Contains(slot2!.Coros, d => d.Method.Name.Contains("OnTick"));
        ticker.Clear();
    }

    [Fact]
    public void SaveSnapshotHoldsLock()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false);
        var caller = GameObject.Create("timer");
        gt.AddAlarm("7","0", caller, repeat:true);
        Exception? err=null;
        var t = new Thread(() => { try { gt.Save(new AtherizDbContext(env.TempPath)); } catch(Exception ex){ err=ex; } });
        gt.Lock.EnterWriteLock();
        try
        {
            t.Start();
            Thread.Sleep(200);
            Assert.False(t.Join(10), "save completed while lock held");
        }
        finally { gt.Lock.ExitWriteLock(); }
        Assert.True(t.Join(5000));
        Assert.Null(err);
    }

    // ---- missing GetTime fractional tick minute specific ----
    [Fact]
    public void DoubleMinuteTick_MinuteIsTwo()
    {
        var s = new AtherizSettings { TickMinutes = 2.0 };
        var gt = MakeGt(1, s);
        Assert.Equal(2, gt.GetTime().Minute);
    }

    [Fact]
    public void FiveMinuteTickOneHour()
    {
        var s = new AtherizSettings { TickMinutes = 5.0 };
        var gt = MakeGt(12, s);
        var t = gt.GetTime();
        Assert.Equal(1, t.Hour);
        Assert.Equal(0, t.Minute);
    }

    [Fact]
    public void FractionalFullYear()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        double tph = s.MinutesPerHour / 0.5;
        double tpd = tph * s.HoursPerDay;
        double tpy = tpd * s.DaysPerYear;
        var gt = MakeGt((int)tpy, s);
        var t = gt.GetTime();
        Assert.Equal(s.StartYear + 1, t.Year);
        Assert.Equal(1, t.Month);
        Assert.Equal(1, t.Day);
    }

    // ---- edge cases missing ----
    [Fact]
    public void TicksFieldInGetTime()
    {
        var gt = MakeGt(42, new AtherizSettings { TickMinutes = 1.0 });
        var t = gt.GetTime();
        Assert.Equal(42, t.Ticks);
    }

    [Fact]
    public void GetTimespanSmallFractional()
    {
        var s = new AtherizSettings { TickMinutes = 0.1 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(1);
        Assert.True(Math.Abs(r.Minutes - 0.1) < 0.001);
    }

    [Fact]
    public void GetTimeSecondsFieldNonzero_OddEven()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gtOdd = MakeGt(1, s);
        Assert.Equal(30, gtOdd.GetTime().Second);
        var gtEven = MakeGt(2, s);
        Assert.Equal(0, gtEven.GetTime().Second);
        Assert.Equal(1, gtEven.GetTime().Minute);
    }

    [Fact]
    public void MonthNameEnum_Ianuarius()
    {
        var gt = MakeGt(0, new AtherizSettings { TickMinutes = 1.0 });
        var t = gt.GetTime();
        Assert.Contains("Ianuarius", t.Formatted);
    }

    [Fact]
    public void GetTimespanSingleTickVariousRates()
    {
        foreach (var tm in new double[] { 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 })
        {
            var s = new AtherizSettings { TickMinutes = tm };
            var gt = MakeGt(0, s);
            var r = gt.GetTimespan(1);
            Assert.True(Math.Abs(r.Minutes - tm) < 0.001, $"Failed for TICK_MINUTES={tm}");
        }
    }

    [Fact]
    public void GetTimeAndTimespanRoundtripVariousRates()
    {
        foreach (var tm in new double[] { 0.25, 0.5, 1.0, 2.0, 5.0 })
        {
            var s = new AtherizSettings { TickMinutes = tm };
            double tph = s.MinutesPerHour / tm;
            int targetMinutes = 3 * 60 + 20;
            int ticks = (int)(targetMinutes / tm);
            var gt = MakeGt(ticks, s);
            var t = gt.GetTime();
            Assert.Equal(3, t.Hour);
            Assert.Equal(20, t.Minute);
            var span = gt.GetTimespan(ticks);
            Assert.Equal(3, span.Hours);
            Assert.True(Math.Abs(span.Minutes - 20.0) < 0.001);
        }
    }

    // ---- alarms missing ----
    [Fact]
    public void AddAlarmNoneCaller()
    {
        var gt = MakeGt(0, new AtherizSettings { TickMinutes = 1.0 });
        gt.AddAlarm("1", "1", (GameObject)null!);
        Assert.Empty(gt.SnapshotAlarms());
    }

    [Fact]
    public void AddMultipleAlarmsSameTime()
    {
        var gt = MakeGt(0, new AtherizSettings { TickMinutes = 1.0 });
        var o1 = GameObject.Create("o1"); ObjectRegistry.AddObject(o1);
        var o2 = GameObject.Create("o2"); ObjectRegistry.AddObject(o2);
        gt.AddAlarm("6","0", o1);
        gt.AddAlarm("6","0", o2);
        Assert.Equal(2, gt.SnapshotAlarms()[("6","0")].Count);
    }

    [Fact]
    public void RemoveAlarmNoneCallerAndNonexistent()
    {
        var gt = MakeGt(0, new AtherizSettings { TickMinutes = 1.0 });
        var ex = Record.Exception(() => gt.RemoveAlarm("1","1", (GameObject)null!));
        Assert.Null(ex);
        // nonexistent should not crash
        var ex2 = Record.Exception(() => gt.RemoveAlarm("99","99", 12345));
        Assert.Null(ex2);
    }

    [Fact]
    public void AddAlarmIntArgsConverted()
    {
        var gt = MakeGt(0, new AtherizSettings { TickMinutes = 1.0 });
        var obj = GameObject.Create("intconv"); ObjectRegistry.AddObject(obj);
        gt.AddAlarm(6, 30, obj);
        Assert.True(gt.SnapshotAlarms().ContainsKey(("6","30")));
    }

    // ---- save/load missing ----
    [Fact]
    public void LoadEmptyFileResetsToDefaults()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        // ensure no gametime row to force legacy path? We'll write empty file and ensure row absent
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); var r=db.GameTime.FirstOrDefault(x=>x.Id==0); if(r!=null){db.GameTime.Remove(r); db.SaveChanges();} }
        File.WriteAllText(Path.Combine(env.TempPath, "time"), "");
        var gt = new GameTime(s, autoLoad:false);
        gt.Load(new AtherizDbContext(env.TempPath));
        Assert.Equal(0, gt.Ticks);
        Assert.Empty(gt.SnapshotAlarms());
    }

    [Fact]
    public void LoadMissingTicksKeyDefaultsToZero()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); var r=db.GameTime.FirstOrDefault(x=>x.Id==0); if(r!=null){db.GameTime.Remove(r); db.SaveChanges();} }
        File.WriteAllText(Path.Combine(env.TempPath, "time"), System.Text.Json.JsonSerializer.Serialize(new { alarms = new Dictionary<string, object>() }));
        var gt = new GameTime(s, autoLoad:false);
        gt.Load(new AtherizDbContext(env.TempPath));
        Assert.Equal(0, gt.Ticks);
    }

    // ---- GameTimeSaveRobustness ----
    [Fact]
    public void NonDictAlarmDataRejected_ExactTypeError()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = new GameTime(s, autoLoad:false);
        var caller = GameObject.Create("timer");
        var ex = Assert.ThrowsAny<Exception>(() => gt.AddAlarm("7","0", caller, true, (object)new object()));
        Assert.IsType<ArgumentException>(ex); // map to TypeError exact per python:1016
        Assert.Empty(gt.SnapshotAlarms());
    }

    [Fact]
    public void DictAlarmDataSavesWithoutError()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false);
        var caller = GameObject.Create("timer2"); ObjectRegistry.AddObject(caller);
        var data = new Dictionary<string, JsonElement> { ["key"] = JsonDocument.Parse("\"val\"").RootElement };
        gt.AddAlarm("7","0", caller, repeat:true, data:data);
        var ex = Record.Exception(() => gt.Save(new AtherizDbContext(env.TempPath)));
        Assert.Null(ex);
        using var db = new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        Assert.NotNull(db.GameTime.FirstOrDefault(x=>x.Id==0));
    }

    [Fact]
    public void SaveMustSerializeWithAlarmMutations_HoldsLock()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad:false);
        var caller = GameObject.Create("timer3"); ObjectRegistry.AddObject(caller);
        gt.AddAlarm("7","0", caller, repeat:true);
        var errors = new List<Exception>();
        void DoSave() { try { gt.Save(new AtherizDbContext(env.TempPath)); } catch(Exception ex){ lock(errors) errors.Add(ex); } }
        gt.Lock.EnterWriteLock();
        Thread? t = null;
        bool finishedWhileLocked = false;
        try
        {
            t = new Thread(new ThreadStart(DoSave));
            t.Start();
            Thread.Sleep(200);
            finishedWhileLocked = !t.IsAlive || t.Join(10);
            Assert.False(finishedWhileLocked, "save() completed while another thread held the game-time lock");
        }
        finally { gt.Lock.ExitWriteLock(); }
        Assert.True(t!.Join(5000));
        Assert.Empty(errors);
    }

    // ---- improved RestartAfterStop with settings var ----
    [Fact]
    public void RestartAfterStopReregistersClock_UsesSettingsVar()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath }; // use default TIME_UPDATE_SECONDS
        double interval = s.TimeUpdateSeconds;
        var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        var gt = new GameTime(s, ticker, null, autoLoad:false);
        gt.Start(ticker);
        var slot = ticker.GetSlot(interval);
        Assert.NotNull(slot);
        Assert.Contains(slot!.Coros, d => d.Method.Name.Contains("OnTick"));
        gt.Stop(ticker);
        gt.Start(ticker);
        var slot2 = ticker.GetSlot(interval);
        Assert.NotNull(slot2);
        Assert.Contains(slot2!.Coros, d => d.Method.Name.Contains("OnTick"));
        ticker.Clear();
    }
}
