using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Vetoed gives report the refusal (give.py:172-177).
[Collection("Ported")]
public class GiveVetoMessageTests
{
    private sealed class VetoItem : GameObject
    {
        public VetoItem()
        {
            Id = IdGenerator.GetUniqueId();
            Name = "cursed";
            IsItem = true;
        }

        public override bool AtPreGive(GameObject giver, GameObject receiver) => false;
    }

    [Fact]
    public void Give_VetoedItem_ReportsRefusal()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("giveveto", 0, 0, 0));
            nh.AddNode(node);
            var giver = GameObject.Create("giver", isPc: true);
            ObjectRegistry.AddObject(giver);
            giver.IsConnected = true;
            Assert.True(giver.MoveTo(node));
            var receiver = GameObject.Create("receiver", isPc: true);
            ObjectRegistry.AddObject(receiver);
            receiver.IsConnected = true;
            Assert.True(receiver.MoveTo(node));
            var item = new VetoItem();
            ObjectRegistry.AddObject(item);
            Assert.True(item.MoveTo(giver));
            var cmd = new GiveCommand();
            giver.ClearMessages();
            cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "cursed", "receiver" }));
            Assert.Contains("You can't give", string.Join(" ", giver.PeekMessages()));
            Assert.Contains(item.Id, giver.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
