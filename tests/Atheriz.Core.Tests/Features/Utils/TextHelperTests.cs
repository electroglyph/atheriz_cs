using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Features.Utils;

// CopyWordCase parity across FuncParserHelpers and GameUtils.
[Collection("Ported")]
public class TextHelperTests
{
    [Fact]
    public void CopyWordCase_Twins_Agree()
    {
        // Behavior: CopyWordCase exists in both FuncParserHelpers and
        // GameUtils with subtly different case handling; one canonical
        // behavior must win.
        foreach (var (src, dst) in new[] { ("HELLO", "world"), ("Hello", "WORLD"), ("hELLO", "wORLD"), ("abc", "XYZ") })
        {
            Assert.Equal(
                GameUtils.CopyWordCase(src, dst),
                FuncParserHelpers.CopyWordCase(src, dst));
        }
    }
}
