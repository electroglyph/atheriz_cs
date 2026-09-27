using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Network;

// save-to-server editor settings: the optional fourth map_edit arg validates
// strictly (reject loudly, never burns a seq) and persists normalized JSON on
// the granting session's account (the editor socket is unauthenticated).
[Collection("Ported")]
public sealed class MapEditorSettingsTests
{
    private static Dictionary<string, object?> ValidTools() => new()
    {
        ["activeToolId"] = "brush",
        ["rectMode"] = "light",
        ["ovalMode"] = "circle",
        ["lineMode"] = "double",
        ["lineDiagonal"] = true,
        ["gradientTarget"] = "both",
        ["fillMode"] = "gradient",
        ["eyedropperTarget"] = "fg-bg",
        ["selectMode"] = "rectangle",
        ["rotateMode"] = "cw90",
        ["typeStyle"] = "bold",
    };

    private static List<object?> Color(int r, int g, int b) => [r, g, b];

    private static List<object?> SlotColors(int n)
    {
        var list = new List<object?>(n);
        for (int i = 0; i < n; i++) list.Add(Color(i % 256, 0, 0));
        return list;
    }

    internal static Dictionary<string, object?> ValidSettings() => new()
    {
        ["fgColor"] = Color(204, 204, 204),
        ["bgColor"] = Color(0, 0, 0),
        ["fgSlots"] = SlotColors(8),
        ["bgSlots"] = SlotColors(8),
        ["gradientStops"] = new List<object?> { Color(0, 0, 0), Color(255, 255, 255) },
        ["customChars"] = new List<object?> { "█", "▓" },
        ["selectedChar"] = "█",
        ["fontFamily"] = "KreativeSquare",
        ["fontSize"] = 18,
        ["tools"] = ValidTools(),
        ["roomColor"] = Color(0, 204, 204),
        ["roomVisible"] = true,
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void TryParse_ValidDictionary_NormalizesWithCamelCaseKeys()
    {
        var raw = ValidSettings();
        raw["futureKey"] = "dropped";

        Assert.True(MapEditorSettings.TryParse(raw, out var settings, out var error));
        Assert.Equal("", error);
        Assert.NotNull(settings);
        Assert.Equal([204, 204, 204], settings.FgColor);
        Assert.Equal(8, settings.FgSlots.Count);
        Assert.Equal(2, settings.GradientStops.Count);
        Assert.Equal(["█", "▓"], settings.CustomChars);
        Assert.Equal("brush", settings.Tools.ActiveToolId);
        Assert.True(settings.Tools.LineDiagonal);
        Assert.True(settings.RoomVisible);

        var normalized = ParseJson(settings.ToJson());
        Assert.Equal(204, normalized.GetProperty("fgColor")[0].GetInt32());
        Assert.Equal("circle", normalized.GetProperty("tools").GetProperty("ovalMode").GetString());
        Assert.Equal(18, normalized.GetProperty("fontSize").GetInt32());
        Assert.False(normalized.TryGetProperty("futureKey", out _));
    }

    [Fact]
    public void TryParse_ValidJsonElement_Accepts()
    {
        using var doc = JsonDocument.Parse("""{"fgColor":[204,204,204],"bgColor":[0,0,0],"fgSlots":[[1,2,3]],"bgSlots":[[4,5,6]],"gradientStops":[[0,0,0],[255,255,255]],"customChars":[],"selectedChar":"X","fontFamily":"Mono","fontSize":12,"tools":{"activeToolId":"line","rectMode":"heavy","ovalMode":"light","lineMode":"light","lineDiagonal":false,"gradientTarget":"foreground","fillMode":"brush","eyedropperTarget":"fg-fg","selectMode":"single","rotateMode":"free","typeStyle":"regular"},"roomColor":[0,204,204],"roomVisible":false}""");

        Assert.True(MapEditorSettings.TryParse(doc.RootElement, out var settings, out var error));
        Assert.Equal("", error);
        Assert.NotNull(settings);
        Assert.Equal("Mono", settings.FontFamily);
        Assert.False(settings.RoomVisible);
        Assert.Empty(settings.CustomChars);
    }

    [Fact]
    public void TryParse_InvalidShapes_RejectsNamingTheField()
    {
        var cases = new List<(Action<Dictionary<string, object?>> breakIt, string expect)>
        {
            ((d) => { d["fgColor"] = 200; }, "'fgColor'"),
            ((d) => { d["fgColor"] = Color(-1, 0, 0); }, "'fgColor'"),
            ((d) => { d["fgSlots"] = new List<object?>(); }, "'fgSlots'"),
            ((d) => { d["gradientStops"] = new List<object?> { Color(1, 2, 3) }; }, "'gradientStops'"),
            ((d) => { d["customChars"] = new List<object?> { "" }; }, "'customChars[0]'"),
            ((d) => { d["selectedChar"] = ""; }, "'selectedChar'"),
            ((d) => { d["fontFamily"] = ""; }, "'fontFamily'"),
            ((d) => { d["fontSize"] = 0; }, "'fontSize'"),
            ((d) => { d["fontSize"] = "big"; }, "'fontSize'"),
            ((d) => { ((Dictionary<string, object?>)d["tools"]!)["rectMode"] = "wavy"; }, "'tools.rectMode'"),
            ((d) => { ((Dictionary<string, object?>)d["tools"]!)["lineDiagonal"] = "yes"; }, "'tools.lineDiagonal'"),
            ((d) => { ((Dictionary<string, object?>)d["tools"]!).Remove("rotateMode"); }, "'tools.rotateMode'"),
            ((d) => { d["roomVisible"] = 1; }, "'roomVisible'"),
            ((d) => { d.Remove("tools"); }, "'tools'"),
        };
        foreach (var (breakIt, expect) in cases)
        {
            var raw = ValidSettings();
            breakIt(raw);
            Assert.False(MapEditorSettings.TryParse(raw, out var settings, out var error));
            Assert.Null(settings);
            Assert.Contains(expect, error);
        }

        Assert.False(MapEditorSettings.TryParse(new List<object?>(), out _, out var listError));
        Assert.Contains("must be an object", listError);
        Assert.False(MapEditorSettings.TryParse(null, out _, out var nullError));
        Assert.Contains("must be an object", nullError);
    }

    private static TestConnection EditorConn()
    {
        var c = new TestConnection();
        c.ClientHost = "10.0.0.1";
        return c;
    }

    private static void ResetChains()
    {
        MapEdit.Reset();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
    }

    private static string Handshake(TestConnection conn, string key)
    {
        new InputFuncs().MapEditHandler(conn, [key, 0, new List<object?>()], []);
        return conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no handshake key");
    }

    [Fact]
    public void MapEditHandler_SettingsSave_PersistsNormalizedJsonOnGrantingAccount()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var gameConn = EditorConn();
            var account = Account.Create("mapedit_settings_user", "pw", saltOverride: "salt");
            gameConn.Session.Account = account;
            var editorConn = EditorConn();

            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0, gameConn.Session);
            var live = Handshake(editorConn, key);
            editorConn.ClearSent();

            var cells = new List<object?> { new List<object?> { 0, 0, "X" } };
            new InputFuncs().MapEditHandler(editorConn, [live, 1, cells, ValidSettings()], []);

            var ack = Assert.Single(editorConn.Sent);
            Assert.Equal("map_ack", ack.Cmd);
            var stored = ParseJson(account.MapEditorSettingsJson);
            Assert.Equal(204, stored.GetProperty("fgColor")[0].GetInt32());
            Assert.Equal("brush", stored.GetProperty("tools").GetProperty("activeToolId").GetString());
            Assert.Equal(8, stored.GetProperty("fgSlots").GetArrayLength());
            Assert.True(account.IsModified);
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MapEditHandler_SettingsOnlySaveWithEmptyCells_Acks()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var gameConn = EditorConn();
            var account = Account.Create("mapedit_settings_only", "pw", saltOverride: "salt");
            gameConn.Session.Account = account;
            var editorConn = EditorConn();

            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0, gameConn.Session);
            var live = Handshake(editorConn, key);
            editorConn.ClearSent();

            new InputFuncs().MapEditHandler(editorConn, [live, 1, new List<object?>(), ValidSettings()], []);

            var ack = Assert.Single(editorConn.Sent);
            Assert.Equal("map_ack", ack.Cmd);
            Assert.NotEqual("", account.MapEditorSettingsJson);
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MapEditHandler_BadSettings_RejectsWithoutConsumingSeq()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var gameConn = EditorConn();
            gameConn.Session.Account = Account.Create("mapedit_bad_settings", "pw", saltOverride: "salt");
            var editorConn = EditorConn();

            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0, gameConn.Session);
            var live = Handshake(editorConn, key);
            editorConn.ClearSent();

            var bad = ValidSettings();
            bad["fontSize"] = -5;
            new InputFuncs().MapEditHandler(editorConn, [live, 1, new List<object?>(), bad], []);

            var reject = Assert.Single(editorConn.Sent);
            Assert.Equal("map_edit_reject", reject.Cmd);
            Assert.Contains("Invalid map editor settings", reject.Args[0]?.ToString());
            Assert.Contains("'fontSize'", reject.Args[0]?.ToString());
            editorConn.ClearSent();

            // The seq was not burned: the same seq still processes.
            new InputFuncs().MapEditHandler(editorConn, [live, 1, new List<object?>(), ValidSettings()], []);
            Assert.Equal("map_ack", Assert.Single(editorConn.Sent).Cmd);
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MapEditHandler_Retry_DoesNotResaveSettings()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var gameConn = EditorConn();
            var account = Account.Create("mapedit_retry_settings", "pw", saltOverride: "salt");
            gameConn.Session.Account = account;
            var editorConn = EditorConn();

            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0, gameConn.Session);
            var live = Handshake(editorConn, key);
            editorConn.ClearSent();

            new InputFuncs().MapEditHandler(editorConn, [live, 1, new List<object?>(), ValidSettings()], []);
            editorConn.ClearSent();
            var first = account.MapEditorSettingsJson;
            Assert.NotEqual("", first);

            // Replay the pre-rotation key with the same seq: retry acks, keeps v1.
            var v2 = ValidSettings();
            v2["fontFamily"] = "Changed";
            new InputFuncs().MapEditHandler(editorConn, [live, 1, new List<object?>(), v2], []);

            var retry = Assert.Single(editorConn.Sent);
            Assert.Equal("map_ack", retry.Cmd);
            Assert.Equal(first, account.MapEditorSettingsJson);
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MapEditHandler_NoAccountOnGrantingSession_AcksWithoutStoring()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var gameConn = EditorConn();
            var editorConn = EditorConn();

            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0, gameConn.Session);
            var live = Handshake(editorConn, key);
            editorConn.ClearSent();

            new InputFuncs().MapEditHandler(editorConn, [live, 1, new List<object?>(), ValidSettings()], []);

            Assert.Equal("map_ack", Assert.Single(editorConn.Sent).Cmd);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void Account_SettingsExtra_RoundTripsThroughDto()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("settings_roundtrip", "pw", saltOverride: "salt");
            Assert.True(MapEditorSettings.TryParse(ValidSettings(), out var settings, out _));
            acc.SetMapEditorSettings(settings!.ToJson());

            var dto = acc.ToDto();
            Assert.True(dto.Extra.TryGetValue("mapEditorSettings", out var el));
            Assert.Equal(JsonValueKind.Object, el.ValueKind);
            Assert.Equal(204, el.GetProperty("fgColor")[0].GetInt32());

            var back = Account.FromDto(dto);
            Assert.Equal(acc.MapEditorSettingsJson, back.MapEditorSettingsJson);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Account_WithoutSettings_OmitsExtraKey()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("settings_absent", "pw", saltOverride: "salt");
            Assert.Equal("", acc.MapEditorSettingsJson);
            Assert.False(acc.ToDto().Extra.ContainsKey("mapEditorSettings"));
            Assert.Equal("(none)", acc.GetExamMembers().First(m => m.name == "MapEditorSettings").value);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Account_SettingsProperty_IsReadOnly()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var acc = Account.Create("settings_readonly", "pw", saltOverride: "salt");
            Assert.True(acc.IsKnownProperty("mapEditorSettings"));
            Assert.False(acc.TrySetProperty("mapEditorSettings", "{}", out var error));
            Assert.Contains("read-only", error);
            Assert.Throws<ArgumentException>(() => acc.SetMapEditorSettings(""));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    private static Dictionary<string, object?> RunDraw(TestConnection conn, Account? account = null)
    {
        var mh = GlobalServices.GetMapHandler();
        mh.SetMapInfo("TestArea", 0, new MapInfo("TestArea"));
        var node = new Node(new Coord("TestArea", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var nh = GlobalServices.GetNodeHandler();
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea", 0);
        area.AddGrid(grid);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        grid.AddNode(node);
        var caller = GameObject.Create("Caller", isPc: true);
        caller.PrivilegeLevel = Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn);
        // DrawCommand reads the caller's own session (not the connection's),
        // so the account must hang here to ride the grant.
        if (account is not null) caller.Session.Account = account;
        caller.Session.Connection = conn;
        conn.Session.Puppet = caller;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        return conn.Sent.First(s => s.Cmd == "launch_draw").Args[1] as Dictionary<string, object?>
            ?? throw new InvalidOperationException("no launch_draw payload");
    }

    [Fact]
    public void DrawPayload_IncludesEditorSettings_WhenAccountHasThem()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var conn = EditorConn();
            var account = Account.Create("draw_restore_user", "pw", saltOverride: "salt");
            var raw = ValidSettings();
            raw["fontFamily"] = "RestoredFamily";
            raw["fontSize"] = 22;
            Assert.True(MapEditorSettings.TryParse(raw, out var settings, out _));
            account.SetMapEditorSettings(settings!.ToJson());

            var payload = RunDraw(conn, account);

            var el = Assert.IsType<JsonElement>(payload["editorSettings"]);
            Assert.Equal("RestoredFamily", el.GetProperty("fontFamily").GetString());
            Assert.Equal(22, el.GetProperty("fontSize").GetInt32());
            Assert.Equal(204, el.GetProperty("fgColor")[0].GetInt32());
            Assert.Equal("brush", el.GetProperty("tools").GetProperty("activeToolId").GetString());
            Assert.True(el.GetProperty("roomVisible").GetBoolean());
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void DrawPayload_OmitsEditorSettings_WhenAccountHasNone()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var conn = EditorConn();
            var payload = RunDraw(conn, Account.Create("draw_restore_empty", "pw", saltOverride: "salt"));

            Assert.False(payload.ContainsKey("editorSettings"));
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void DrawPayload_OmitsEditorSettings_WhenNoAccount()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var payload = RunDraw(EditorConn());

            Assert.False(payload.ContainsKey("editorSettings"));
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void DrawPayload_WireJson_RoundTripsEditorSettingsKey()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        ResetChains();
        try
        {
            var conn = EditorConn();
            var account = Account.Create("draw_wire_user", "pw", saltOverride: "salt");
            var raw = ValidSettings();
            raw["fontFamily"] = "WireFamily";
            raw["fontSize"] = 21;
            raw["roomVisible"] = false;
            Assert.True(MapEditorSettings.TryParse(raw, out var settings, out _));
            account.SetMapEditorSettings(settings!.ToJson());
            RunDraw(conn, account);

            var sent = conn.Sent.First(s => s.Cmd == "launch_draw");
            // Same envelope + options as WebSocketProtocol.SendCommand: this is
            // the exact byte shape the draw tab's JSON.parse sees.
            string wire = JsonSerializer.Serialize(
                new object?[] { sent.Cmd, sent.Args, sent.Kwargs },
                JsonSerializerOptions.Default);
            using var doc = JsonDocument.Parse(wire);
            Assert.Equal("launch_draw", doc.RootElement[0].GetString());
            var payload = doc.RootElement[1][1];
            Assert.True(payload.TryGetProperty("editorSettings", out var wireSettings),
                "wire JSON must carry editorSettings under the key the draw tab reads");
            Assert.Equal("WireFamily", wireSettings.GetProperty("fontFamily").GetString());
            Assert.Equal(21, wireSettings.GetProperty("fontSize").GetInt32());
            Assert.False(wireSettings.GetProperty("roomVisible").GetBoolean());
            Assert.Equal("gradient", wireSettings.GetProperty("tools").GetProperty("fillMode").GetString());
            Assert.Equal("█", wireSettings.GetProperty("selectedChar").GetString());
        }
        finally { ResetChains(); ObjectRegistry.ClearAll(); }
    }
}
