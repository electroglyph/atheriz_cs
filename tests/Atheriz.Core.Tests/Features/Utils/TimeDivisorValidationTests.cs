using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Utils;

// Audit 10 finding 2: DaysPerWeek, LunarCycleDays and TickMinutes are all
// used as divisors in GameTime, so the validator must reject zero and the
// clock must clamp defensively when constructed directly.
[Collection("Ported")]
public sealed class TimeDivisorValidationTests
{
    [Fact]
    public void SettingsValidator_ZeroDaysPerWeek_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = new AtherizSettings { DaysPerWeek = 0 };
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("DaysPerWeek", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsValidator_ZeroLunarCycleDays_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = new AtherizSettings { LunarCycleDays = 0 };
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("LunarCycleDays", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsValidator_ZeroTickMinutes_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = new AtherizSettings { TickMinutes = 0 };
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("TickMinutes", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsValidator_NaNTickMinutes_Fails()
    {
        // NaN fails every comparison, so a `<= 0` spelling would accept it
        // while GetTimespan's decimal parse threw on it every call.
        var v = new AtherizSettingsValidator();
        var s = new AtherizSettings { TickMinutes = double.NaN };
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("TickMinutes", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameTime_NaNTickMinutes_DoesNotThrow()
    {
        // decimal.Parse("NaN") throws FormatException, so the guard must run
        // before parsing, not after.
        var s = new AtherizSettings { TickMinutes = double.NaN };
        var gt = new GameTime(s, autoLoad: false) { Ticks = 12345 };
        var info = gt.GetTime();
        Assert.NotNull(info.Formatted);
        var span = gt.GetTimespan(12345);
        Assert.NotNull(span);
    }

    [Fact]
    public void GameTime_ZeroDivisors_DoNotThrow()
    {
        var s = new AtherizSettings { DaysPerWeek = 0, LunarCycleDays = 0, TickMinutes = 0 };
        var gt = new GameTime(s, autoLoad: false) { Ticks = 12345 };
        var info = gt.GetTime();
        Assert.NotNull(info.Formatted);
        var span = gt.GetTimespan(12345);
        Assert.NotNull(span);
    }
}
