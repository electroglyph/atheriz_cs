using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Display names are view-gated with offline/Someone fallbacks
// (base_obj.py:1428-1437), not the raw name.
[Collection("Ported")]
public class DisplayNameTests
{
    [Fact]
    public void OfflinePc_ShowsOfflineSuffix()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var pc = GameObject.Create("hero", isPc: true);
            ObjectRegistry.AddObject(pc);
            pc.IsConnected = false;
            var viewer = GameObject.Create("viewer", isPc: true);
            ObjectRegistry.AddObject(viewer);
            viewer.IsConnected = true;
            Assert.Equal("hero (offline)", pc.GetDisplayName(viewer));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ViewDenied_ShowsSomeoneOrSomething()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var pc = GameObject.Create("sneak", isPc: true);
            ObjectRegistry.AddObject(pc);
            pc.IsConnected = true;
            pc.AddLock("view", _ => false);
            var rock = GameObject.Create("rock", isItem: true);
            ObjectRegistry.AddObject(rock);
            rock.AddLock("view", _ => false);
            var viewer = GameObject.Create("viewer", isPc: true);
            ObjectRegistry.AddObject(viewer);
            viewer.IsConnected = true;
            Assert.Equal("Someone", pc.GetDisplayName(viewer));
            Assert.Equal("Something", rock.GetDisplayName(viewer));
            Assert.Equal("sneak", pc.GetDisplayName(null));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
