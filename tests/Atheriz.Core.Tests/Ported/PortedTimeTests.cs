// Port of atheriz/tests/test_time.py:1 part1
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedTimeTests
{
    private static GameTime MakeGt(long ticks = 0, AtherizSettings? s = null)
    {
        s ??= new AtherizSettings();
        var gt = new GameTime(s, autoLoad: false);
        gt.Ticks = ticks;
        // clear alarms
        var fld = typeof(GameTime).GetField("_alarms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = (System.Collections.IDictionary)fld!.GetValue(gt)!;
        dict.Clear();
        return gt;
    }

    // ---- get_timespan defaults ----

    [Fact]
    public void GetTimespanZero()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(0);
        Assert.Equal("now", r.Desc);
        Assert.Equal(0, r.Years); Assert.Equal(0, r.Months); Assert.Equal(0, r.Weeks);
        Assert.Equal(0, r.Days); Assert.Equal(0, r.Hours); Assert.Equal(0, r.Minutes);
    }

    [Fact]
    public void GetTimespanOneTickIsOneMinute()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(1);
        Assert.Equal(1.0, r.Minutes);
        Assert.Contains("minute", r.Desc);
    }

    [Fact]
    public void GetTimespanOneHour()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        int ticks = (int)(s.MinutesPerHour / s.TickMinutes);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Hours);
        Assert.Equal(0, r.Minutes);
        Assert.Contains("1 hour", r.Desc);
    }

    [Fact]
    public void GetTimespanOneDay()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        int ticks = (int)(tph * s.HoursPerDay);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Days);
        Assert.Equal(0, r.Hours);
        Assert.Contains("1 day", r.Desc);
    }

    [Fact]
    public void GetTimespanOneWeek()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        int ticks = (int)(tph * s.HoursPerDay * s.DaysPerWeek);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Weeks);
        Assert.Equal(0, r.Days);
        Assert.Contains("1 week", r.Desc);
    }

    [Fact]
    public void GetTimespanOneMonth()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        int ticks = (int)(tph * s.HoursPerDay * s.DaysPerMonth);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Months);
        Assert.Equal(0, r.Weeks);
        Assert.Contains("1 month", r.Desc);
    }

    [Fact]
    public void GetTimespanOneYear()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        int ticks = (int)(tpd * s.DaysPerMonth * s.MonthsPerYear);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Years);
        Assert.Equal(0, r.Months);
        Assert.Contains("1 year", r.Desc);
    }

    [Fact]
    public void GetTimespanNegative()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(-60);
        Assert.Contains("in the future", r.Desc);
        Assert.Equal(1, r.Hours);
    }

    [Fact]
    public void GetTimespanPlural()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r = gt.GetTimespan((int)(tph*2));
        Assert.Contains("2 hours", r.Desc);
    }
    [Fact]
    public void GetTimespanSingular()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r2 = gt.GetTimespan((int)(tph));
        Assert.Contains("1 hour", r2.Desc);
    }
    [Fact]
    public void GetTimespanDescContainsAnd()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        int ticks = (int)(tpd + tph + 5);
        var r3 = gt.GetTimespan(ticks);
        Assert.Contains(" and ", r3.Desc);
        Assert.Contains("ago", r3.Desc);
    }

    // ---- fractional ----

    [Fact]
    public void HalfMinuteTicksOneMinute()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(2);
        Assert.True(Math.Abs(r.Minutes - 1.0) < 0.001);
    }

    [Fact]
    public void HalfMinuteTicksOneHour()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r = gt.GetTimespan((int)tph);
        Assert.Equal(1, r.Hours);
    }

    [Fact]
    public void QuarterMinuteOneHour()
    {
        var s = new AtherizSettings { TickMinutes = 0.25 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r = gt.GetTimespan((int)tph);
        Assert.Equal(1, r.Hours);
    }

    [Fact]
    public void DoubleMinuteOneHour()
    {
        var s = new AtherizSettings { TickMinutes = 2.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        var r = gt.GetTimespan((int)tph);
        Assert.Equal(1, r.Hours);
    }

    // ---- get_time defaults ----

    [Fact]
    public void GetTimeTickZero()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var t = gt.GetTime();
        Assert.Equal(s.StartYear, t.Year);
        Assert.Equal(1, t.Month);
        Assert.Equal(1, t.Day);
        Assert.Equal(0, t.Hour);
        Assert.Equal(0, t.Minute);
        Assert.Equal(0, t.Second);
    }

    [Fact]
    public void GetTimeOneTick()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(1, s);
        Assert.Equal(1, gt.GetTime().Minute);
    }

    [Fact]
    public void GetTime60TicksOneHour()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(60, s);
        var t = gt.GetTime();
        Assert.Equal(1, t.Hour);
        Assert.Equal(0, t.Minute);
    }

    [Fact]
    public void GetTimeFullDay() {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        var gt = MakeGt(tpd, s);
        var t = gt.GetTime();
        Assert.Equal(2, t.Day);
        Assert.Equal(0, t.Hour);
    }

    [Fact]
    public void GetTimeFullMonth() {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        int tpmo = tpd * s.DaysPerMonth;
        var gt = MakeGt(tpmo, s);
        Assert.Equal(2, gt.GetTime().Month);
    }

    [Fact]
    public void GetTimeFullYear() {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpy = s.MinutesPerHour * s.HoursPerDay * s.DaysPerYear;
        var gt = MakeGt(tpy, s);
        Assert.Equal(s.StartYear+1, gt.GetTime().Year);
    }

    [Fact]
    public void GetTimeFormatted()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        var t = gt.GetTime();
        Assert.Contains(s.StartYear.ToString(), t.FormattedShort);
        Assert.Contains("Moon phase", t.Formatted);
    }
    [Fact] public void GetTimeSeasonWinter()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        var gt = MakeGt(0, s);
        Assert.Equal("winter", gt.GetTime().Season);
    }
    [Fact] public void GetTimeSeasonSpring()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        int ticks = tpd * s.DaysPerMonth * 2;
        var gt2 = MakeGt(ticks, s);
        Assert.Equal("spring", gt2.GetTime().Season);
        Assert.Equal(3, gt2.GetTime().Month);
    }
    [Fact] public void GetTimeSeasonSummer()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        Assert.Equal("summer", MakeGt(tpd * s.DaysPerMonth *5, s).GetTime().Season);
    }
    [Fact] public void GetTimeSeasonAutumn()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        Assert.Equal("autumn", MakeGt(tpd * s.DaysPerMonth *8, s).GetTime().Season);
    }
    [Fact] public void GetTimeSeasonWinterDecember()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        Assert.Equal("winter", MakeGt(tpd * s.DaysPerMonth *11, s).GetTime().Season);
    }

    [Fact]
    public void MoonPhases()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        Assert.Equal("new", MakeGt(0, s).GetTime().MoonPhase);
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        Assert.Equal("full", MakeGt(tpd*15, s).GetTime().MoonPhase);
        Assert.Equal("new", MakeGt(tpd*s.LunarCycleDays, s).GetTime().MoonPhase);
    }

    [Fact]
    public void OrdinalSuffixes()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int tpd = s.MinutesPerHour * s.HoursPerDay;
        Assert.Contains("1st", MakeGt(0, s).GetTime().Formatted);
        Assert.Contains("2nd", MakeGt(tpd, s).GetTime().Formatted);
        Assert.Contains("3rd", MakeGt(tpd*2, s).GetTime().Formatted);
        Assert.Contains("4th", MakeGt(tpd*3, s).GetTime().Formatted);
        Assert.Contains("11th", MakeGt(tpd*10, s).GetTime().Formatted);
        Assert.Contains("21st", MakeGt(tpd*20, s).GetTime().Formatted);
    }

    // ---- sun_up ----

    [Fact]
    public void SunUpAtSunrise()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int ticks = s.SunriseHour * s.MinutesPerHour;
        Assert.True(MakeGt(ticks, s).SunUp());
        Assert.False(MakeGt(0, s).SunUp());
        Assert.True(MakeGt(12*60, s).SunUp());
        Assert.False(MakeGt(s.SunsetHour*60, s).SunUp());
        Assert.True(MakeGt(0, s).SunUpAlt(s.SunriseHour));
        Assert.False(MakeGt(0, s).SunUpAlt(s.SunriseHour-1));
    }

    [Fact]
    public void SunUpFractional()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        double tph = s.MinutesPerHour / 0.5;
        int ticks = (int)(s.SunriseHour * tph);
        Assert.True(MakeGt(ticks, s).SunUp());
        Assert.False(MakeGt(0, s).SunUp());
    }

    // ---- missing from TestGetTimespanDefaults ----
    [Fact]
    public void GetTimespanMixedLargeValue()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        double tpw = tpd * s.DaysPerWeek;
        double tpmo = tpd * s.DaysPerMonth;
        double tpy = tpmo * s.MonthsPerYear;
        int ticks = (int)(1 * tpy + 1 * tpmo + 2 * tpw + 3 * tpd + 4 * tph + 5);
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Years);
        Assert.Equal(1, r.Months);
        Assert.Equal(2, r.Weeks);
        Assert.Equal(3, r.Days);
        Assert.Equal(4, r.Hours);
        Assert.True(Math.Abs(r.Minutes - 5 * s.TickMinutes) < 0.001);
    }

    // ---- missing TestGetTimespanFractionalTickMinutes ----
    [Fact]
    public void HalfMinuteTicksZero()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(0);
        Assert.Equal("now", r.Desc);
    }

    [Fact]
    public void HalfMinuteTicksOneDay()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes;
        double tpd = tph * s.HoursPerDay;
        var r = gt.GetTimespan((int)tpd);
        Assert.Equal(1, r.Days);
        Assert.Equal(0, r.Hours);
    }

    [Fact]
    public void HalfMinuteTicksPartial()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(3);
        Assert.True(Math.Abs(r.Minutes - 1.5) < 0.001);
    }

    [Fact]
    public void QuarterMinuteTicksOneYear()
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

    [Fact]
    public void DoubleMinuteTicksMixed()
    {
        var s = new AtherizSettings { TickMinutes = 2.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes; // 30
        int ticks = (int)(2 * tph + 2);
        var r = gt.GetTimespan(ticks);
        Assert.Equal(2, r.Hours);
        Assert.True(Math.Abs(r.Minutes - 4.0) < 0.001);
    }

    [Fact]
    public void FractionalNegative()
    {
        var s = new AtherizSettings { TickMinutes = 0.5 };
        var gt = MakeGt(0, s);
        var r = gt.GetTimespan(-120);
        Assert.Contains("in the future", r.Desc);
        Assert.Equal(1, r.Hours);
    }

    [Fact]
    public void FiveMinuteTicks()
    {
        var s = new AtherizSettings { TickMinutes = 5.0 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes; // 12
        var r = gt.GetTimespan((int)tph);
        Assert.Equal(1, r.Hours);
        var r2 = gt.GetTimespan(1);
        Assert.True(Math.Abs(r2.Minutes - 5.0) < 0.001);
    }

    [Fact]
    public void TenthMinuteTicksConsistency()
    {
        var s = new AtherizSettings { TickMinutes = 0.1 };
        var gt = MakeGt(0, s);
        double tph = s.MinutesPerHour / s.TickMinutes; // 600
        double tpd = tph * s.HoursPerDay; // 14400
        int ticks = (int)(tpd + 2 * tph + 300); // 300 ticks *0.1=30 min
        var r = gt.GetTimespan(ticks);
        Assert.Equal(1, r.Days);
        Assert.Equal(2, r.Hours);
        Assert.True(Math.Abs(r.Minutes - 30.0) < 0.01);
    }

    // ---- additional missing sun / alarm edge not covered but ensure verbatim ----
    [Fact]
    public void SunDownBeforeSunrise()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int ticks = (int)((s.SunriseHour - 1) * s.MinutesPerHour);
        Assert.False(MakeGt(ticks, s).SunUp());
    }

    [Fact]
    public void SunUpBeforeSunset()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int ticks = (int)((s.SunsetHour - 1) * s.MinutesPerHour);
        Assert.True(MakeGt(ticks, s).SunUp());
    }

    [Fact]
    public void SunDownAtSunset()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int ticks = s.SunsetHour * s.MinutesPerHour;
        Assert.False(MakeGt(ticks, s).SunUp());
    }

    [Fact]
    public void SunUpAltDirectly()
    {
        var gt = MakeGt(0, new AtherizSettings { TickMinutes = 1.0 });
        var s = new AtherizSettings();
        Assert.True(gt.SunUpAlt(s.SunriseHour));
        Assert.False(gt.SunUpAlt(s.SunriseHour - 1));
        Assert.True(gt.SunUpAlt(s.SunsetHour - 1));
        Assert.False(gt.SunUpAlt(s.SunsetHour));
        Assert.False(gt.SunUpAlt(0));
        Assert.True(gt.SunUpAlt(12));
    }

    [Fact]
    public void SunDownAtMidnight()
    {
        Assert.False(MakeGt(0, new AtherizSettings { TickMinutes = 1.0 }).SunUp());
    }

    [Fact]
    public void SunUpAtNoonDirect()
    {
        var s = new AtherizSettings { TickMinutes = 1.0 };
        int ticks = 12 * s.MinutesPerHour;
        Assert.True(MakeGt(ticks, s).SunUp());
    }
}
