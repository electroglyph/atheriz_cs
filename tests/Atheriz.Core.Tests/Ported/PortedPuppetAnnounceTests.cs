// Port of atheriz/tests/test_puppet_announce.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPuppetAnnounceTests
{
    [Fact]
    public void NoWalkAnnouncementOnPuppet()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test",0,0,0), desc:"A room.", symbol:"#");
        ObjectRegistry.AddObject(node);
        var puppet = GameObject.Create("Player", isPc:true);
        var observer = GameObject.Create("Observer");
        ObjectRegistry.AddObject(puppet); ObjectRegistry.AddObject(observer);
        puppet.MoveTo(node);
        observer.MoveTo(node);
        observer.ClearMessages();
        puppet.Session = new Session();
        // at_post_puppet should not broadcast walk messages
        puppet.AtPostPuppet();
        var texts = observer.PeekMessages();
        Assert.DoesNotContain(texts, t => t.ToLowerInvariant().Contains("walk"));
    }
}
