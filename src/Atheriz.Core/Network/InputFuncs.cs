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
        // explicit registration list — the eight known handlers bind
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

            // snapshot puppet once — inputfuncs.py:284-286
            Atheriz.Core.Objects.GameObject? puppet = null;
            lock (session.Lock) puppet = session.Puppet;

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

// prompt welcome screen
    public void ClientReady(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
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
                    // Cells accept int/long coords, so legend entries do too.
                    if (x is not int && x is not long) return false;
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
    // map_validate_moves, map_edit_legend). Returns false on bad input; each
    // caller keeps its own failure path (silent return vs map_edit_reject).
    private static bool TryGetSeq(object? o, out int seq)
    {
        switch (o)
        {
            case int si:
                seq = si;
                return true;
            case long sl:
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
        if (o is long l) return (int)l;
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
    // (map_edit, map_validate_moves, map_legend). Returns null after sending
    // map_edit_reject; otherwise the consume result for Retry-ack or processing.
    private static Globals.MapEditResult? ConsumeOrReply(BaseConnection connection, string? key, int seq)
    {
        string ip = connection.ClientHost ?? "?";
        var result = Globals.MapEdit.Consume(key!, ip, seq);
        if (result.Status == Globals.MapEditStatus.Reject)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ result.Reason }, []);
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
                if (failed.Count > 0)
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[MapEdit] ApplyMoves refused {failed.Count}/{roomMoves.Count} moves on {result.Chain.Area} z={result.Chain.Z}: {string.Join(";", failed.Take(5))}"); }
                    catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.MapEditHandler: " + logEx.Message, "ConnectionManager"); }
            }
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
}
