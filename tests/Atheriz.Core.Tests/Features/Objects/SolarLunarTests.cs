using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Solar/lunar events deliver their message (base_obj.py:486-506).
[Collection("Ported")]
public class SolarLunarTests
{
    [Fact]
    public void SolarAndLunar_DeliverMessage()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("watcher", isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            o.ClearMessages();
            o.AtSolarEvent("The sun rises.");
            o.AtLunarEvent("A full moon rises.");
            Assert.Contains(o.PeekMessages(), m => m.Contains("The sun rises."));
            Assert.Contains(o.PeekMessages(), m => m.Contains("full moon"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
