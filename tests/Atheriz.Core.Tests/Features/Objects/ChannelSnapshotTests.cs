using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Channel save-ops and DTO share one history snapshot: both observe the same
// entries in order, snapshots are isolated from later writes, and clearing the
// history empties both.
[Collection("Ported")]
public class ChannelSnapshotTests
{
    [Fact]
    public void ChannelSnapshot_ToDtoAndSaveOps_ObserveSameHistory()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Id = 5151;
            ch.Name = "snap";
            var sender = GameObject.Create("Bob");
            ch.Msg("m1", sender);
            ch.Msg("m2", sender);

            var dto = ch.ToDto();
            var history = dto.Extra["history"];
            Assert.Equal(2, history.GetArrayLength());
            Assert.Equal("m1", history[0][2].GetString());
            Assert.Equal("m2", history[1][2].GetString());
            Assert.Equal("Bob", history[1][1].GetString());

            var (sql, pars) = ch.GetSaveOps();
            Assert.StartsWith("INSERT OR REPLACE", sql);
            string json = pars[1].ToString()!;
            Assert.Contains("m1", json);
            Assert.Contains("m2", json);

            var revived = GameObjectDtoSerializer.FromJson(GameObjectDtoSerializer.ToJson(dto));
            Assert.Equal(2, revived.Extra["history"].GetArrayLength());
            Assert.Equal("m2", revived.Extra["history"][1][2].GetString());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ChannelSnapshot_SnapshotIsolatedFromLaterWrites()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Id = 5152;
            ch.Name = "snap";
            var sender = GameObject.Create("Bob");
            ch.Msg("first", sender);

            var dto = ch.ToDto();
            ch.Msg("later", sender);

            Assert.Equal(1, dto.Extra["history"].GetArrayLength());
            Assert.Equal("first", dto.Extra["history"][0][2].GetString());
            Assert.Equal(2, ch.ToDto().Extra["history"].GetArrayLength());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ChannelSnapshot_AfterClearHistory_BothDtoAndSaveAreEmpty()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Id = 5153;
            ch.Name = "snap";
            var sender = GameObject.Create("Bob");
            ch.Msg("gone", sender);
            ch.ClearHistory();

            Assert.Equal(0, ch.ToDto().Extra["history"].GetArrayLength());
            var (_, pars) = ch.GetSaveOps();
            Assert.DoesNotContain("gone", pars[1].ToString());
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
