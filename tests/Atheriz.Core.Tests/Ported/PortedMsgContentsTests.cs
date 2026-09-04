// Port of atheriz/tests/test_msg_contents.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMsgContentsTests
{
    [Fact]
    public void UnmappedPlaceholderDoesNotCrash()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test",0,0,0));
        ObjectRegistry.AddObject(node);
        var receiver = GameObject.Create("listener");
        ObjectRegistry.AddObject(receiver);
        receiver.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(receiver);
        receiver.ClearMessages();
        var ex = Record.Exception(() => node.MsgContents("hi {foo}"));
        Assert.Null(ex);
        Assert.Single(receiver.PeekMessages());
    }

    [Fact]
    public void MappedPlaceholderIsReplaced()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test",0,0,0));
        ObjectRegistry.AddObject(node);
        var speaker = GameObject.Create("speaker", isPc:true);
        var receiver = GameObject.Create("listener");
        ObjectRegistry.AddObject(speaker); ObjectRegistry.AddObject(receiver);
        receiver.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        speaker.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(speaker); node.AddObject(receiver);
        receiver.ClearMessages();
        node.MsgContents("hi {target}", mapping: new Dictionary<string,object?>{{"target", speaker}});
        var sent = receiver.PeekMessages().FirstOrDefault() ?? "";
        Assert.Contains("speaker", sent.ToLowerInvariant());
    }
}
