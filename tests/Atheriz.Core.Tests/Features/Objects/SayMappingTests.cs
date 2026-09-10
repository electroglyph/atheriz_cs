using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// AtSayFull per-audience mappings share one builder: caller custom entries
// merge over the top for the self, receiver, and location audiences alike.
[Collection("Ported")]
public class SayMappingTests
{
    private static (GameObject speaker, GameObject hearer, GameObject bystander) Setup()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("saymap", 0, 0, 0));
        nh.AddNode(node);
        GameObject Mk(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            Assert.True(o.MoveTo(node));
            o.ClearMessages();
            return o;
        }
        return (Mk("speaker"), Mk("hearer"), Mk("bystander"));
    }

    [Fact]
    public void CustomSpeechOverride_ReachesAllAudiences()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (speaker, hearer, bystander) = Setup();
            var mapping = new Dictionary<string, object?>(StringComparer.Ordinal) { ["speech"] = "HACK" };
            speaker.AtSayFull("hello", msgSelf: true, receivers: new[] { hearer },
                msgReceivers: "{speech}!", mapping: mapping);
            Assert.Contains(speaker.PeekMessages(), m => m.Contains("HACK"));
            Assert.Contains(hearer.PeekMessages(), m => m.Contains("HACK!"));
            Assert.Contains(bystander.PeekMessages(), m => m.Contains("HACK"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void WhisperModes_KeepDistinctTypes()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (speaker, hearer, bystander) = Setup();
            speaker.AtSayFull("psst", msgSelf: true, receivers: new[] { hearer }, whisper: true);
            Assert.Contains(hearer.PeekMessages(), m => m.Contains("whispers"));
            Assert.DoesNotContain(bystander.PeekMessages(), m => m.Contains("psst"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
