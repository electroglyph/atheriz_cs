using System.Collections.Frozen;
using InputHandler = System.Action<Atheriz.Core.Network.BaseConnection, System.Collections.Generic.List<object?>, System.Collections.Generic.Dictionary<string, object?>>;

namespace Atheriz.Core.Network;

/// <summary>
/// Legacy marker for input-func handlers — mirrors <c>atheriz/inputfuncs.py:64 @inputfunc</c>.
/// Discovery by attribute was removed: subclasses register extras explicitly
/// via <see cref="InputFuncs.RegisterExtraHandlers"/>. Kept as public API only.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InputFuncAttribute : Attribute
{
    public string? Name { get; }
    public InputFuncAttribute(string? name = null) => Name = name;
}

/// <summary>
/// Handles parsed JSON-RPC input messages from the client. Mirrors <c>atheriz/inputfuncs.py:211 InputFuncs</c>.
/// Methods in this class correspond to specific message commands sent by the client.
/// </summary>
public class InputFuncs
{
    public static Func<MapHandler> MapHandlerFactory = () => GlobalServices.GetMapHandler();
    public static Func<NodeHandler> NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();

    public Dictionary<string, InputHandler> GetHandlers()
    {
        // Single case-insensitive registry: each handler is stored under its
        // attribute name plus the method name when they differ (no triplication:
        // the derived lowercase form is covered by the comparer itself).
        var handlers = new Dictionary<string, InputHandler>(StringComparer.OrdinalIgnoreCase);
        // explicit registration list — the twelve known handlers bind
        // with no per-construction reflection (method-group delegates).
        // Subclasses add extras by overriding RegisterExtraHandlers and
        // calling AddInputHandler explicitly (no attribute scanning).
        AddInputHandler(handlers, "text", Text, nameof(Text));
        AddInputHandler(handlers, "term_size", TermSize, nameof(TermSize));
        AddInputHandler(handlers, "map_size", MapSize, nameof(MapSize));
        AddInputHandler(handlers, "screenreader", Screenreader, nameof(Screenreader));
        AddInputHandler(handlers, "client_ready", ClientReady, nameof(ClientReady));
        AddInputHandler(handlers, "map_edit", MapEditHandler, nameof(MapEditHandler));
        AddInputHandler(handlers, "map_validate_moves", MapValidateMovesHandler, nameof(MapValidateMovesHandler));
        AddInputHandler(handlers, "map_edit_legend", MapEditLegendHandler, nameof(MapEditLegendHandler));
        AddInputHandler(handlers, "map_edit_room", MapEditRoomHandler, nameof(MapEditRoomHandler));
        AddInputHandler(handlers, "map_edit_exits", MapEditExitsHandler, nameof(MapEditExitsHandler));
        AddInputHandler(handlers, "map_create_rooms", MapCreateRoomsHandler, nameof(MapCreateRoomsHandler));
        AddInputHandler(handlers, "map_delete_rooms", MapDeleteRoomsHandler, nameof(MapDeleteRoomsHandler));
        RegisterExtraHandlers(handlers);
        return handlers;
    }

    /// <summary>
    /// Registers one handler under its command name plus the method name when
    /// they differ (single case-insensitive registry; the derived lowercase
    /// form is covered by the comparer itself).
    /// </summary>
    protected static void AddInputHandler(Dictionary<string, InputHandler> handlers, string name, InputHandler del, string methodName)
    {
        handlers[name] = del;
        if (!name.Equals(methodName, StringComparison.OrdinalIgnoreCase)) handlers[methodName] = del;
    }

    /// <summary>
    /// Subclass hook for extra handlers. The base is a no-op: subclasses
    /// override it and call <see cref="AddInputHandler"/> explicitly.
    /// </summary>
    protected virtual void RegisterExtraHandlers(Dictionary<string, InputHandler> handlers)
    {
    }

// core command dispatch
    public void Text(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        try
        {
            var text = args.FirstOrDefault()?.ToString() ?? "";
            var session = connection.Session;
            // In Python, atp is get_async_threadpool(); here we use ConnectionManager's pool via global
            // Check-and-clear must be atomic: prompt owner and disconnect cleanup both touch input_future
            TaskCompletionSource<string>? future = null;
            bool masked = false;
            bool closed;
            // Single-section snapshot: closed/future/puppet must come
            // from one hold — the old split reads let AtDisconnect land
            // between them and route late input to ResolveUnloggedIn.
            Atheriz.Core.Objects.GameObject? puppet = null;
            lock (session.Lock)
            {
                closed = session.Closed;
                future = session.InputFuture;
                masked = session.InputMasked;
                if (future is not null && !future.Task.IsCompleted)
                {
                    session.InputFuture = null;
                    session.InputMasked = false;
                }
                else
                {
                    future = null;
                    masked = false;
                }
                puppet = session.Puppet;
            }
            // A session past AtDisconnect dispatches nothing: its puppet is
            // unwound and its teardown already ran, so late input is dropped
            // (mirrors DrainInput refusing closed sessions).
            if (closed) return;
            if (future is not null)
            {
                if (masked)
                {
                    try { connection.SendCommand("echo_on"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed InputFuncs.Text: " + logEx.Message, "InputFuncs"); }
                }
                try { future.TrySetResult(text); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed InputFuncs.Text: " + logEx.Message, "InputFuncs"); }
                return;
            }
            if (string.IsNullOrEmpty(text)) return;

            if (puppet is not null)
            {
                var job = Atheriz.Core.Commands.CommandDispatcher.DispatchLoggedIn(puppet, text, immediate: true);
                if (job is not null)
                {
                    // already on game worker via connection drain — execute inline instead of queueing second task
                    try { job.Func(job.Caller, job.Args); } catch (Exception ex) { try { Atheriz.Core.AtherizLogger.LogError($"Exception in text handler: {ex}"); } catch { Console.Error.WriteLine(ex); } }
                }
            }
            else
            {
                var job = Atheriz.Core.Commands.CommandDispatcher.ResolveUnloggedIn(connection, text);
                if (job is not null)
                {
                    try { job.Func(job.Caller, job.Args); } catch (Exception ex) { try { Atheriz.Core.AtherizLogger.LogError($"Exception in text handler: {ex}"); } catch { Console.Error.WriteLine(ex); } }
                }
            }
        }
        catch (Exception ex)
        {
            try { Atheriz.Core.AtherizLogger.LogError($"Exception in text handler: {ex}"); } catch { Console.Error.WriteLine($"Exception in text handler: {ex}"); }
        }
    }

    public void TermSize(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        var settings = AtherizSettings.Global;
        if (!TryCoerceSize(args, settings.TermSizeMaxWidth, settings.TermSizeMaxHeight, out var w, out var h)) return;
        connection.Session.TermWidth = w;
        connection.Session.TermHeight = h;
    }

    public void MapSize(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        var settings = AtherizSettings.Global;
        if (!TryCoerceSize(args, settings.MapSizeMaxWidth, settings.MapSizeMaxHeight, out var w, out var h)) return;
        connection.Session.MapWidth = w;
        connection.Session.MapHeight = h;
    }

    // Shared int coercion for the size handlers: JsonElement numbers arrive
    // from parsed JSON, int/long from in-process callers. Order matches the
    // old per-handler chains (JsonElement, then int, then long).
    private static bool TryCoerceInt(object? o, out int v)
    {
        switch (o)
        {
            case int iv:
                v = iv;
                return true;
            case long lv:
                // Narrowing without a range check wraps (e.g. 2^32+1 becomes
                // 1 and defeats the caller's lower-bound guard), so reject
                // values outside Int32 exactly like the JsonElement arm does.
                if (lv < int.MinValue || lv > int.MaxValue)
                {
                    v = 0;
                    return false;
                }
                v = (int)lv;
                return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Number:
                return je.TryGetInt32(out v);
            default:
                v = 0;
                return false;
        }
    }

    // Shared size coercion + bound check for TermSize/MapSize (differ only in
    // limits/targets). False preserves each caller's silent-return reject.
    private static bool TryCoerceSize(List<object?> args, int maxW, int maxH, out int w, out int h)
    {
        w = 0;
        h = 0;
        if (args.Count < 2) return false;
        if (!TryCoerceInt(args[0], out w)) return false;
        if (!TryCoerceInt(args[1], out h)) return false;
        return 0 < w && w <= maxW && 0 < h && h <= maxH;
    }

    public void Screenreader(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count > 0)
        {
            var value = args[0];
            bool enabled;
            if (value is bool b) enabled = b;
            else if (value is string s) enabled = s.ToLowerInvariant() == "true";
            else return;
            connection.Session.ScreenReader = enabled;
            connection.Msg($"Screenreader {(enabled ? "enabled" : "disabled")}.");
        }
    }

// Default command prompt: (re)asserted whenever a session enters
    // command mode (puppet attached). The welcome screen sets ">" for
    // fresh sockets, but token auto-logins skip it and menu prompts
    // (character select, passwords) replace it — without this the client
    // keeps showing the last menu prompt instead of ">".
    public static void SendDefaultPrompt(BaseConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try { connection.SendCommand("prompt", new List<object?> { ">" }, []); }
        catch (Exception) { }
    }

// prompt welcome screen
    public void ClientReady(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        // Token auto-login binds the account before the client's ready
        // signal arrives (it is sent on every socket open). Rendering the
        // welcome + "connect to login" screen there would land on top of
        // the character-selection prompt and look like the login never
        // happened; an authenticated session keeps the screen the
        // character wizard already owns.
        if (connection.Session.Account is not null) return;
        var welcome = ConnectionScreen.Render(connection.Session);
        connection.Msg(welcome);
        connection.SendCommand("prompt", new List<object?> { ">" }, []);
    }

    private static bool IsColor(object? v)
    {
        if (v is List<object?> lst && lst.Count==3)
        {
            // Positional ints are the only list shape: the all-int recheck
            // below used to repeat this, and a JsonElement whole-value check
            // can never match inside a proven-List branch (the whole-value
            // case has its own branch after this one).
            if (lst[0] is int a && lst[1] is int b && lst[2] is int c)
            {
                if (a==-1 && b==-1 && c==-1) return true;
                return a>=0 && a<=255 && b>=0 && b<=255 && c>=0 && c<=255;
            }
            return false;
        }
        if (v is System.Text.Json.JsonElement jel && jel.ValueKind==System.Text.Json.JsonValueKind.Array)
        {
            int jelLen = jel.GetArrayLength();
            int[] arr = new int[jelLen];
            int colorIdx = 0;
            foreach (var e in jel.EnumerateArray())
                arr[colorIdx++] = (e.ValueKind==System.Text.Json.JsonValueKind.Number && e.TryGetInt32(out var iv)) ? iv : -999;
            if (arr.Length!=3) return false;
            if (arr[0]==-1 && arr[1]==-1 && arr[2]==-1) return true;
            foreach (var x in arr)
                if (!(x>=0 && x<=255)) return false;
            return true;
        }
        return false;
    }
    // Single source of truth for the allowed text-attribute set. Membership
    // tests below go through this set; duplicate detection uses three bool
    // flags (duplicates reject, matching the old HashSet.Count/Distinct checks).
    private static readonly FrozenSet<string> AllowedAttrs = FrozenSet.ToFrozenSet<string>(["bold", "italic", "underline"]);

    private static bool IsAttrs(object? v)
    {
        if (v is List<object?> lst) {
            bool bold = false, italic = false, underline = false;
            foreach (var e in lst)
            {
                if (e is not string s || !AllowedAttrs.Contains(s)) return false;
                switch (s)
                {
                    case "bold": if (bold) return false; bold = true; break;
                    case "italic": if (italic) return false; italic = true; break;
                    case "underline": if (underline) return false; underline = true; break;
                    default: return false;
                }
            }
            return true;
        }
        if (v is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Array)
        {
            bool bold = false, italic = false, underline = false;
            foreach (var e in je.EnumerateArray())
            {
                // Same conversion as the old Select(GetString) path: null for
                // JSON null, throw for non-string numbers.
                var s = e.GetString();
                if (s is null || !AllowedAttrs.Contains(s)) return false;
                switch (s)
                {
                    case "bold": if (bold) return false; bold = true; break;
                    case "italic": if (italic) return false; italic = true; break;
                    case "underline": if (underline) return false; underline = true; break;
                    default: return false;
                }
            }
            return true;
        }
        return false;
    }
    private static Dictionary<string, object?>? NormalizeDict(object? v)
    {
        if (v is Dictionary<string, object?> d) return d;
        if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            Dictionary<string, object?> dict = [];
            foreach (var p in je.EnumerateObject()) dict[p.Name] = ConnectionManager.JsonElementToObject(p.Value);
            return dict;
        }
        return null;
    }

    private static bool IsLegendEntry(object? v)
    {
        if (NormalizeDict(v) is not { } dict) return false;
        if (!dict.TryGetValue("symbol", out var sym) || sym is not string symStr) return false;
        string visible;
        try { visible = GameUtils.StripAnsi(symStr); } catch { visible = symStr; }
        if (visible.Length==0 || visible.Length>2) return false;
        if (symStr.Length>64) return false;
        if (dict.TryGetValue("desc", out var desc) && desc is not null && desc is not string) return false;
        if (dict.TryGetValue("coord", out var coord) && coord is not null)
        {
            if (coord is List<object?> lst)
            {
                if (lst.Count!=2) return false;
                foreach (var x in lst)
                    // Cells accept int/long coords, so legend entries do too —
                    // but a long outside Int32 must reject, not wrap in ToInt.
                    if (x is not int && x is not long) return false;
                    else if (x is long ll && (ll < int.MinValue || ll > int.MaxValue)) return false;
            }
            else if (coord is System.Text.Json.JsonElement je2 && je2.ValueKind==System.Text.Json.JsonValueKind.Array)
            {
                var arr = je2.EnumerateArray().ToList();
                if (arr.Count!=2) return false;
                foreach (var e in arr)
                    if (e.ValueKind!=System.Text.Json.JsonValueKind.Number || !e.TryGetInt32(out _)) return false;
            }
            else return false;
        }
        if (dict.TryGetValue("show", out var show) && show is not null)
        {
            if (show is not bool) return false;
        }
        bool IsFg(object? fg) => IsRgb(fg, allowPartialTransparent: false);
        bool IsBg(object? bg) => IsRgb(bg, allowPartialTransparent: true);
        dict.TryGetValue("fg", out var fgVal);
        dict.TryGetValue("bg", out var bgVal);
        if (!IsFg(fgVal)) return false;
        if (!IsBg(bgVal)) return false;
        return true;
    }

    // Shared list-triple RGB check for the IsFg/IsBg legend validators. The
    // only difference is the -1 allowance (fg needs the full -1,-1,-1
    // transparent triple; bg allows -1 per component). The null/scalar/
    // JsonElement-number preamble is identical in both, so it merges too —
    // but IsColor stays separate: it rejects null/scalars and handles
    // JsonElement arrays, a different accept table.
    private static bool IsRgb(object? v, bool allowPartialTransparent)
    {
        if (v is null) return true;
        if (v is int || v is double || v is float) return true;
        if (v is System.Text.Json.JsonElement je && (je.ValueKind == System.Text.Json.JsonValueKind.Number || je.ValueKind == System.Text.Json.JsonValueKind.Null)) return true;
        if (v is List<object?> lst && lst.Count == 3)
        {
            foreach (var x in lst)
                if (x is not int) return false;
            int a = (int)lst[0]!; int b = (int)lst[1]!; int c = (int)lst[2]!;
            if (a == -1 && b == -1 && c == -1) return true;
            int lo = allowPartialTransparent ? -1 : 0;
            return a >= lo && a <= 255 && b >= lo && b <= 255 && c >= lo && c <= 255;
        }
        return false;
    }

    // Shared seq coercion for the map_edit family (map_edit,
    // map_validate_moves, map_edit_legend, map_edit_room, map_edit_exits, map_create_rooms, map_delete_rooms). Returns false on bad input; each
    // caller keeps its own failure path (silent return vs map_edit_reject).
    private static bool TryGetSeq(object? o, out int seq)
    {
        switch (o)
        {
            case int si:
                seq = si;
                return true;
            case long sl:
                // Same unchecked-narrowing shape as TryCoerceInt's long arm:
                // a seq that does not fit Int32 must be rejected, not
                // wrapped (the wrap would echo/look up the wrong chain).
                if (sl < int.MinValue || sl > int.MaxValue) { seq = 0; return false; }
                seq = (int)sl;
                return true;
            case System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var jsi):
                seq = jsi;
                return true;
            default:
                seq = 0;
                return false;
        }
    }

    private static int ToInt(object? o)
    {
        if (o is int i) return i;
        // Same unchecked-narrowing shape as TryCoerceInt: a long outside
        // Int32 must never wrap to a small coord (fail-closed throw; every
        // coordinate validation loop above rejects such values first, so
        // this is unreachable in normal flow and only fires on future misuse).
        if (o is long l) return checked((int)l);
        if (o is double d) return (int)d;
        if (o is string s && int.TryParse(s, out var iv)) return iv;
        if (o is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var jv)) return jv;
        return 0;
    }
    private static string? ToStr(object? o) => o?.ToString();
    private static List<object?> ToList(object? o)
    {
        if (o is List<object?> lst) return lst;
        if (o is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Array)
        {
            var converted = new List<object?>(je.GetArrayLength());
            foreach (var item in je.EnumerateArray())
                converted.Add(ConnectionManager.JsonElementToObject(item));
            return converted;
        }
        return [];
    }

    // Shared seq/key consume + reject-reply cycle for the map_edit family
    // (map_edit, map_validate_moves, map_edit_legend, map_edit_room, map_edit_exits, map_create_rooms, map_delete_rooms). Returns null after sending
    // map_edit_reject; otherwise the consume result for Retry-ack or processing.
    private static Globals.MapEditResult? ConsumeOrReply(BaseConnection connection, string? key, int seq)
    {
        string ip = connection.ClientHost ?? "?";
        var result = Globals.MapEdit.Consume(key!, ip, seq);
        if (result.Status == Globals.MapEditStatus.Reject)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ result.Reason }, []);
            // A dead key (grant→disconnect race) fails closed with no
            // hint — tell the user to reopen the editor. The wire reason is
            // unchanged for older clients.
            if (result.Reason == "unknown_key")
            {
                try { connection.Msg("The map editor session expired. Reopen the editor with the draw command."); } catch (Exception) { }
            }
            return null;
        }
        return result;
    }

    // Single-parse MapEdit cell: draw vs room discriminated, so the validate/
    // apply/roomMoves phases iterate this typed list instead of re-running
    // ToList (JsonElementToObject + int/string re-tests) per cell per phase.
    // Fg/Bg/Attrs stay raw for draw cells with style — the apply phase
    // decodes them exactly as before.
    private readonly record struct MapEditCell(
        bool IsRoom,
        int X,
        int Y,
        int Tx,
        int Ty,
        string Symbol,
        object? Fg,
        object? Bg,
        object? Attrs,
        bool HasStyle);

    // Validating parse pass: runs the exact validation sequence of the old
    // first traversal (same order, same first-failure return) and
    // materializes each passing cell. Room cells skip drawing exactly as
    // before (IsRoom; apply continues past them, roomMoves collects them).
    // The first failing cell index reports back so the caller can reject
    // loudly instead of dropping the edit silently.
    private static bool TryParseMapEditCells(List<object?> cells, out List<MapEditCell> parsed, out int failIndex)
    {
        parsed = new List<MapEditCell>(cells.Count);
        failIndex = -1;
        for (int idx = 0; idx < cells.Count; idx++)
        {
            var cell = ToList(cells[idx]);
            if (cell.Count == 0) { failIndex = idx; return false; }
            // Convert possible JsonElement string first element
            object? first = cell[0];
            if (first is System.Text.Json.JsonElement jef && jef.ValueKind == System.Text.Json.JsonValueKind.String) first = jef.GetString();
            if (first is string fs && fs == "room")
            {
                if (cell.Count != 5) { failIndex = idx; return false; }
                for (int i = 1; i < 5; i++)
                {
                    var v = cell[i];
                    if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number) { if (!je.TryGetInt32(out _)) { failIndex = idx; return false; } }
                    else if (v is not int && v is not long) { failIndex = idx; return false; }
                    else if (v is long lroom && (lroom < int.MinValue || lroom > int.MaxValue)) { failIndex = idx; return false; }
                }
                parsed.Add(new MapEditCell(true, ToInt(cell[1]), ToInt(cell[2]), ToInt(cell[3]), ToInt(cell[4]), "", null, null, null, false));
                continue;
            }
            if (cell.Count != 3 && cell.Count != 6) { failIndex = idx; return false; }
            // first two must be int
            for (int i = 0; i < 2; i++)
            {
                var v = cell[i];
                if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number) { if (!je.TryGetInt32(out _)) { failIndex = idx; return false; } }
                else if (v is not int && v is not long) { failIndex = idx; return false; }
                else if (v is long lxy && (lxy < int.MinValue || lxy > int.MaxValue)) { failIndex = idx; return false; }
            }
            if (cell[2] is not string)
            {
                if (cell[2] is System.Text.Json.JsonElement je3 && je3.ValueKind == System.Text.Json.JsonValueKind.String) { } else { failIndex = idx; return false; }
            }
            if (cell.Count == 6)
            {
                if (!IsColor(cell[3]) || !IsColor(cell[4]) || !IsAttrs(cell[5])) { failIndex = idx; return false; }
            }
            string sym = cell[2] is string ss ? ss : (cell[2] is System.Text.Json.JsonElement je4 && je4.ValueKind == System.Text.Json.JsonValueKind.String ? je4.GetString() ?? "" : "");
            parsed.Add(cell.Count == 3
                ? new MapEditCell(false, ToInt(cell[0]), ToInt(cell[1]), 0, 0, sym, null, null, null, false)
                : new MapEditCell(false, ToInt(cell[0]), ToInt(cell[1]), 0, 0, sym, cell[3], cell[4], cell[5], true));
        }
        return true;
    }

    public void MapEditHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3) return;
        var key = args[0] as string;
        object? seqObj = args[1];
        var cellsObj = args[2];
        if (key is null || cellsObj is null) return;
        // seq must be int
        if (!TryGetSeq(seqObj, out var seq)) return;
        // key is a string here (null returned above), so only the cells shape
        // still needs checking.
        if (cellsObj is not List<object?> && cellsObj is not System.Text.Json.JsonElement) {
            // try to normalize cells via ToList? For JsonElement list it will be list of JsonElements -> still allowed but need validation path
            if (cellsObj is System.Text.Json.JsonElement je2 && je2.ValueKind==System.Text.Json.JsonValueKind.Array) { }
            else return;
        }
        var cells = ToList(cellsObj);
        // Single validating parse (replaces the old validate traversal); the
        // apply and roomMoves phases below iterate the typed list.
        if (!TryParseMapEditCells(cells, out var parsed, out var failIndex))
        {
            // A failing cell must reject loudly like a failing legend entry —
            // silently dropping the whole edit hides validation disagreements
            // (e.g. scalar cell colors, which the cell gate rejects by design).
            connection.SendCommand("map_edit_reject", new List<object?> { $"Invalid map edit cell at index {failIndex}." }, []);
            return;
        }
        // Optional editor-settings fourth arg (explicit save-to-server only):
        // validated before consume so a bad payload never burns a seq, and
        // rejected loudly like bad cells/legend entries. Absent (or JSON
        // null) means an autosync or a settings-less save — nothing to store.
        MapEditorSettings? editorSettings = null;
        if (args.Count >= 4 && args[3] is not null)
        {
            if (!MapEditorSettings.TryParse(args[3], out editorSettings, out var settingsError))
            {
                connection.SendCommand("map_edit_reject", new List<object?> { $"Invalid map editor settings: {settingsError}" }, []);
                return;
            }
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        if (result.Status == Globals.MapEditStatus.Retry)
        {
            connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, []);
            return;
        }
        // Process edits
        var mh = MapHandlerFactory();
        var mi = mh.GetMapInfo(result.Chain!.Area, result.Chain.Z);
        if (mi is not null)
        {
            using (mi.BatchUpdate())
            {
                foreach (var cell in parsed)
                {
                    if (cell.IsRoom) continue;
                    int x = cell.X; int y = cell.Y;
                    string sym = cell.Symbol;
                    if (sym=="") mi.RemovePreCell((x,y));
                    else if (!cell.HasStyle) mi.SetPreCell((x,y), sym);
                    else
                    {
                        var fg = cell.Fg; var bg = cell.Bg; var attrs = cell.Attrs;
                        // decode fg/bg
                        (byte R,byte G,byte B)? fgT=null; (byte R,byte G,byte B)? bgT=null;
                        List<object?> fgList = ToList(fg); List<object?> bgList = ToList(bg);
                        bool fgTrans = fgList.Count==3;
                        if (fgTrans)
                        {
                            foreach (var e in fgList)
                            {
                                if (ToInt(e)!=-1) { fgTrans = false; break; }
                            }
                        }
                        bool bgTrans = bgList.Count==3;
                        if (bgTrans)
                        {
                            foreach (var e in bgList)
                            {
                                if (ToInt(e)!=-1) { bgTrans = false; break; }
                            }
                        }
                        if (!fgTrans && fgList.Count==3) fgT = ((byte)ToInt(fgList[0]), (byte)ToInt(fgList[1]), (byte)ToInt(fgList[2]));
                        if (!bgTrans && bgList.Count==3) bgT = ((byte)ToInt(bgList[0]), (byte)ToInt(bgList[1]), (byte)ToInt(bgList[2]));
                        var attrList = ToList(attrs);
                        bool bold = false, italic = false, underline = false;
                        foreach (var a in attrList)
                        {
                            if ((a is string s && s=="bold") || (a is System.Text.Json.JsonElement je && je.GetString()=="bold")) bold = true;
                            if ((a is string s2 && s2=="italic") || (a is System.Text.Json.JsonElement je2 && je2.GetString()=="italic")) italic = true;
                            if ((a is string s3 && s3=="underline") || (a is System.Text.Json.JsonElement je3 && je3.GetString()=="underline")) underline = true;
                        }
                        string wrapped = GameUtils.WrapRgb(sym, fgT, bgT, bold, italic, underline);
                        mi.SetPreCell((x,y), wrapped);
                    }
                }
                mi.MapChanged = true;
            }
        }
        List<((int X,int Y) src,(int X,int Y) dst)> roomMoves = [];
        foreach (var cell in parsed)
        {
            if (cell.IsRoom)
            {
                roomMoves.Add(((cell.X, cell.Y), (cell.Tx, cell.Ty)));
            }
        }
        if (roomMoves.Count>0)
        {
            var nh = NodeHandlerFactory();
            var areaObj = nh.GetArea(result.Chain.Area);
            var grid = areaObj?.GetGrid(result.Chain.Z);
            if (grid is not null)
            {
                var failed = grid.ApplyMoves(roomMoves);
                // already validated — a refusal here means a cross-message
                // validate->apply TOCTOU or a stale chain, worth one line).
                // Surface the refusal to the client as moves_denied
                // (the draw editor rolls denied moves back) instead of only
                // logging — otherwise the client keeps showing a move the
                // server never applied (silent partial apply).
                if (failed.Count > 0)
                {
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[MapEdit] ApplyMoves refused {failed.Count}/{roomMoves.Count} moves on {result.Chain.Area} z={result.Chain.Z}: {string.Join(";", failed.Take(5))}"); }
                    catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.MapEditHandler: " + logEx.Message, "ConnectionManager"); }
                    try { connection.SendCommand("moves_denied", new List<object?> { seq, result.NewKey, failed }, []); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.MapEditHandler: " + logEx.Message, "ConnectionManager"); }
                }
            }
        }
        if (editorSettings is not null)
        {
            // The editor socket is unauthenticated, so the settings belong to
            // the granting (game) session's account, not this connection's.
            // Only Processed reaches here: retries ack without re-saving, and
            // a missing account (tests, logged-out grant) ack without storing.
            result.Chain!.Session?.Account?.SetMapEditorSettings(editorSettings.ToJson());
        }
        connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, []);
    }

    public void MapValidateMovesHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3 || args.Count > 4) return;
        var key = args[0] as string;
        object? seqObj = args[1];
        var movesObj = args[2];
        if (key is null || movesObj is null) return;
        if (!TryGetSeq(seqObj, out var seq)) return;
        var movesList = ToList(movesObj);
        foreach (var mObj in movesList)
        {
            var m = ToList(mObj);
            if (m.Count!=4) return;
            for(int i=0;i<4;i++) {
                var v=m[i];
                if (v is System.Text.Json.JsonElement jeM && jeM.ValueKind==System.Text.Json.JsonValueKind.Number) { if (!jeM.TryGetInt32(out _)) return; }
                else if (v is not int && v is not long) return;
                else if (v is long lm && (lm < int.MinValue || lm > int.MaxValue)) return;
            }
        }
        List<((int X,int Y) src,(int X,int Y) dst)>? context=null;
        if (args.Count==4)
        {
            var ctxArg = args[3];
            if (ctxArg is not List<object?> && !(ctxArg is System.Text.Json.JsonElement jeCtx && jeCtx.ValueKind==System.Text.Json.JsonValueKind.Array)) return;
            var ctxList = ToList(ctxArg);
            context = [];
            foreach (var ctxObj in ctxList)
            {
                var ctx = ToList(ctxObj);
                if (ctx.Count!=4) return;
                for(int i=0;i<4;i++) {
                    var v=ctx[i];
                    if (v is System.Text.Json.JsonElement jeC && jeC.ValueKind==System.Text.Json.JsonValueKind.Number) { if (!jeC.TryGetInt32(out _)) return; }
                    else if (v is not int && v is not long) return;
                    else if (v is long lc && (lc < int.MinValue || lc > int.MaxValue)) return;
                }
                context.Add(((ToInt(ctx[0]), ToInt(ctx[1])), (ToInt(ctx[2]), ToInt(ctx[3]))));
            }
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        if (result.Status == Globals.MapEditStatus.Retry)
        {
            SendMoveVerdict(connection, seq, result.NewKey!, result.Chain!.Validation ?? []);
            return;
        }
        var nh2 = NodeHandlerFactory();
        var areaObj2 = nh2.GetArea(result.Chain!.Area);
        var grid2 = areaObj2?.GetGrid(result.Chain.Z);
        List<int> denied;
        if (grid2 is null) denied = Enumerable.Range(0, movesList.Count).ToList();
        else
        {
            var moves = new List<((int, int), (int, int))>(movesList.Count);
            foreach (var mObj in movesList)
            {
                var m = ToList(mObj);
                moves.Add(((ToInt(m[0]), ToInt(m[1])), (ToInt(m[2]), ToInt(m[3]))));
            }
            var failed = grid2.CheckMoves(moves, context);
            denied = failed.OrderBy(x=>x).ToList();
        }
        Globals.MapEdit.SetValidation(result.NewKey!, denied);
        SendMoveVerdict(connection, seq, result.NewKey!, denied);
    }

    private void SendMoveVerdict(BaseConnection connection, int seq, string newKey, List<int> denied)
    {
        if (denied.Count>0) connection.SendCommand("moves_denied", new List<object?>{ seq, newKey, denied }, []);
        else connection.SendCommand("moves_ok", new List<object?>{ seq, newKey }, []);
    }

    public void MapEditLegendHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid legend payload." }, []);
            return;
        }
        var key = args[0] as string;
        object? seqObj = args[1];
        var legendObj = args[2];
        if (key is null || legendObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid legend payload." }, []);
            return;
        }
        if (!TryGetSeq(seqObj, out var seq)) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid legend payload." }, []); return; }
        var legend = ToList(legendObj);
        if (legend.Count>200) { connection.SendCommand("map_edit_reject", new List<object?>{ "Too many legend entries (max 200)." }, []); return; }
        // Single validating parse: normalize once per entry, in index order
        // with first-failure reject, and share the dicts with the apply phase.
        var parsedLegend = new List<Dictionary<string, object?>>(legend.Count);
        for(int idx=0; idx<legend.Count; idx++)
        {
            var dict = NormalizeDict(legend[idx]);
            if (dict is null || !IsLegendEntry(dict))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid legend entry at index {idx}." }, []);
                return;
            }
            parsedLegend.Add(dict);
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        if (result.Status == Globals.MapEditStatus.Retry)
        {
            connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, []);
            // legacy also sends legend_ok? Python for retry only sends map_ack (no legend_ok). Check python: for retry it does connection.send_command("map_ack", seq, new_key) return (no legend_ok). So only ack.
            return;
        }
        var mh = MapHandlerFactory();
        var mi = mh.GetMapInfo(result.Chain!.Area, result.Chain.Z);
        if (mi is null)
        {
            mi = new MapInfo(result.Chain.Area);
            // atomic publish — concurrent creators converge on the
            // stored winner; a locally built loser is dropped, not last-wins.
            mi = mh.GetOrAddMapInfo(result.Chain.Area, result.Chain.Z, mi);
        }
        List<LegendEntry> newEntries = new(parsedLegend.Count);
        foreach (var dict in parsedLegend)
        {
            var le = new LegendEntry();
            le.Symbol = dict.TryGetValue("symbol", out var sy) ? sy as string : null;
            var desc = dict.TryGetValue("desc", out var de) ? de : null;
            if (desc is null) le.Desc = "";
            else le.Desc = desc as string ?? "";
            if (dict.TryGetValue("coord", out var co) && co is not null)
            {
                if (co is List<object?> lst && lst.Count>=2) le.Coord = (ToInt(lst[0]), ToInt(lst[1]));
                else if (co is System.Text.Json.JsonElement je2 && je2.ValueKind==System.Text.Json.JsonValueKind.Array)
                {
                    int coordLen = je2.GetArrayLength();
                    int[] coordArr = new int[coordLen];
                    int coordIdx = 0;
                    foreach (var x in je2.EnumerateArray())
                        coordArr[coordIdx++] = x.TryGetInt32(out var iv) ? iv : 0;
                    if (coordArr.Length>=2) le.Coord=(coordArr[0],coordArr[1]);
                }
            }
            else le.Coord=null;
            le.Show = dict.TryGetValue("show", out var sh) && sh is bool sb ? sb : true;
            if (dict.TryGetValue("fg", out var fg) && fg is not null)
            {
                // A validated [r,g,b] triple keeps its RGB form (the scalar
                // hue stays default); scalars fill Fg as before.
                if (fg is List<object?> fgList && fgList.Count == 3)
                    le.FgRgb = [ToInt(fgList[0]), ToInt(fgList[1]), ToInt(fgList[2])];
                else if (fg is double dd) le.Fg=dd;
                else if (fg is float ff) le.Fg=ff;
                else if (fg is int ii) le.Fg=ii;
                else if (fg is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var dv)) le.Fg=dv;
                else le.Fg=170.0;
            }
            if (dict.TryGetValue("bg", out var bg) && bg is not null)
            {
                if (bg is List<object?> bgList && bgList.Count == 3)
                    le.BgRgb = [ToInt(bgList[0]), ToInt(bgList[1]), ToInt(bgList[2])];
                else if (bg is double db) le.Bg=db;
                else if (bg is float fb) le.Bg=fb;
                else if (bg is int ib) le.Bg=ib;
                else if (bg is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var dv)) le.Bg=dv;
                else le.Bg=null;
            }
            newEntries.Add(le);
        }
        mi.ReplaceLegendEntries(newEntries);
        mi.RenderLegend();
        connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, []);
        connection.SendCommand("legend_ok", new List<object?>{ seq, result.NewKey }, []);
    }

    // Room-coordinate coercion for map_edit_room: int/long/JSON-number only,
    // range-checked like the moves validator (a long outside Int32 rejects,
    // never wraps to a wrong room).
    private static bool TryGetRoomCoord(Dictionary<string, object?> room, string field, out int value)
    {
        value = 0;
        if (!room.TryGetValue(field, out var o) || o is null) return false;
        if (o is int i) { value = i; return true; }
        if (o is long l)
        {
            if (l < int.MinValue || l > int.MaxValue) return false;
            value = (int)l;
            return true;
        }
        if (o is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var jv))
        {
            value = jv;
            return true;
        }
        return false;
    }

    // Room-name/desc coercion for map_edit_room: an absent or null field
    // leaves the node property unchanged; a present field must be a string
    // within its length cap. Returns false (with the caller's reject already
    // sent) on a wrong-typed or overlong value.
    private static bool TakeRoomText(BaseConnection connection, Dictionary<string, object?> room, string field, int maxLength, out string? value)
    {
        value = null;
        if (!room.TryGetValue(field, out var o) || o is null) return true;
        if (o is not string s || s.Length > maxLength)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid room {field}." }, []);
            return false;
        }
        value = s;
        return true;
    }

    public void MapEditRoomHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room payload." }, []);
            return;
        }
        var key = args[0] as string;
        object? seqObj = args[1];
        var roomObj = args[2];
        if (key is null || roomObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room payload." }, []);
            return;
        }
        if (!TryGetSeq(seqObj, out var seq)) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room payload." }, []); return; }
        var room = NormalizeDict(roomObj);
        if (room is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room payload." }, []); return; }
        if (!TryGetRoomCoord(room, "x", out var x) || !TryGetRoomCoord(room, "y", out var y))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room coordinates." }, []);
            return;
        }
        if (!TakeRoomText(connection, room, "name", 200, out var name)) return;
        if (!TakeRoomText(connection, room, "desc", 8000, out var desc)) return;
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        // One verdict path for Processed and Retry alike: a duplicate
        // delivery replays the verdict instead of blind-acking. Re-applying
        // identical values is idempotent (and a room deleted since still
        // denies), so a retry can never report a save that did not land —
        // the moves handler replays its stored verdict the same way.
        var node = NodeHandlerFactory().GetArea(result.Chain!.Area)?.GetGrid(result.Chain.Z)?.GetNode(x, y);
        if (node is null)
        {
            // The key is already consumed: rotate it like moves_denied so the
            // session survives (map_edit_reject would kill the client).
            connection.SendCommand("room_denied", new List<object?>{ seq, result.NewKey, $"No room at ({x}, {y})." }, []);
            return;
        }
        // Plain Node.Name stays coord-derived by pin; the editable room
        // name is DisplayName. Desc always applies.
        if (name is not null) node.DisplayName = name;
        if (desc is not null) node.Desc = desc;
        connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, []);
        connection.SendCommand("room_ok", new List<object?>{ seq, result.NewKey }, []);
    }

    // Single exit validation for map_edit_exits: name/aliases statics, coord
    // triple. Returns the parsed link or null after sending the loud reject.
    private bool TryParseExit(BaseConnection connection, object? exitObj, int index, out Objects.NodeLink? link)
    {
        link = null;
        var dict = NormalizeDict(exitObj);
        if (dict is null) { connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit at index {index}." }, []); return false; }
        if (!dict.TryGetValue("name", out var nameObj) || nameObj is not string exitName || exitName.Length == 0 || exitName.Length > 64)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit name at index {index}." }, []);
            return false;
        }
        List<string> aliases = [];
        if (dict.TryGetValue("aliases", out var aliasesObj) && aliasesObj is not null)
        {
            var aliasList = ToList(aliasesObj);
            if (aliasesObj is not List<object?> && !(aliasesObj is System.Text.Json.JsonElement jeArr && jeArr.ValueKind == System.Text.Json.JsonValueKind.Array))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit aliases at index {index}." }, []);
                return false;
            }
            if (aliasList.Count > 16)
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Too many exit aliases at index {index} (max 16)." }, []);
                return false;
            }
            foreach (var a in aliasList)
            {
                if (a is not string alias || alias.Length == 0 || alias.Length > 32)
                {
                    connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit alias at index {index}." }, []);
                    return false;
                }
                aliases.Add(alias);
            }
        }
        if (!dict.TryGetValue("coord", out var coordObj) || coordObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit coord at index {index}." }, []);
            return false;
        }
        var coordList = ToList(coordObj);
        if (coordList.Count != 4)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit coord at index {index}." }, []);
            return false;
        }
        var area = coordList[0] as string;
        if (string.IsNullOrEmpty(area) || area.Length > 64)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit coord at index {index}." }, []);
            return false;
        }
        if (!TryCoerceExitInt(coordList[1], out var cx) || !TryCoerceExitInt(coordList[2], out var cy) || !TryCoerceExitInt(coordList[3], out var cz))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid exit coord at index {index}." }, []);
            return false;
        }
        link = new Objects.NodeLink(exitName, new Coord(area, cx, cy, cz), aliases);
        return true;
    }

    // Exit-coord int coercion: int/long/JSON-number only, range-checked
    // (a long outside Int32 rejects, never wraps to a wrong room).
    private static bool TryCoerceExitInt(object? o, out int value)
    {
        value = 0;
        if (o is int i) { value = i; return true; }
        if (o is long l)
        {
            if (l < int.MinValue || l > int.MaxValue) return false;
            value = (int)l;
            return true;
        }
        if (o is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var jv))
        {
            value = jv;
            return true;
        }
        return false;
    }

    public void MapEditExitsHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid exits payload." }, []);
            return;
        }
        var key = args[0] as string;
        object? seqObj = args[1];
        var roomObj = args[2];
        if (key is null || roomObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid exits payload." }, []);
            return;
        }
        if (!TryGetSeq(seqObj, out var seq)) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid exits payload." }, []); return; }
        var room = NormalizeDict(roomObj);
        if (room is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid exits payload." }, []); return; }
        if (!TryGetRoomCoord(room, "x", out var x) || !TryGetRoomCoord(room, "y", out var y))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room coordinates." }, []);
            return;
        }
        if (!room.TryGetValue("exits", out var exitsObj) || exitsObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid exits payload." }, []);
            return;
        }
        var exitList = ToList(exitsObj);
        if (exitsObj is not List<object?> && !(exitsObj is System.Text.Json.JsonElement jeEx && jeEx.ValueKind == System.Text.Json.JsonValueKind.Array))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid exits payload." }, []);
            return;
        }
        if (exitList.Count > 64)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Too many exits (max 64)." }, []);
            return;
        }
        List<Objects.NodeLink> parsed = new(exitList.Count);
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        for (int idx = 0; idx < exitList.Count; idx++)
        {
            if (!TryParseExit(connection, exitList[idx], idx, out var link)) return;
            if (!seenNames.Add(link!.Name))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Duplicate exit name '{link.Name}'." }, []);
                return;
            }
            parsed.Add(link);
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        ApplyExitsVerdict(connection, seq, result.NewKey!, result.Chain!.Area, result.Chain.Z, x, y, parsed);
    }

    // Shared verdict for fresh and retried map_edit_exits: missing room or
    // missing target denies with a rotated key (session survives); otherwise
    // the full list replaces the room's links (remove-all then add, so
    // renames and coord rewrites apply atomically) and acks map_ack +
    // exits_ok. Re-applying the identical list is idempotent, so duplicate
    // delivery replays the verdict instead of inventing a save.
    private void ApplyExitsVerdict(BaseConnection connection, int seq, string newKey, string area, int z, int x, int y, List<Objects.NodeLink> parsed)
    {
        var nh = NodeHandlerFactory();
        var node = nh.GetArea(area)?.GetGrid(z)?.GetNode(x, y);
        if (node is null)
        {
            connection.SendCommand("exits_denied", new List<object?>{ seq, newKey, $"No room at ({x}, {y})." }, []);
            return;
        }
        foreach (var link in parsed)
        {
            var target = nh.GetArea(link.Coord.Area)?.GetGrid(link.Coord.Z)?.GetNode(link.Coord.X, link.Coord.Y);
            if (target is null)
            {
                connection.SendCommand("exits_denied", new List<object?>{ seq, newKey, $"No room at {link.Coord} for exit '{link.Name}'." }, []);
                return;
            }
        }
        foreach (var old in node.GetLinks()) node.RemoveLink(old.Name);
        foreach (var link in parsed) node.AddLink(new Objects.NodeLink(link.Name, link.Coord, link.Aliases));
        connection.SendCommand("map_ack", new List<object?>{ seq, newKey }, []);
        connection.SendCommand("exits_ok", new List<object?>{ seq, newKey }, []);
    }

    // Room-creation batch for map_create_rooms: payload
    // {rooms:[{x,y,name?,desc?}], exits:[{x,y,exits:[...]}]}. Shape errors
    // fail loud (map_edit_reject, session dies — same as the sibling
    // editors); semantic errors deny with a rotated key (session survives).
    // Validation runs fully before any mutation, so a deny never leaves a
    // half-created batch behind.
    public void MapCreateRoomsHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []);
            return;
        }
        var key = args[0] as string;
        object? seqObj = args[1];
        var payloadObj = args[2];
        if (key is null || payloadObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []);
            return;
        }
        if (!TryGetSeq(seqObj, out var seq)) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []); return; }
        var payload = NormalizeDict(payloadObj);
        if (payload is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []); return; }
        if (!payload.TryGetValue("rooms", out var roomsObj) || roomsObj is null
            || (roomsObj is not List<object?> && !(roomsObj is System.Text.Json.JsonElement jeRooms && jeRooms.ValueKind == System.Text.Json.JsonValueKind.Array)))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []);
            return;
        }
        var roomList = ToList(roomsObj);
        if (roomList.Count == 0)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []);
            return;
        }
        if (roomList.Count > 256)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Too many rooms (max 256)." }, []);
            return;
        }
        List<(int x, int y, string? name, string? desc)> rooms = new(roomList.Count);
        HashSet<(int, int)> seenRooms = [];
        for (int idx = 0; idx < roomList.Count; idx++)
        {
            var dict = NormalizeDict(roomList[idx]);
            if (dict is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []); return; }
            if (!TryGetRoomCoord(dict, "x", out var x) || !TryGetRoomCoord(dict, "y", out var y))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room coordinates." }, []);
                return;
            }
            if (!seenRooms.Add((x, y)))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Duplicate room at ({x}, {y})." }, []);
                return;
            }
            if (!TakeRoomText(connection, dict, "name", 200, out var name)) return;
            if (!TakeRoomText(connection, dict, "desc", 8000, out var desc)) return;
            rooms.Add((x, y, name, desc));
        }
        if (!payload.TryGetValue("exits", out var exitsObj) || exitsObj is null
            || (exitsObj is not List<object?> && !(exitsObj is System.Text.Json.JsonElement jeEx && jeEx.ValueKind == System.Text.Json.JsonValueKind.Array)))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []);
            return;
        }
        var exitsList = ToList(exitsObj);
        List<(int x, int y, List<Objects.NodeLink> links)> exits = new(exitsList.Count);
        HashSet<(int, int)> seenExits = [];
        for (int idx = 0; idx < exitsList.Count; idx++)
        {
            var dict = NormalizeDict(exitsList[idx]);
            if (dict is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []); return; }
            if (!TryGetRoomCoord(dict, "x", out var x) || !TryGetRoomCoord(dict, "y", out var y))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room coordinates." }, []);
                return;
            }
            if (!seenExits.Add((x, y)))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Duplicate exits for room ({x}, {y})." }, []);
                return;
            }
            if (!dict.TryGetValue("exits", out var roomExitsObj) || roomExitsObj is null
                || (roomExitsObj is not List<object?> && !(roomExitsObj is System.Text.Json.JsonElement jeRe && jeRe.ValueKind == System.Text.Json.JsonValueKind.Array)))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid create payload." }, []);
                return;
            }
            var roomExitList = ToList(roomExitsObj);
            if (roomExitList.Count > 64)
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ "Too many exits (max 64)." }, []);
                return;
            }
            List<Objects.NodeLink> parsed = new(roomExitList.Count);
            HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
            for (int eidx = 0; eidx < roomExitList.Count; eidx++)
            {
                if (!TryParseExit(connection, roomExitList[eidx], eidx, out var link)) return;
                if (!seenNames.Add(link!.Name))
                {
                    connection.SendCommand("map_edit_reject", new List<object?>{ $"Duplicate exit name '{link.Name}'." }, []);
                    return;
                }
                parsed.Add(link);
            }
            exits.Add((x, y, parsed));
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        ApplyCreateVerdict(connection, seq, result.NewKey!, result.Chain!.Area, result.Chain.Z, rooms, exits);
    }

    // Shared verdict for fresh and retried map_create_rooms: every exit
    // entry and target must resolve against the post-creation world
    // (existing rooms plus this batch) before anything mutates, so a deny
    // reports with a rotated key and no partial apply. Missing rooms are
    // created (existing coords are skipped, making duplicate delivery an
    // idempotent replay like the room/exits verdicts), exit replacements
    // apply remove-all then add, and success acks map_ack + create_ok.
    private void ApplyCreateVerdict(BaseConnection connection, int seq, string newKey, string area, int z,
        List<(int x, int y, string? name, string? desc)> rooms, List<(int x, int y, List<Objects.NodeLink> links)> exits)
    {
        var nh = NodeHandlerFactory();
        HashSet<(int, int)> creates = new(rooms.Select(r => (r.x, r.y)));
        bool roomKnown(int x, int y) =>
            creates.Contains((x, y)) || nh.GetArea(area)?.GetGrid(z)?.GetNode(x, y) is not null;
        foreach (var (x, y, _) in exits)
        {
            if (!roomKnown(x, y))
            {
                connection.SendCommand("create_denied", new List<object?>{ seq, newKey, $"No room at ({x}, {y})." }, []);
                return;
            }
        }
        foreach (var (_, _, links) in exits)
        {
            foreach (var link in links)
            {
                var t = link.Coord;
                var target = nh.GetArea(t.Area)?.GetGrid(t.Z)?.GetNode(t.X, t.Y);
                if (target is null && !(t.Area == area && t.Z == z && creates.Contains((t.X, t.Y))))
                {
                    connection.SendCommand("create_denied", new List<object?>{ seq, newKey, $"No room at {link.Coord} for exit '{link.Name}'." }, []);
                    return;
                }
            }
        }
        foreach (var (x, y, name, desc) in rooms)
        {
            var node = nh.GetArea(area)?.GetGrid(z)?.GetNode(x, y);
            if (node is null)
            {
                node = new Objects.Node(new Coord(area, x, y, z), desc: desc ?? "New room.");
                nh.AddNode(node);
            }
            else if (desc is not null)
            {
                node.Desc = desc;
            }
            if (name is not null) node.DisplayName = name;
        }
        foreach (var (x, y, links) in exits)
        {
            var node = nh.GetArea(area)?.GetGrid(z)?.GetNode(x, y);
            if (node is null) continue;
            foreach (var old in node.GetLinks()) node.RemoveLink(old.Name);
            foreach (var link in links) node.AddLink(new Objects.NodeLink(link.Name, link.Coord, link.Aliases));
        }
        // New rooms join the in-game map the way dig draws them: without a
        // grid cell a room falls outside the rendered bounds and the
        // character symbol has nowhere to land. Empty cells only — drawn
        // art is never overwritten. Auxiliary to the ack (rooms and exits
        // already applied), so a map failure is logged, never fatal.
        try
        {
            var mi = MapHandlerFactory().EnsureMapInfo(area, z);
            using (mi.BatchUpdate())
            {
                var roomPH = AtherizSettings.Global.RoomPlaceholder;
                foreach (var (x, y, _, _) in rooms)
                    mi.SetPreCellIfAbsent((x, y), roomPH);
            }
        }
        catch (Exception mapEx) { AtherizLogger.LogDebug("Suppressed MapCreateRoomsHandler map stamp: " + mapEx.Message, "InputFuncs"); }
        connection.SendCommand("map_ack", new List<object?>{ seq, newKey }, []);
        connection.SendCommand("create_ok", new List<object?>{ seq, newKey }, []);
    }

    // Room-deletion batch for map_delete_rooms: payload
    // {rooms:[{x,y,fallback?:{x,y}}]}. Shape errors fail loud
    // (map_edit_reject, session dies — same as the sibling editors);
    // semantic errors deny with a rotated key (session survives).
    // Missing rooms are skipped, making duplicate delivery an idempotent
    // replay like the create verdict; everything else validates fully
    // before any mutation, so a deny never leaves a half-deleted batch
    // behind except rooms already evacuated and removed one by one (each
    // a complete deletion on its own — retry converges via skip-missing).
    public void MapDeleteRoomsHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []);
            return;
        }
        var key = args[0] as string;
        object? seqObj = args[1];
        var payloadObj = args[2];
        if (key is null || payloadObj is null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []);
            return;
        }
        if (!TryGetSeq(seqObj, out var seq)) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []); return; }
        var payload = NormalizeDict(payloadObj);
        if (payload is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []); return; }
        if (!payload.TryGetValue("rooms", out var roomsObj) || roomsObj is null
            || (roomsObj is not List<object?> && !(roomsObj is System.Text.Json.JsonElement jeRooms && jeRooms.ValueKind == System.Text.Json.JsonValueKind.Array)))
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []);
            return;
        }
        var roomList = ToList(roomsObj);
        if (roomList.Count == 0)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []);
            return;
        }
        if (roomList.Count > 256)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Too many rooms (max 256)." }, []);
            return;
        }
        List<(int x, int y, int fx, int fy, bool hasFallback)> rooms = new(roomList.Count);
        HashSet<(int, int)> seenRooms = [];
        for (int idx = 0; idx < roomList.Count; idx++)
        {
            var dict = NormalizeDict(roomList[idx]);
            if (dict is null) { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid delete payload." }, []); return; }
            if (!TryGetRoomCoord(dict, "x", out var x) || !TryGetRoomCoord(dict, "y", out var y))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid room coordinates." }, []);
                return;
            }
            if (!seenRooms.Add((x, y)))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Duplicate room at ({x}, {y})." }, []);
                return;
            }
            int fx = 0, fy = 0;
            bool hasFallback = false;
            if (dict.TryGetValue("fallback", out var fallbackObj) && fallbackObj is not null)
            {
                var fallback = NormalizeDict(fallbackObj);
                if (fallback is null || !TryGetRoomCoord(fallback, "x", out fx) || !TryGetRoomCoord(fallback, "y", out fy))
                {
                    connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid fallback coordinates." }, []);
                    return;
                }
                hasFallback = true;
            }
            rooms.Add((x, y, fx, fy, hasFallback));
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result is null) return;
        ApplyDeleteVerdict(connection, seq, result.NewKey!, result.Chain!.Area, result.Chain.Z, rooms);
    }

    // Shared verdict for fresh and retried map_delete_rooms: missing rooms
    // are skipped (idempotent replay), but door endpoints and fallback
    // shape deny with a rotated key before anything mutates. Occupied rooms
    // evacuate to their fallback (bounded sweeps, fail closed like the door
    // placement); rooms without a usable fallback deny instead of stranding
    // anyone. Removal unlinks the grid entry, marks the node deleted, tears
    // down tickables, and journals the row death so the checkpoint drops it
    // (a bare registry eviction would resurrect the room on the next load).
    // Inbound links from surviving rooms are stripped as part of the
    // deletion — a deleted room takes its own links with it, and survivors
    // must not point at rooms that no longer exist. Success acks
    // map_ack + delete_ok.
    private void ApplyDeleteVerdict(BaseConnection connection, int seq, string newKey, string area, int z,
        List<(int x, int y, int fx, int fy, bool hasFallback)> rooms)
    {
        var nh = NodeHandlerFactory();
        HashSet<Coord> targets = new(rooms.Select(r => new Coord(area, r.x, r.y, z)));
        List<(Objects.Node node, Coord fallback, bool hasFallback)> plan = new(rooms.Count);
        foreach (var (x, y, fx, fy, hasFallback) in rooms)
        {
            var node = nh.GetArea(area)?.GetGrid(z)?.GetNode(x, y);
            if (node is null) continue;
            var doors = nh.GetDoors(new Coord(area, x, y, z));
            if (doors is not null && doors.Count > 0)
            {
                connection.SendCommand("delete_denied", new List<object?>{ seq, newKey, $"Room at ({x}, {y}) is a door endpoint; remove the door first." }, []);
                return;
            }
            if (hasFallback && targets.Contains(new Coord(area, fx, fy, z)))
            {
                connection.SendCommand("delete_denied", new List<object?>{ seq, newKey, $"Fallback room at ({fx}, {fy}) is also being deleted." }, []);
                return;
            }
            plan.Add((node, new Coord(area, fx, fy, z), hasFallback));
        }
        foreach (var (node, fallback, hasFallback) in plan)
        {
            var c = node.Coord;
            if (node.GetContents().Count > 0)
            {
                Objects.Node? fallbackNode = null;
                if (hasFallback) fallbackNode = nh.GetArea(fallback.Area)?.GetGrid(fallback.Z)?.GetNode(fallback.X, fallback.Y);
                if (fallbackNode is null)
                {
                    connection.SendCommand("delete_denied", new List<object?>{ seq, newKey, $"Room at ({c.X}, {c.Y}) is occupied." }, []);
                    return;
                }
                for (int sweep = 0; sweep < 3; sweep++)
                {
                    var occupants = node.GetContents();
                    if (occupants.Count == 0) break;
                    foreach (var obj in occupants)
                    {
                        obj.MoveTo(fallbackNode, force: true, announce: false);
                        try { obj.Msg($"A room is being deleted where you stand; you are moved to {fallbackNode.Name}."); } catch (Exception) { }
                    }
                }
                if (node.GetContents().Count != 0)
                {
                    connection.SendCommand("delete_denied", new List<object?>{ seq, newKey, $"Could not clear the room at ({c.X}, {c.Y}); occupants remain." }, []);
                    return;
                }
            }
            node.IsDeleted = true;
            nh.RemoveNode(c);
            if (!node.IsTemporary) Globals.ObjectRegistry.NoteDeleted(node.Id);
            if (node.IsTickable)
            {
                try { Globals.GlobalServices.TryGetTicker()?.RemoveCoro(node.AtTick, node.TickSeconds); } catch (Exception) { }
            }
        }
        // Survivors must not point at rooms that no longer exist: strip
        // inbound links to the deleted coords (snapshot first — removal
        // during enumeration would invalidate it).
        var holders = Globals.ObjectRegistry.FilterBy(o => o is Objects.Node);
        foreach (var holder in holders)
        {
            if (holder is not Objects.Node holderNode || targets.Contains(holderNode.Coord)) continue;
            foreach (var link in holderNode.GetLinks().ToList())
            {
                if (targets.Contains(link.Coord))
                    holderNode.RemoveLink(link.Name);
            }
        }
        connection.SendCommand("map_ack", new List<object?>{ seq, newKey }, []);
        connection.SendCommand("delete_ok", new List<object?>{ seq, newKey }, []);
    }
}
