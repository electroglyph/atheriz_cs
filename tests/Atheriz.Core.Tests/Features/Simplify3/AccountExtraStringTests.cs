using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Account extras share one string reader: set values round-trip, missing keys
// keep the empty default, and non-string payloads take the raw-text path. The
// characters branch is untouched and still round-trips.
[Collection("Ported")]
public class AccountExtraStringTests
{
    [Fact]
    public void AccountFromDto_PasswordAndBanReasonSet_RoundTripPreserved()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("extra_user", "pw", saltOverride: "salt");
            acc.BanReason = "spam";
            var dto = acc.ToDto();

            var back = Account.FromDto(dto);

            Assert.Equal(acc.PasswordHash, back.PasswordHash);
            Assert.Equal("spam", back.BanReason);
            Assert.False(back.LoggedIn);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AccountFromDto_MissingExtras_DefaultToEmpty()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("missing_user", "pw", saltOverride: "salt");
            acc.BanReason = "spam";
            var dto = acc.ToDto();
            dto.Extra.Remove("password");
            dto.Extra.Remove("banReason");

            var back = Account.FromDto(dto);

            Assert.Equal("", back.PasswordHash);
            Assert.Equal("", back.BanReason);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AccountFromDto_ExplicitNullExtras_TakeNonStringPath()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("null_user", "pw", saltOverride: "salt");
            var dto = acc.ToDto();
            dto.Extra["password"] = JsonOptions.ToElement<string?>(null);
            dto.Extra["banReason"] = JsonOptions.ToElement<string?>(null);

            var back = Account.FromDto(dto);

            Assert.Equal("null", back.PasswordHash);
            Assert.Equal("null", back.BanReason);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AccountFromDto_NonStringExtras_TakeRawTextPath()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("raw_user", "pw", saltOverride: "salt");
            var dto = acc.ToDto();
            dto.Extra["password"] = JsonOptions.ToElement(123);
            dto.Extra["banReason"] = JsonOptions.ToElement(true);

            var back = Account.FromDto(dto);

            Assert.Equal("123", back.PasswordHash);
            Assert.Equal("true", back.BanReason);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AccountFromDto_CharactersBranch_StillRoundTrips()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("char_user", "pw", saltOverride: "salt");
            var ch = GameObject.Create("hero");
            acc.AddCharacter(ch);
            var dto = acc.ToDto();

            var back = Account.FromDto(dto);

            Assert.Contains(ch.Id, back.Characters);
            Assert.Equal(JsonValueKind.False, dto.Extra["loggedIn"].ValueKind);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
