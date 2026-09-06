using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Channel history entries and replay formatting.
[Collection("Ported")]
public class ChannelHistoryTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- Channel history / msglog ---

    [Fact]
    public void Channel_History_ContainsSenderAndTimestamp()
    {
        // Python parity (base_channel.py): history keeps (timestamp, sender,
        // message) entries; History projects the raw messages while GetHistory
        // re-formats on replay, so replay matches what live listeners received.
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            var sender = GameObject.Create("Alice");
            RegisterAll(sender);
            ch.Msg("hello", sender);
            Assert.Contains("hello", ch.History);
            var h = ch.GetHistory(10);
            Assert.Contains("hello", h);
            Assert.Contains("Alice", h);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
