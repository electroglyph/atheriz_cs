using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// at_say carries whisper mode, per-receiver mapping, and msg types
// (base_obj.py:1976-2115); the (text, msgSelf) override stays compatible.
[Collection("Ported")]
public class SayWhisperTests
{
    private static (Node node, GameObject speaker, GameObject hearer, GameObject bystander) Setup()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("sayarea", 0, 0, 0));
        nh.AddNode(node);
        GameObject mk(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            Assert.True(o.MoveTo(node));
            o.ClearMessages();
            return o;
        }
        return (node, mk("speaker"), mk("hearer"), mk("bystander"));
    }

    [Fact]
    public void Whisper_ReachesReceiver_NotLocation()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (_, speaker, hearer, bystander) = Setup();
            speaker.AtSayFull("psst", msgSelf: true, receivers: new[] { hearer }, whisper: true);
            Assert.Contains(hearer.PeekMessages(), m => m.Contains("whispers"));
            Assert.DoesNotContain(bystander.PeekMessages(), m => m.Contains("psst"));
            Assert.Contains(speaker.PeekMessages(), m => m.Contains("whisper to"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Say_ReachesLocation_ExcludingReceivers()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (_, speaker, hearer, bystander) = Setup();
            speaker.AtSayFull("hello", msgSelf: true, receivers: new[] { hearer });
            Assert.Contains(bystander.PeekMessages(), m => m.Contains("says"));
            Assert.DoesNotContain(hearer.PeekMessages(), m => m.Contains("hello") && m.Contains("says"));
            Assert.Contains(speaker.PeekMessages(), m => m.Contains("say,"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
