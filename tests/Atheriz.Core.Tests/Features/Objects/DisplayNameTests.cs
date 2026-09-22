using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Display names are view-gated with offline/Someone fallbacks
// (base_obj.py:1428-1437), not the raw name.
[Collection("Ported")]
public class DisplayNameTests
{
    [Fact]
    public void OfflinePc_RegularViewerSeesSomeone()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var pc = GameObject.Create("hero", isPc: true);
            ObjectRegistry.AddObject(pc);
            pc.IsConnected = false;
            // Even with view passing (locks cleared), a regular player must
            // not learn the name or online state of an offline PC.
            pc.ClearLocksByName("view");
            var viewer = GameObject.Create("viewer", isPc: true);
            ObjectRegistry.AddObject(viewer);
            viewer.IsConnected = true;
            Assert.Equal("Someone", pc.GetDisplayName(viewer));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void OfflinePc_BuilderViewerSeesOfflineSuffix()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var pc = GameObject.Create("hero", isPc: true);
            ObjectRegistry.AddObject(pc);
            pc.IsConnected = false;
            // Stock locks deny everyone sight of offline PCs; builders pass
            // view and see the name with the offline suffix.
            var builder = GameObject.Create("builder", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            builder.IsConnected = true;
            Assert.Equal("hero (offline)", pc.GetDisplayName(builder));
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

    [Fact]
    public void GetDisplayName_ViewDeniedOffline_DoesNotLeakName()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var target = GameObject.Create("f4hidden", isPc: true);
            var looker = GameObject.Create("f4looker", isPc: true);
            ObjectRegistry.AddObject(target);
            ObjectRegistry.AddObject(looker);
            Assert.False(target.IsConnected);
            Assert.False(target.Access(looker, "view"));
            Assert.Equal("Someone", target.GetDisplayName(looker));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
