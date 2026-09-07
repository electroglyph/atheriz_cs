using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Recursive delete honors child vetoes (base_obj.py:320-325) and tears down
// follows, channel memberships, sessions, and tick slots (base_obj.py:349-426).
[Collection("Ported")]
public class DeleteVetoTeardownTests
{
    private sealed class VetoBox : GameObject
    {
        public VetoBox()
        {
            Id = IdGenerator.GetUniqueId();
            Name = "vetobox";
            IsContainer = true;
        }

        public override bool AtDelete(GameObject caller) => false;
    }

    [Fact]
    public void DeleteRecursive_VetoedChild_Survives()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var owner = GameObject.Create("owner", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(owner);
            var bag = GameObject.Create("bag", isContainer: true);
            ObjectRegistry.AddObject(bag);
            var veto = new VetoBox();
            ObjectRegistry.AddObject(veto);
            Assert.True(veto.MoveTo(bag));
            var res = bag.Delete(owner, recursive: true);
            Assert.NotNull(res);
            Assert.NotEmpty(ObjectRegistry.Get(veto.Id));
            Assert.Empty(ObjectRegistry.Get(bag.Id));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Delete_ClearsFollowsAndChannels()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var owner = GameObject.Create("owner", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(owner);
            var leader = GameObject.Create("leader", isPc: true);
            ObjectRegistry.AddObject(leader);
            leader.IsConnected = true;
            var doomed = GameObject.Create("doomed", isPc: true);
            ObjectRegistry.AddObject(doomed);
            doomed.IsConnected = true;
            doomed.Following = leader.Id;
            leader.AddFollower(doomed.Id);
            var channel = Channel.Create("doomchan");
            doomed.Subscribe(channel);
            Assert.Contains(channel.Id, doomed.ChannelsSnapshot);
            var res = doomed.Delete(owner, recursive: false);
            Assert.NotNull(res);
            Assert.Null(doomed.Following);
            Assert.DoesNotContain(doomed.Id, leader.FollowersSnapshot);
            Assert.DoesNotContain(channel.Id, doomed.ChannelsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
