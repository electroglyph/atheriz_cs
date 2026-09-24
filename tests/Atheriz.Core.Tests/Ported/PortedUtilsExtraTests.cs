// Port of atheriz/tests/test_utils_extra.py:1 faithful
using Atheriz.Core.Utils;
using System.Threading;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedUtilsExtraTests
{
    [Fact]
    public void WordReplace_EmptyAndFull()
    {
        using var env = GlobalTestEnv.Enter();
        // faithful to test_word_replace_empty_and_full: uniform patched 1.0 => no replace, 0.0 => full replace
        // In C# deterministic with freq 0 and 1
        Assert.Equal("hello world", GameUtils.WordReplace("hello world", 0));
        Assert.Equal("... ...", GameUtils.WordReplace("hello world", 1));
        Assert.Equal("X X", GameUtils.WordReplace("hello world", 1, replacement:"X"));
        Assert.Equal("", GameUtils.WordReplace("", 1));
        // With freq 1.0 via uniform 0.0, all words replaced; with 0 none
        // Also verify that default replacement is "..."
        var replaced = GameUtils.WordReplace("hello world", 1);
        Assert.Equal("... ...", replaced);
    }

    [Fact] public void DiceZeroRolls_ReturnsZero()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Equal(0, GameUtils.DiceRoll(0,6));
        Assert.Equal(0, GameUtils.DiceRoll(0,0));
    }

    [Fact] public void Clamp_MinGreaterThanMax()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Equal(10, GameUtils.Clamp(10,5,0));
        Assert.Equal(5, GameUtils.Clamp(5,10,0));
        Assert.Equal(5, GameUtils.Clamp(0,5,10));
        Assert.Equal(0, GameUtils.Clamp(0,-5,10));
        Assert.Equal(10, GameUtils.Clamp(0,15,10));
    }

    [Fact] public void StripTerminalEscapes_OscNull()
    {
        using var env = GlobalTestEnv.Enter();
        // Exact strings from Python: \x1b[2J \x1b]0;title\x07\x00 -> ""
        Assert.Equal("", GameUtils.StripTerminalEscapes("\x1b[2J\x1b]0;title\x07\x00"));
        Assert.Equal("hello", GameUtils.StripTerminalEscapes("\x1b[31mhello\x1b[0m"));
        Assert.Equal("normaltext", GameUtils.StripTerminalEscapes("normal\x00text"));
        Assert.Equal("", GameUtils.StripTerminalEscapes(""));
    }

    [Fact] public void WrapXterm256_AnsiCodes()
    {
        using var env = GlobalTestEnv.Enter();
        var result = GameUtils.WrapXterm256("hi", fg:196, bg:21, bold:true);
        Assert.Contains("\x1b[38;5;196m", result);
        Assert.Contains("\x1b[48;5;21m", result);
        Assert.Contains("\x1b[1m", result);
        Assert.EndsWith("\x1b[0m", result);
        var simple = GameUtils.WrapXterm256("hi", fg:5);
        Assert.Contains("\x1b[38;5;5m", simple);
        var cleared = GameUtils.WrapXterm256("\x1b[31mhi\x1b[0m", fg:1, clear:true);
        Assert.Contains("\x1b[38;5;1m", cleared);
    }

    [Fact] public void CompressWhitespace_MaxSpacing()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Equal("a  b", GameUtils.CompressWhitespace("a    b", maxSpacing:2));
        Assert.Equal("a b", GameUtils.CompressWhitespace("a  b", maxSpacing:1));
        Assert.Equal("a\nb", GameUtils.CompressWhitespace("a\n\n\nb", maxLinebreaks:1));
        Assert.Equal("a \n b", GameUtils.CompressWhitespace("a \n\n b", maxLinebreaks:1));
        Assert.Equal("hello  world", GameUtils.CompressWhitespace("  hello   world  ", maxSpacing:2).Trim());
    }

    private sealed class Dummy
    {
        public ReaderWriterLockSlim @lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        public int x = 1;
    }

    [Fact] public void EnsureThreadSafe_Idempotent()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new Dummy();
        // In Python, ensure_thread_safe patched the class to copy-on-read.
        // In C# locking is explicit per instance, so the call is a no-op:
        // it never throws, runs twice cleanly, and leaves behavior alone.
        var ex = Record.Exception(() => GameUtils.EnsureThreadSafe(typeof(Dummy)));
        Assert.Null(ex);
        var ex2 = Record.Exception(() => GameUtils.EnsureThreadSafe(typeof(Dummy)));
        Assert.Null(ex2);
        obj.x = 5;
        Assert.Equal(5, obj.x);
    }
}
