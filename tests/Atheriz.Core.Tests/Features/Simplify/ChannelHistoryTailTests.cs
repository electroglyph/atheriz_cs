using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Channel formatting: the shared prefix shape and TakeLast tail selection
// (same tail as Skip(count-n), no off-by-one on the history limit).
[Collection("Ported")]
public class ChannelHistoryTailTests
{
    [Fact]
    public void FormatMessage_SharesPrefixShape()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Name = "chan";
            var named = ch.FormatMessage(0, "Bob", "hi");
            Assert.StartsWith("(chan) [", named);
            Assert.Contains("Bob: hi", named);
            var anon = ch.FormatMessage(0, "", "hi");
            Assert.EndsWith("] hi", anon);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetHistory_ReturnsExactTail()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel(historyLimit: 3);
            ch.Name = "chan";
            var sender = GameObject.Create("Bob");
            ch.Msg("m1", sender);
            ch.Msg("m2", sender);
            ch.Msg("m3", sender);
            ch.Msg("m4", sender);
            ch.Msg("m5", sender);

            Assert.Equal(3, ch.History.Count);
            var tail = ch.GetHistory(2).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, tail.Length);
            Assert.Contains("m4", tail[0]);
            Assert.Contains("m5", tail[1]);
            Assert.Equal("", ch.GetHistory(0));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
