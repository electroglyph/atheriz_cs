using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.ServerLog;

// Sequence ids and live-post events backing the server-log page.
[Collection("Ported")]
public class ChannelSeqTests
{
    [Fact]
    public void Seq_IncreasesPerMessage_AndHistoryLinesMatchHistoryText()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Msg("first", null);
            ch.Msg("second", null);
            ch.Msg("third", null);
            var lines = ch.GetHistoryLines(10);
            Assert.Equal([1, 2, 3], lines.Select(l => l.Seq).ToArray());
            var text = ch.GetHistory(10).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(text, lines.Select(l => l.Line).ToArray());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void LinesAfter_ReturnsOnlyNewer()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Msg("old", null);
            ch.Msg("marker", null);
            long mark = ch.GetHistoryLines(10)[1].Seq;
            ch.Msg("new", null);
            var after = ch.LinesAfter(mark);
            Assert.Single(after);
            Assert.Contains("new", after[0].Line);
            Assert.Empty(ch.LinesAfter(after[0].Seq));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MessagePosted_FiresWithFormattedLine_AndThrowingSubscriberDoesNotBreakDelivery()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            var seen = new List<(long, long, string)>();
            ch.MessagePosted += (seq, timestamp, text) => seen.Add((seq, timestamp, text));
            ch.MessagePosted += (seq, timestamp, text) => throw new InvalidOperationException("boom");
            var ex = Record.Exception(() => ch.Msg("hello", null));
            Assert.Null(ex);
            Assert.Single(seen);
            Assert.Equal(1, seen[0].Item1);
            Assert.True(seen[0].Item2 > 0);
            Assert.Contains("hello", seen[0].Item3);
            Assert.Contains("hello", ch.GetHistory(10));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RestoreHistory_SeedsSeq_PastRestoredMax()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.RestoreHistory([
                new ChannelHistoryEntry(1, "a", "x", 7),
                new ChannelHistoryEntry(2, "b", "y", 9),
            ]);
            ch.Msg("next", null);
            var lines = ch.GetHistoryLines(10);
            Assert.Equal(10, lines[^1].Seq);
            Assert.Contains("next", lines[^1].Line);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void OldTripleHistory_LoadsWithSeqZero()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.RestoreHistory([new ChannelHistoryEntry(1, "a", "legacy")]);
            var lines = ch.GetHistoryLines(10);
            Assert.Single(lines);
            Assert.Equal(0, lines[0].Seq);
            Assert.Contains("legacy", lines[0].Line);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Seq_SurvivesSaveRoundtrip_AndContinues()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("seqlog1");
        channel.Send("one", null);
        channel.Send("two", null);
        int channelId = channel.Id;
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var reloaded = ObjectRegistry.Get(channelId).FirstOrDefault() as Channel;
        Assert.NotNull(reloaded);
        var before = reloaded!.GetHistoryLines(10);
        Assert.Equal([1, 2], before.Select(l => l.Seq).ToArray());
        reloaded.Send("three", null);
        var after = reloaded.GetHistoryLines(10);
        Assert.Equal([1, 2, 3], after.Select(l => l.Seq).ToArray());
        Assert.Contains("three", after[^1].Line);
    }
}
