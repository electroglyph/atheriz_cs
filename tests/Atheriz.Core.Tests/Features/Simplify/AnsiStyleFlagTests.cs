using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared ANSI style-flag applier: byte-identical snapshots for all three
// Wrap* helpers, including flag order and the trailing reset.
public class AnsiStyleFlagTests
{
    [Fact]
    public void WrapXterm256_FlagOrder_BoldThenInverseThenStrikethruThenReset()
    {
        Assert.Equal("\x1b[9m\x1b[1mx\x1b[0m",
            GameUtils.WrapXterm256("x", bold: true, strikethru: true));
    }

    [Fact]
    public void WrapXterm256_AllFlags_ColorFirstThenFlagsThenReset()
    {
        Assert.Equal("\x1b[9m\x1b[7m\x1b[4m\x1b[3m\x1b[1m\x1b[38;5;1m\x1b[48;5;2mx\x1b[0m",
            GameUtils.WrapXterm256("x", fg: 1, bg: 2,
                bold: true, italic: true, underline: true, inverse: true, strikethru: true));
    }

    [Fact]
    public void WrapRgb_Defaults_Fg204BgBlackThenReset()
    {
        Assert.Equal("\x1b[38;2;204;204;204m\x1b[48;2;0;0;0mx\x1b[0m",
            GameUtils.WrapRgb("x"));
    }

    [Fact]
    public void WrapRgb_Flags_ShareApplierOrder()
    {
        Assert.Equal("\x1b[4m\x1b[1m\x1b[38;2;204;204;204m\x1b[48;2;0;0;0mx\x1b[0m",
            GameUtils.WrapRgb("x", bold: true, underline: true));
    }

    [Fact]
    public void WrapTruecolor_Defaults_WhiteOnBlackThenReset()
    {
        Assert.Equal("\x1b[38;2;255;255;255m\x1b[48;2;0;0;0mx\x1b[0m",
            GameUtils.WrapTruecolor("x"));
    }
}
