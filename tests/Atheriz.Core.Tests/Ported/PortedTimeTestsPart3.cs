// Port of atheriz/tests/test_time.py remaining 18 — faithful individual facts
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedTimeTestsPart3
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

    // Port of test_time.py:143 test_negative_ticks — -60 ticks => Hours==1 in the future
    [Fact]
    public void NegativeTicks_HoursOne()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(-60);
        Assert.Contains("in the future", r.Desc);
        Assert.Equal(1, r.Hours);
    }

    // Port of test_time.py:166 test_plural_formatting — 2 hours
    [Fact]
    public void PluralFormatting_TwoHours()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r = gt.GetTimespan((int)(tph * 2));
        Assert.Contains("2 hours", r.Desc);
    }

    // Port of test_time.py:172 test_singular_formatting — 1 hour
    [Fact]
    public void SingularFormatting_OneHour()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r = gt.GetTimespan((int)tph);
        Assert.Contains("1 hour", r.Desc);
    }

    // Port of test_time.py:178 test_desc_contains_and_for_multiple_units — " and " and "ago"
    [Fact]
    public void DescContainsAndForMultipleUnits()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        int ticks = (int)(tpd + tph + 5);
        var r = gt.GetTimespan(ticks);
        Assert.Contains(" and ", r.Desc);
        Assert.Contains("ago", r.Desc);
    }

    // Port of test_time.py:509 test_double_minute_tick minute==2
    [Fact]
    public void DoubleMinuteTick_MinuteEqualsTwo()
    {
        var s = new AtherizSettings { TickMinutes = 2.0 };
        var gt = MakeGt(1, s);
        var t = gt.GetTime();
        Assert.Equal(2, t.Minute);
    }

    // Port of test_time.py:284 test_five_minute_ticks — 12 ticks =>1 hour, 1 tick=>5 min
    [Fact]
    public void FiveMinuteTicks_Exact()
    {
        var s = new AtherizSettings { TickMinutes = 5.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes; // 12
        var r = gt.GetTimespan((int)tph);
        Assert.Equal(1, r.Hours);
        var r2 = gt.GetTimespan(1);
        Assert.True(Math.Abs(r2.Minutes - 5.0) < 0.001);
        var gt2 = MakeGt(12, s);
        var t = gt2.GetTime();
        Assert.Equal(1, t.Hour);
        Assert.Equal(0, t.Minute);
    }

    // Port of test_time.py:883 test_ticks_field_in_get_time
    [Fact]
    public void TicksFieldInGetTime()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(42, s);
        var t = gt.GetTime();
        Assert.Equal(42, t.Ticks);
    }

    // Port of test_time.py:888 test_get_timespan_small_fractional — 0.1
    [Fact]
    public void GetTimespanSmallFractional()
    {
        var s = new AtherizSettings { TickMinutes = 0.1 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(1);
        Assert.True(Math.Abs(r.Minutes - 0.1) < 0.001);
    }

    // Port of test_time.py:895 test_get_time_seconds_field_nonzero — 0.5 => second 30
    [Fact]
    public void GetTimeSecondsFieldNonzero()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(1, s);
        var t = gt.GetTime();
        Assert.Equal(30, t.Second);
    }

    // Port of test_time.py:905 test_get_time_seconds_zero_on_even_ticks — 2 ticks => second 0 minute 1
    [Fact]
    public void GetTimeSecondsZeroOnEvenTicks()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(2, s);
        var t = gt.GetTime();
        Assert.Equal(0, t.Second);
        Assert.Equal(1, t.Minute);
    }

    // Port of test_time.py:1044 test_save_must_serialize_with_alarm_mutations — holds lock
    [Fact]
    public void SaveSnapshotFiltersHoldObjectLock()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(s, autoLoad: false);
        var caller = GameObject.Create("timer");
        ObjectRegistry.AddObject(caller);
        gt.AddAlarm("7", "0", caller, repeat: true);
        var errors = new List<Exception>();
        void DoSave() { try { gt.Save(new Atheriz.Core.Persistence.AtherizDbContext(env.TempPath)); } catch (Exception ex) { lock (errors) errors.Add(ex); } }
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

    // Port of test_time.py:463 test_week_of_season — handler flag week_of_season >=1
    [Fact]
    public void WeekOfSeason_HandlerFlag()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var t = gt.GetTime();
        Assert.True(t.WeekOfSeason >= 1);
    }

    // Port of test_time.py:246 test_quarter_minute_ticks_one_year
    [Fact]
    public void QuarterMinuteTicksOneYear_GetTimespan()
    {
        var s = new AtherizSettings { TickMinutes = 0.25 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        double tpmo = tpd * s.DaysPerMonth;
        double tpy = tpmo * s.MonthsPerYear;
        var r = gt.GetTimespan((int)tpy);
        Assert.Equal(1, r.Years);
        Assert.Equal(0, r.Months);
    }

    // Port of test_time.py:266 test_double_minute_ticks_mixed — 2 hours 4 minutes @2.0
    [Fact]
    public void DoubleMinuteTicksMixed_GetTimespan()
    {
        var s = new AtherizSettings { TickMinutes = 2.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        int ticks = (int)(2 * tph + 2);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(2, r.Hours);
        Assert.True(Math.Abs(r.Minutes - 4.0) < 0.001);
    }

    // Port of test_time.py:278 test_fractional_negative — -120 @0.5 => 1 hour future
    [Fact]
    public void FractionalNegative_GetTimespan()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(-120);
        Assert.Contains("in the future", r.Desc);
        Assert.Equal(1, r.Hours);
    }

    // Port of test_time.py:294 test_tenth_minute_ticks_consistency
    [Fact]
    public void TenthMinuteTicksConsistency()
    {
        var s = new AtherizSettings { TickMinutes = 0.1 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        int ticks = (int)(tpd + 2 * tph + 300);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Days);
        Assert.Equal(2, r.Hours);
        Assert.True(Math.Abs(r.Minutes - 30.0) < 0.01);
    }

    // Port of test_time.py:501 test_quarter_minute_tick_one_hour GetTime — 240 ticks => hour 1
    [Fact]
    public void QuarterMinuteTickOneHour_GetTime()
    {
        var s = new AtherizSettings { TickMinutes = 0.25 };
        var gt = MakeGt(240, s);
        var t = gt.GetTime();
        Assert.Equal(1, t.Hour);
        Assert.Equal(0, t.Minute);
    }

    // Port of test_time.py:493 test_half_minute_tick_one_day GetTime — ticks per day => day 2
    [Fact]
    public void HalfMinuteTickOneDay_GetTime()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        double tpd = (s.MinutesPerHour / 0.5) * s.HoursPerDay;
        var gt = MakeGt((int)tpd, s);
        var t = gt.GetTime();
        Assert.Equal(2, t.Day);
        Assert.Equal(0, t.Hour);
    }
}
