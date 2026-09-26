using System.Reflection;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// Follow-up to audit 10 finding 5: TryGetSeq had the same unchecked
// long→int narrowing as TryCoerceInt. A wrapped seq would echo — and look
// up the map_edit chain for — the wrong sequence number, so out-of-Int32
// values must be rejected, not wrapped. Reflection is used because the
// method is private and has no other public probe (allowed in tests).
public sealed class MapEditSeqNarrowingTests
{
    private static bool TryGetSeq(object? o, out int seq)
    {
        var m = typeof(InputFuncs).GetMethod("TryGetSeq", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        var args = new object?[] { o, 0 };
        var ok = (bool)m.Invoke(null, args)!;
        seq = (int)args[1]!;
        return ok;
    }

    [Fact]
    public void TryGetSeq_LongOutsideInt32_IsRejected()
    {
        Assert.False(TryGetSeq(4294967297L, out _));
        Assert.False(TryGetSeq(-4294967295L, out _));
        Assert.False(TryGetSeq((long)int.MaxValue + 1, out _));
    }

    [Fact]
    public void TryGetSeq_InRangeValues_Accepted()
    {
        Assert.True(TryGetSeq(5, out var s1));
        Assert.Equal(5, s1);
        Assert.True(TryGetSeq(5L, out var s2));
        Assert.Equal(5, s2);
    }
}
