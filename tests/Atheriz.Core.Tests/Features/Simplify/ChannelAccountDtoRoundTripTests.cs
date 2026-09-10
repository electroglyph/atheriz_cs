using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// DTO persistence shape: channel history triples, account extras (loggedIn
// persisted as false), and the shared single-id fetch outcome.
[Collection("Ported")]
public class ChannelAccountDtoRoundTripTests
{
    [Fact]
    public void Channel_ToDto_KeepsHistoryTriples()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Id = 4242;
            ch.Name = "chan";
            var sender = GameObject.Create("Bob");
            ch.Msg("hello", sender);

            var dto = ch.ToDto();

            Assert.Equal("channel", dto.Type);
            var history = dto.Extra["history"];
            Assert.Equal(JsonValueKind.Array, history.ValueKind);
            Assert.Equal(1, history.GetArrayLength());
            var triple = history[0];
            Assert.Equal(3, triple.GetArrayLength());
            Assert.Equal("Bob", triple[1].GetString());
            Assert.Equal("hello", triple[2].GetString());

            var (sql, pars) = ch.GetSaveOps();
            Assert.StartsWith("INSERT OR REPLACE", sql);
            Assert.Equal(4242, pars[0]);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Account_ToDto_PersistsLoggedInFalse_AndRoundTrips()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("dto_user", "pw", saltOverride: "salt");
            var dto = acc.ToDto();

            Assert.Equal("account", dto.Type);
            Assert.Equal(JsonValueKind.False, dto.Extra["loggedIn"].ValueKind);

            var back = Account.FromDto(dto);
            Assert.Equal(acc.PasswordHash, back.PasswordHash);
            Assert.False(back.LoggedIn);

            var (sql, pars) = acc.GetSaveOps();
            Assert.StartsWith("INSERT OR REPLACE", sql);
            Assert.Equal(acc.Id, pars[0]);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetSingle_MatchesGetFirstOrDefault()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("one");
            ObjectRegistry.AddObject(obj);

            Assert.Same(obj, ObjectRegistry.GetSingle(obj.Id));
            Assert.Equal(ObjectRegistry.Get(obj.Id).FirstOrDefault(), ObjectRegistry.GetSingle(obj.Id));
            Assert.Null(ObjectRegistry.GetSingle(-987654));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
