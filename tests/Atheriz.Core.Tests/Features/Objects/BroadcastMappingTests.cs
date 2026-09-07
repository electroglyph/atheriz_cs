using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Broadcasts must not mutate the caller's mapping dict: both MsgContents
// overloads write mapping["you"] into the caller-supplied dictionary, so a
// reused mapping leaks a stale actor (Messaging/GameObjectMessaging.cs:148;
// Node.Partial.cs:270).
[Collection("Ported")]
public class BroadcastMappingTests
{
    [Fact]
    public void GameObject_MsgContents_DoesNotMutateCallerMapping()
    {
        // Correct: the passed mapping has no "you" key added by the broadcast.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var listener = GameObject.Create("ear");
            ObjectRegistry.AddObject(room);
            ObjectRegistry.AddObject(listener);
            Assert.True(listener.MoveTo(room));

            var mapping = new Dictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            room.MsgContents("hi", mapping: mapping);
            Assert.False(mapping.ContainsKey("you"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Node_MsgContents_DoesNotMutateCallerMapping()
    {
        // Correct: same copy-on-write contract on the Node overload.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 6, 6, 0));
            var listener = GameObject.Create("ear");
            ObjectRegistry.AddObject(listener);
            Assert.True(listener.MoveTo(node));

            var mapping = new Dictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            node.MsgContents("hi", mapping: mapping);
            Assert.False(mapping.ContainsKey("you"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
