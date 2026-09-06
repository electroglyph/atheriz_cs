using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Features.Concurrency;

// AsyncTicker TimeSlot duplicate accessors stay consistent.
[Collection("Ported")]
public class AsyncTickerTests
{
    [Fact]
    public void Ticker_DupAccessors_Agree()
    {
        // Behavior pin: TimeSlot.running/Coros duplicates must stay
        // consistent until the aliases are removed.
        var pool = new AsyncThreadPool(maxThreads: 1);
        try
        {
            var slot = new AsyncTicker.TimeSlot(TimeSpan.FromSeconds(1), pool);
            Assert.Equal(slot.Running, slot.running);
            Assert.Equal(slot.Coros.Count, slot.CorosSnapshot.Count);
        }
        finally { pool.Stop(wait: false); }
    }
}
