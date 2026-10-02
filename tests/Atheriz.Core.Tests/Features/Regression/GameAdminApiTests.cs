using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Server.Hosting;
using System.Text.Json;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for the landing-page admin API: token service, effective
// privilege (max over account + characters), below-level edit gates, and the
// set/unset rules mirrored from the in-game commands.
[Collection("Ported")]
public class GameAdminApiTests
{
    private static Account NewAccount(string name, Privilege level = Privilege.Player)
    {
        var acc = Account.Create(name, "pass12345", "testsalt");
        acc.PrivilegeLevel = level;
        return acc;
    }

    private static GameObject NewCharacter(Account acc, string name, Privilege level)
    {
        var hero = GameObject.Create(name, isPc: true);
        hero.PrivilegeLevel = level;
        ObjectRegistry.AddObject(hero);
        acc.AddCharacter(hero);
        return hero;
    }

    [Fact]
    public void TokenService_Mint_Resolve_Roundtrip()
    {
        ObjectRegistry.ClearAll();
        var svc = new GameTokenService();
        var acc = NewAccount("tokenbob");
        string token = svc.Mint(acc.Id);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(acc.Id, svc.Resolve(token)?.Id);
    }

    [Fact]
    public void TokenService_Unknown_And_Revoked_AreNull()
    {
        ObjectRegistry.ClearAll();
        var svc = new GameTokenService();
        Assert.Null(svc.Resolve("nope"));
        var acc = NewAccount("tokenrev");
        string token = svc.Mint(acc.Id);
        svc.Revoke(token);
        Assert.Null(svc.Resolve(token));
    }

    [Fact]
    public void ToDetail_AccountWithHigherActingLevel_ExplainsActsAs()
    {
        ObjectRegistry.ClearAll();
        var acc = NewAccount("actsbob");
        NewCharacter(acc, "ActsHero", Privilege.Admin);
        var detail = AdminAccess.ToDetail(acc, acc);
        Assert.Equal((int)Privilege.Admin, detail.Privilege);
        Assert.Equal("Player", Assert.Single(detail.Props, p => p.Name == "PrivilegeLevel").Value);
        var note = Assert.Single(detail.Props, p => p.Name == "acts_as");
        Assert.Equal("admin", note.Value);
        Assert.False(note.Editable);

        var plain = NewAccount("actsplain");
        Assert.DoesNotContain(AdminAccess.ToDetail(plain, plain).Props, p => p.Name == "acts_as");

        var rock = GameObject.Create("rock", isItem: true);
        ObjectRegistry.AddObject(rock);
        Assert.DoesNotContain(AdminAccess.ToDetail(acc, rock).Props, p => p.Name == "acts_as");
    }

    [Fact]
    public void ShownPrivilege_AccountReportsActingLevel_OthersRaw()
    {
        ObjectRegistry.ClearAll();
        var acc = NewAccount("shownbob");
        Assert.Equal(Privilege.Player, acc.PrivilegeLevel);
        NewCharacter(acc, "ShownHero", Privilege.Admin);
        Assert.Equal(Privilege.Admin, AdminAccess.ShownPrivilege(acc));
        Assert.Equal((int)Privilege.Admin, AdminAccess.ToListItem(acc).Privilege);
        var rock = GameObject.Create("rock", isItem: true);
        ObjectRegistry.AddObject(rock);
        Assert.Equal(Privilege.Guest, AdminAccess.ShownPrivilege(rock));
        Assert.Equal((int)Privilege.Guest, AdminAccess.ToListItem(rock).Privilege);
    }

    [Fact]
    public void TokenService_TokenSurvivesRepeatedResolve()
    {
        ObjectRegistry.ClearAll();
        var svc = new GameTokenService();
        var acc = NewAccount("tokenreuse");
        string token = svc.Mint(acc.Id);
        Assert.Equal(acc.Id, svc.Resolve(token)?.Id);
        Assert.Equal(acc.Id, svc.Resolve(token)?.Id);
    }

    [Fact]
    public void TokenService_TwoTokens_Differ()
    {
        ObjectRegistry.ClearAll();
        var svc = new GameTokenService();
        var acc = NewAccount("tokentwo");
        Assert.NotEqual(svc.Mint(acc.Id), svc.Mint(acc.Id));
    }

    [Fact]
    public void EffectiveLevel_CharactersOnly_AccountLevelIgnored()
    {
        ObjectRegistry.ClearAll();
        var acc = NewAccount("effbob");
        Assert.Equal(Privilege.Player, AdminAccess.EffectiveLevel(acc));
        NewCharacter(acc, "EffHero", Privilege.Builder);
        Assert.Equal(Privilege.Builder, AdminAccess.EffectiveLevel(acc));
        acc.PrivilegeLevel = Privilege.Admin;
        Assert.Equal(Privilege.Builder, AdminAccess.EffectiveLevel(acc));

        var boosted = NewAccount("effboost", Privilege.Admin);
        NewCharacter(boosted, "EffZero", Privilege.Guest);
        Assert.Equal(Privilege.Player, AdminAccess.EffectiveLevel(boosted));

        var legacy = NewAccount("efflegacy");
        legacy.PrivilegeLevel = Privilege.Guest;
        Assert.Equal(Privilege.Player, AdminAccess.EffectiveLevel(legacy));
    }

    [Fact]
    public void CanEdit_BuilderBelowOnly_AdminAnything_OwnAvatar()
    {
        ObjectRegistry.ClearAll();
        var builderAcct = NewAccount("caneditbuilder");
        var own = NewCharacter(builderAcct, "CanEditOwn", Privilege.Builder);
        var player = GameObject.Create("CanEditPlayer", isPc: true);
        var peer = GameObject.Create("CanEditPeer", isPc: true);
        peer.PrivilegeLevel = Privilege.Builder;

        Assert.True(AdminAccess.CanEdit(builderAcct, player, out _));
        Assert.False(AdminAccess.CanEdit(builderAcct, peer, out var why));
        Assert.Equal("You cannot modify an object of equal or higher privilege.", why);
        Assert.True(AdminAccess.CanEdit(builderAcct, own, out _));

        var adminAcct = NewAccount("caneditadmin");
        NewCharacter(adminAcct, "CanEditAdminHero", Privilege.Admin);
        Assert.True(AdminAccess.CanEdit(adminAcct, peer, out _));
    }

    [Fact]
    public void TryApplySet_BuilderSetsDesc_ProtectedNeedsAdmin()
    {
        ObjectRegistry.ClearAll();
        var builderAcct = NewAccount("setbuilder");
        NewCharacter(builderAcct, "SetHero", Privilege.Builder);
        var target = GameObject.Create("SetTarget", isPc: true);

        Assert.True(AdminAccess.TryApplySet(builderAcct, target, "desc", "A fine room.", out var err, out var isNew));
        Assert.Null(err);
        Assert.False(isNew);
        Assert.Equal("A fine room.", target.Desc);

        Assert.False(AdminAccess.TryApplySet(builderAcct, target, "privilege_level", "Builder", out err, out _));
        Assert.Equal("'privilege_level' is protected and cannot be set.", err);

        var adminAcct = NewAccount("setadmin");
        NewCharacter(adminAcct, "SetAdminHero", Privilege.Admin);
        Assert.True(AdminAccess.TryApplySet(adminAcct, target, "privilege_level", "Builder", out err, out _));
        Assert.Null(err);
        Assert.Equal(Privilege.Builder, target.PrivilegeLevel);
    }

    [Fact]
    public void TryApplySet_MoveGate_And_PrivilegeGate_Refuse()
    {
        ObjectRegistry.ClearAll();
        var builderAcct = NewAccount("gatebuilder");
        NewCharacter(builderAcct, "GateHero", Privilege.Builder);
        var target = GameObject.Create("GateTarget", isPc: true);

        // The remaining MoveGate members still refuse over HTTP.
        Assert.False(AdminAccess.TryApplySet(builderAcct, target, "_contents", "x", out var err, out _));
        Assert.Equal("'_contents' cannot be set directly; use move/teleport instead.", err);

        var peer = GameObject.Create("GatePeer", isPc: true);
        peer.PrivilegeLevel = Privilege.Builder;
        Assert.False(AdminAccess.TryApplySet(builderAcct, peer, "desc", "x", out err, out _));
        Assert.Equal("You cannot modify an object of equal or higher privilege.", err);
    }

    [Fact]
    public void LocationEdit_CoordString_MovesObject()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("movadmin");
        NewCharacter(adminAcct, "MovAdminHero", Privilege.Admin);
        var room = new Node(new Coord("testarea", 1, 2, 3), "Test Room");
        ObjectRegistry.AddObject(room);
        var rock = GameObject.Create("MovRock", isItem: true);
        ObjectRegistry.AddObject(rock);

        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "Location", "testarea(1,2,3)", out var err, out var isNew));
        Assert.Null(err);
        Assert.False(isNew);
        Assert.Equal(LocationRef.FromCoord(new Coord("testarea", 1, 2, 3)), rock.Location);
        Assert.Same(room, rock.ResolveLocationObject());

        // Same coord in another accepted shape: success, still there.
        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "location", "testarea 1 2 3", out err, out _));
        Assert.Null(err);
        Assert.Same(room, rock.ResolveLocationObject());
    }

    [Fact]
    public void LocationEdit_HashId_And_Null_MoveAndClear()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("movboxadmin");
        NewCharacter(adminAcct, "MovBoxHero", Privilege.Admin);
        var box = GameObject.Create("MovBox", isItem: true);
        ObjectRegistry.AddObject(box);
        var rock = GameObject.Create("MovBoxRock", isItem: true);
        ObjectRegistry.AddObject(rock);

        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "location", $"#{box.Id}", out var err, out _));
        Assert.Null(err);
        Assert.Equal(new LocationRef.ObjectLocation(box.Id), rock.Location);
        Assert.Contains(rock.Id, box.ContentsSnapshot);

        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "Location", "null", out err, out _));
        Assert.Null(err);
        Assert.Equal(LocationRef.NullLocation.Instance, rock.Location);
        Assert.DoesNotContain(rock.Id, box.ContentsSnapshot);
    }

    [Fact]
    public void LocationEdit_BadInput_ExplainsShapes()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("movbadadmin");
        NewCharacter(adminAcct, "MovBadHero", Privilege.Admin);
        var rock = GameObject.Create("MovBadRock", isItem: true);
        ObjectRegistry.AddObject(rock);

        Assert.False(AdminAccess.TryApplySet(adminAcct, rock, "location", "not a place", out var err, out _));
        Assert.Contains("must be a coord", err);

        Assert.False(AdminAccess.TryApplySet(adminAcct, rock, "location", "#424242", out err, out _));
        Assert.Equal("No object found with ID 424242.", err);

        Assert.False(AdminAccess.TryApplySet(adminAcct, rock, "location", "void(9,9,9)", out err, out _));
        Assert.Equal("No node found at void(9,9,9).", err);

        // Empty/JSON-null clears to nowhere.
        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "location", "", out err, out _));
        Assert.Null(err);
        Assert.Equal(LocationRef.NullLocation.Instance, rock.Location);
    }

    [Fact]
    public void LocationEdit_BelowLevel_Refused_RoomTarget_Refused()
    {
        ObjectRegistry.ClearAll();
        var builderAcct = NewAccount("movgatebuilder");
        NewCharacter(builderAcct, "MovGateHero", Privilege.Builder);
        var peer = GameObject.Create("MovGatePeer", isPc: true);
        peer.PrivilegeLevel = Privilege.Admin;

        Assert.False(AdminAccess.TryApplySet(builderAcct, peer, "location", "null", out var err, out _));
        Assert.Equal("You cannot modify an object of equal or higher privilege.", err);

        var adminAcct = NewAccount("movroomadmin");
        NewCharacter(adminAcct, "MovRoomHero", Privilege.Admin);
        var room = new Node(new Coord("roomarea", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        Assert.False(AdminAccess.TryApplySet(adminAcct, room, "location", "null", out err, out _));
        Assert.Equal("'location' cannot be set on a room; a room's position is its coord.", err);
    }

    [Fact]
    public void HomeEdit_StoresCoord_WithoutMoving()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("homeadmin");
        NewCharacter(adminAcct, "HomeHero", Privilege.Admin);
        var box = GameObject.Create("HomeBox", isItem: true);
        ObjectRegistry.AddObject(box);
        var rock = GameObject.Create("HomeRock", isItem: true);
        ObjectRegistry.AddObject(rock);

        // A coord with no node is a valid home marker (home is data, not a move).
        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "Home", "dark(7,7,7)", out var err, out _));
        Assert.Null(err);
        Assert.Equal(LocationRef.FromCoord(new Coord("dark", 7, 7, 7)), rock.Home);
        Assert.Equal(LocationRef.NullLocation.Instance, rock.Location);

        Assert.True(AdminAccess.TryApplySet(adminAcct, rock, "home", $"#{box.Id}", out err, out _));
        Assert.Null(err);
        Assert.Equal(new LocationRef.ObjectLocation(box.Id), rock.Home);
        Assert.Equal(LocationRef.NullLocation.Instance, rock.Location);
    }

    [Fact]
    public void LocationAndHome_AreEditable_AllSpellings()
    {
        ObjectRegistry.ClearAll();
        var builderAcct = NewAccount("spellbuilder");
        NewCharacter(builderAcct, "SpellHero", Privilege.Builder);
        var rock = GameObject.Create("SpellRock", isItem: true);
        ObjectRegistry.AddObject(rock);

        foreach (var spelling in new[] { "Location", "location", "_location", "Home", "home", "_home" })
            Assert.True(AdminAccess.IsEditableProp(builderAcct, rock, spelling), spelling);

        var detail = AdminAccess.ToDetail(builderAcct, rock);
        var loc = Assert.Single(detail.Props, p => p.Name == "Location");
        Assert.True(loc.Editable);
        Assert.Equal("null", loc.Value);
    }

    [Fact]
    public void AccountPrivilege_IsInert_RefusedAndReadOnly()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("privadmin");
        NewCharacter(adminAcct, "PrivAdminHero", Privilege.Admin);
        var targetAcc = NewAccount("privtarget");

        Assert.False(AdminAccess.TryApplySet(adminAcct, targetAcc, "privilege_level", "Builder", out var err, out _));
        Assert.Equal("Accounts carry no privilege; grant it on one of its characters instead.", err);
        Assert.False(AdminAccess.IsEditableProp(adminAcct, targetAcc, "PrivilegeLevel"));

        var player = GameObject.Create("PrivPlayer", isPc: true);
        Assert.True(AdminAccess.IsEditableProp(adminAcct, player, "PrivilegeLevel"));
        Assert.True(AdminAccess.TryApplySet(adminAcct, player, "privilege_level", "Builder", out err, out _));
        Assert.Null(err);
        Assert.Equal(Privilege.Builder, player.PrivilegeLevel);
    }

    [Fact]
    public void TryApplySet_NewExtra_Flagged_Rename_Validated()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("extraadmin");
        NewCharacter(adminAcct, "ExtraAdminHero", Privilege.Admin);
        var target = GameObject.Create("ExtraTarget", isPc: true);

        Assert.True(AdminAccess.TryApplySet(adminAcct, target, "title", "Warden", out var err, out var isNew));
        Assert.Null(err);
        Assert.True(isNew);

        Assert.False(AdminAccess.TryApplySet(adminAcct, target, "name", "x", out err, out _));
        Assert.Equal("Name must be at least 3 characters.", err);

        Assert.True(AdminAccess.TryApplySet(adminAcct, target, "name", "Renamed Hero", out err, out _));
        Assert.Null(err);
        Assert.Equal("Renamed Hero", target.Name);
    }

    [Fact]
    public void TryRemoveExtra_Removes_And_RefusesKnown()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("unadmin");
        NewCharacter(adminAcct, "UnAdminHero", Privilege.Admin);
        var target = GameObject.Create("UnTarget", isPc: true);
        Assert.True(AdminAccess.TryApplySet(adminAcct, target, "title", "Warden", out _, out _));

        Assert.True(AdminAccess.TryRemoveExtra(adminAcct, target, "title", out var err));
        Assert.Null(err);
        Assert.False(AdminAccess.TryRemoveExtra(adminAcct, target, "title", out err));
        Assert.Equal("No such attribute 'title'.", err);
        Assert.False(AdminAccess.TryRemoveExtra(adminAcct, target, "desc", out err));
        Assert.Equal("'desc' is not an extra attribute.", err);
    }

    [Fact]
    public void IsSettableProperty_MatchesTrySetPropertyArms()
    {
        ObjectRegistry.ClearAll();
        var obj = GameObject.Create("SettableProbe", isPc: true);
        Assert.True(obj.IsSettableProperty("desc"));
        Assert.True(obj.IsSettableProperty("Desc"));
        Assert.False(obj.IsSettableProperty("Id"));
        Assert.False(obj.IsSettableProperty("id"));
        Assert.False(obj.IsSettableProperty("Location"));
        Assert.False(obj.IsSettableProperty("nope"));

        var node = new Node(new Coord("limbo", 0, 0, 0)) { Name = "SettableRoom" };
        Assert.True(node.IsSettableProperty("theme"));
        Assert.False(node.IsSettableProperty("ScriptsSet"));

        var acc = NewAccount("settableacc");
        Assert.True(acc.IsSettableProperty("ban_reason"));
        Assert.False(acc.IsSettableProperty("characters"));

        var channel = new Channel();
        Assert.True(channel.IsSettableProperty("created_by"));
        Assert.False(channel.IsSettableProperty("history"));
    }

    [Fact]
    public void ToDetail_ListsProps_And_EnumeratesExtras()
    {
        ObjectRegistry.ClearAll();
        var adminAcct = NewAccount("detailadmin");
        NewCharacter(adminAcct, "DetailAdminHero", Privilege.Admin);
        var target = GameObject.Create("DetailTarget", isPc: true);
        Assert.True(AdminAccess.TryApplySet(adminAcct, target, "title", "Warden", out _, out _));
        target.SetExtraJson("password_shadow", JsonSerializer.SerializeToElement("nope"));

        var detail = AdminAccess.ToDetail(adminAcct, target);
        Assert.Equal(target.Id, detail.Id);
        Assert.Equal("player", detail.Type);
        Assert.Contains(detail.Props, p => p.Name == "Desc");
        var extra = Assert.Single(detail.Props, p => p is { Name: "title", IsExtra: true });
        Assert.Equal("Warden", extra.Value);
        Assert.True(extra.Editable);
        Assert.DoesNotContain(detail.Props, p => p.Name == "password_shadow");
        Assert.DoesNotContain(detail.Props, p => p.Name == "extra");
    }

    [Fact]
    public void FromJson_ConvertsShapes()
    {
        Assert.Equal("hi", AdminAccess.FromJson(JsonDocument.Parse("\"hi\"").RootElement));
        Assert.Equal(3, AdminAccess.FromJson(JsonDocument.Parse("3").RootElement));
        Assert.Equal(true, AdminAccess.FromJson(JsonDocument.Parse("true").RootElement));
        Assert.Null(AdminAccess.FromJson(JsonDocument.Parse("null").RootElement));
        var list = Assert.IsType<List<object?>>(AdminAccess.FromJson(JsonDocument.Parse("[1, 2]").RootElement));
        Assert.Equal([1, 2], list);
        var dict = Assert.IsType<Dictionary<string, object?>>(
            AdminAccess.FromJson(JsonDocument.Parse("{\"a\": 1}").RootElement));
        Assert.Equal(1, dict["a"]);
    }

    [Fact]
    public void TryParsePrivilege_NamesAndInts()
    {
        Assert.True(AdminAccess.TryParsePrivilege("Builder", out var p));
        Assert.Equal(Privilege.Builder, p);
        Assert.True(AdminAccess.TryParsePrivilege(5, out p));
        Assert.Equal(Privilege.Admin, p);
        Assert.False(AdminAccess.TryParsePrivilege("Wizard", out _));
        Assert.False(AdminAccess.TryParsePrivilege(99, out _));
    }

    [Fact]
    public void ToDetail_ReportsEditorKindPerProp()
    {
        ObjectRegistry.ClearAll();
        var acc = NewAccount("typeacc");
        var hero = NewCharacter(acc, "TypeHero", Privilege.Player);
        hero.SetExtraJson("flagged", JsonSerializer.SerializeToElement(true));

        var detail = AdminAccess.ToDetail(acc, hero);
        Assert.Equal("bool", detail.Props.Single(p => p.Name == "Quelled").Type);
        Assert.Equal("text", detail.Props.Single(p => p.Name == "Gender").Type);
        Assert.Equal("bool", detail.Props.Single(p => p.Name == "flagged").Type);
        // The serialized shape is what the landing page reads (minimal-API
        // JSON uses web defaults: camelCase).
        var json = JsonSerializer.SerializeToElement(detail,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("bool", json.GetProperty("props").EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "Quelled")
            .GetProperty("type").GetString());
    }

    [Fact]
    public void IsLoginThrottled_BannedHost_UnknownHostNever()
    {
        const string host = "203.0.113.99";
        try
        {
            Assert.False(AdminAccess.IsLoginThrottled(host));
            Assert.False(AdminAccess.IsLoginThrottled("?"));
            ObjectRegistry.BanIp(host, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);
            Assert.True(AdminAccess.IsLoginThrottled(host));
            Assert.False(AdminAccess.IsLoginThrottled("?"));
        }
        finally
        {
            ObjectRegistry.UnbanIp(host);
        }
        Assert.False(AdminAccess.IsLoginThrottled(host));
    }

    // The client sends client_ready on every socket open. With a token
    // auto-login the account is already bound at that point, so the welcome
    // screen (which advertises "connect ... to login") must not be rendered
    // over the character-selection prompt — otherwise a Play-button login
    // ends on what looks like an untouched login prompt.
    [Fact]
    public void ClientReady_AuthenticatedSession_SkipsWelcomeScreen()
    {
        using var env = GlobalTestEnv.Enter();
        var inp = new InputFuncs();
        var conn = new TestConnection();
        conn.Session.Account = NewAccount("readyacct");
        conn.ClearSent();
        inp.ClientReady(conn, [], []);
        Assert.Empty(conn.Sent);

        // Unauthenticated connections still get the welcome screen + prompt.
        conn.Session.Account = null;
        conn.ClearSent();
        inp.ClientReady(conn, [], []);
        Assert.Contains(conn.Sent, s => s.Cmd == "prompt");
        var allText = string.Join(" ", conn.Sent.Select(s => s.Args.FirstOrDefault()?.ToString() ?? ""));
        Assert.Contains("ATHERIZ VERSION", allText);
    }

    [Fact]
    public void ItemEditor_FallbackType_IsOther()
    {
        // The admin type filter's catch-all is labeled "other" (plain
        // objects: no pc/npc/item/script flag, not account/channel/room),
        // and the filter, classifier, and template option agree on it.
        ObjectRegistry.ClearAll();
        try
        {
            var plain = GameObject.Create("plainrock");
            ObjectRegistry.AddObject(plain);
            Assert.Equal("other", AdminAccess.ObjectType(plain));
            var html = SourceScan.Read("src", "Atheriz.Server", "web", "templates", "index.html");
            Assert.Contains("<option value=\"other\">other</option>", html);
            var routes = SourceScan.Read("src", "Atheriz.Server", "Hosting", "GameAdminRoutes.cs");
            Assert.Contains("\"other\"", routes);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ItemEditor_TypeSwitch_CollapsesOpenDetail()
    {
        // Switching the admin search type must collapse the open item
        // editor: the old item's properties must not linger under the new
        // list. doSearch tracks the last listed type and clears the detail
        // pane when it changes (scoped to doSearch so a stray clear
        // elsewhere cannot satisfy this pin).
        var html = SourceScan.Read("src", "Atheriz.Server", "web", "templates", "index.html");
        int start = html.IndexOf("async function doSearch()", StringComparison.Ordinal);
        int end = html.IndexOf("function clearDetailSelection()", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var body = html.Substring(start, end - start);
        Assert.Contains("type !== lastType", body);
        Assert.Contains("clearDetailSelection()", body);
    }
}
