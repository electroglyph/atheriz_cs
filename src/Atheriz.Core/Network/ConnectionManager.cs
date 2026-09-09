using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Network;

// Port of atheriz/network/manager.py:1-229
// Manages all connections and orchestrates message handling across protocols.
// Replaces older WebSocketManager to be protocol-agnostic.
// Line-number comments reference manager.py original.

/// <summary>
/// Attribute to mark InputFunc handlers — mirrors <c>atheriz/inputfuncs.py:64 @inputfunc</c>.
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

    // Port of inputfuncs.py:224-238 get_handlers
    public Dictionary<string, Delegate> GetHandlers()
    {
        // Single case-insensitive registry: each handler is stored under its
        // attribute name plus the method name when they differ (no triplication:
        // the derived lowercase form is covered by the comparer itself).
        var handlers = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);
        // explicit registration list — the eight known handlers bind
        // with no per-construction reflection (method-group delegates).
        // Subclass extras (tests only in practice) register via the override
        // hook below, mirroring Python get_handlers subclass discovery.
        void Add(string name, Action<BaseConnection, List<object?>, Dictionary<string, object?>> del, string methodName)
        {
            handlers[name] = del;
            if (!name.Equals(methodName, StringComparison.OrdinalIgnoreCase)) handlers[methodName] = del;
        }
        Add("text", Text, nameof(Text));
        Add("term_size", TermSize, nameof(TermSize));
        Add("map_size", MapSize, nameof(MapSize));
        Add("screenreader", Screenreader, nameof(Screenreader));
        Add("client_ready", ClientReady, nameof(ClientReady));
        Add("map_edit", MapEditHandler, nameof(MapEditHandler));
        Add("map_validate_moves", MapValidateMovesHandler, nameof(MapValidateMovesHandler));
        Add("map_edit_legend", MapEditLegendHandler, nameof(MapEditLegendHandler));
        RegisterExtraHandlers(handlers);
        return handlers;
    }

    /// <summary>
    /// Subclass hook for extra [InputFunc] handlers. The default discovers
    /// methods declared on subclasses only (never re-scans the base eight);
    /// exact-<see cref="InputFuncs"/> instances pay zero reflection.
    /// </summary>
    protected virtual void RegisterExtraHandlers(Dictionary<string, Delegate> handlers)
    {
        if (GetType() == typeof(InputFuncs)) return;
        var methods = GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var m in methods)
        {
            if (m.DeclaringType == typeof(InputFuncs)) continue;
            var attr = m.GetCustomAttribute<InputFuncAttribute>();
            if (attr != null)
            {
                var name = attr.Name ?? m.Name;
                // Create delegate of signature Action<BaseConnection, List<object?>, Dictionary<string,object?>>
                try
                {
                    var del = Delegate.CreateDelegate(typeof(Action<BaseConnection, List<object?>, Dictionary<string, object?>>), this, m, false);
                    if (del != null)
                    {
                        handlers[name] = del;
                        if (!name.Equals(m.Name, StringComparison.OrdinalIgnoreCase)) handlers[m.Name] = del;
                    }
                    else
                    {
                        // fallback generic Delegate
                        var del2 = m.CreateDelegate(typeof(Action<BaseConnection, List<object?>, Dictionary<string, object?>>), this);
                        handlers[name] = del2;
                        if (!name.Equals(m.Name, StringComparison.OrdinalIgnoreCase)) handlers[m.Name] = del2;
                    }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed InputFuncs.RegisterExtraHandlers: " + logEx.Message, "InputFuncs"); }
            }
        }
    }

    // Port of inputfuncs.py:240-301 text handler — core command dispatch
    [InputFunc("text")]
    public void Text(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        try
        {
            var text = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            // port of inputfuncs.py:258 session handling + future check
            var session = connection.Session;
            // In Python, atp is get_async_threadpool(); here we use ConnectionManager's pool via global
            // Check-and-clear must be atomic: prompt owner and disconnect cleanup both touch input_future
            TaskCompletionSource<string>? future = null;
            bool masked = false;
            lock (session.Lock)
            {
                future = session.InputFuture;
                masked = session.InputMasked;
                if (future != null && !future.Task.IsCompleted)
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
            if (future != null)
            {
                if (masked)
                {
                    try { connection.SendCommand("echo_on"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed InputFuncs.Text: " + logEx.Message, "InputFuncs"); }
                }
                // port of inputfuncs.py:277 atp.loop.call_soon_threadsafe(future.set_result, text)
                try { future.TrySetResult(text); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed InputFuncs.Text: " + logEx.Message, "InputFuncs"); }
                return;
            }
            if (string.IsNullOrEmpty(text)) return; // port of inputfuncs.py:280

            // snapshot puppet once — inputfuncs.py:284-286
            Atheriz.Core.Objects.GameObject? puppet = null;
            lock (session.Lock) puppet = session.Puppet;

            if (puppet != null)
            {
                // port of inputfuncs.py:290 dispatch_loggedin immediate
                var job = Atheriz.Core.Commands.CommandDispatcher.DispatchLoggedIn(puppet, text, immediate: true);
                if (job != null)
                {
                    // already on game worker via connection drain — execute inline instead of queueing second task
                    // port of inputfuncs.py:295-297 atp.run(*job)
                    try { job.Func(job.Caller, job.Args); } catch (Exception ex) { try { Atheriz.Core.AtherizLogger.LogError($"Exception in text handler: {ex}"); } catch { Console.Error.WriteLine(ex); } }
                }
            }
            else
            {
                // port of inputfuncs.py:293 _resolve_unloggedin
                var job = Atheriz.Core.Commands.CommandDispatcher.ResolveUnloggedIn(connection, text);
                if (job != null)
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

    // Port of inputfuncs.py:302-320 term_size
    [InputFunc("term_size")]
    public void TermSize(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count >= 2)
        {
            // Handle JsonElement before int check (faithful to handle both types)
            object? a0 = args[0], a1 = args[1];
            int w, h;
            if (a0 is JsonElement je0 && je0.ValueKind == JsonValueKind.Number && je0.TryGetInt32(out var jw)) w = jw;
            else if (a0 is int iv0) w = iv0;
            else if (a0 is long lv0) w = (int)lv0;
            else return;
            if (a1 is JsonElement je1 && je1.ValueKind == JsonValueKind.Number && je1.TryGetInt32(out var jh)) h = jh;
            else if (a1 is int iv1) h = iv1;
            else if (a1 is long lv1) h = (int)lv1;
            else return;
            var settings = AtherizSettings.Global;
            if (!(0 < w && w <= settings.TermSizeMaxWidth && 0 < h && h <= settings.TermSizeMaxHeight)) return;
            connection.Session.TermWidth = w;
            connection.Session.TermHeight = h;
        }
    }

    // Port of inputfuncs.py:322-340 map_size
    [InputFunc("map_size")]
    public void MapSize(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count >= 2)
        {
            object? a0 = args[0], a1 = args[1];
            int w, h;
            if (a0 is JsonElement je0 && je0.ValueKind == JsonValueKind.Number && je0.TryGetInt32(out var jw)) w = jw;
            else if (a0 is int iv0) w = iv0;
            else if (a0 is long lv0) w = (int)lv0;
            else return;
            if (a1 is JsonElement je1 && je1.ValueKind == JsonValueKind.Number && je1.TryGetInt32(out var jh)) h = jh;
            else if (a1 is int iv1) h = iv1;
            else if (a1 is long lv1) h = (int)lv1;
            else return;
            var settings = AtherizSettings.Global;
            if (!(0 < w && w <= settings.MapSizeMaxWidth && 0 < h && h <= settings.MapSizeMaxHeight)) return;
            connection.Session.MapWidth = w;
            connection.Session.MapHeight = h;
        }
    }

    // Port of inputfuncs.py:342-360 screenreader
    [InputFunc("screenreader")]
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

    // Port of inputfuncs.py:362-374 client_ready — prompt welcome screen
    // Port of atheriz/connection_screen.py:95 via ConnectionScreen.Render
    [InputFunc("client_ready")]
    public void ClientReady(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        // Port of inputfuncs.py:399-401 render(connection.session) + msg + prompt
        var welcome = ConnectionScreen.Render(connection.Session); // Port of connection_screen.py:79 render
        connection.Msg(welcome);
        connection.SendCommand("prompt", new List<object?> { ">" }, new Dictionary<string, object?>());
    }

    // Port of inputfuncs.py:18-90 helpers
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
            var arr = jel.EnumerateArray().Select(e=> e.TryGetInt32(out var iv)?iv:-999).ToArray();
            if (arr.Length!=3) return false;
            if (arr[0]==-1 && arr[1]==-1 && arr[2]==-1) return true;
            return arr.All(x=> x>=0 && x<=255);
        }
        return false;
    }
    private static bool IsAttrs(object? v)
    {
        if (v is List<object?> lst) {
            foreach (var e in lst) if (e is not string s || (s!="bold" && s!="italic" && s!="underline")) return false;
            var set = new HashSet<string>(lst.Cast<string>());
            return set.Count==lst.Count && set.IsSubsetOf(new[]{"bold","italic","underline"});
        }
        if (v is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Array)
        {
            var arr = je.EnumerateArray().Select(e=> e.GetString()).ToList();
            if (arr.Any(s=> s!="bold" && s!="italic" && s!="underline")) return false;
            return arr.Distinct().Count()==arr.Count;
        }
        return false;
    }
    private static bool IsLegendEntry(object? v)
    {
        if (v is not Dictionary<string, object?> dict)
        {
            if (v is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                dict = new Dictionary<string, object?>();
                foreach (var p in je.EnumerateObject()) dict[p.Name]= ConnectionManager.JsonElementToObject(p.Value);
            }
            else return false;
        }
        if (!dict.TryGetValue("symbol", out var sym) || sym is not string symStr) return false;
        string visible;
        try { visible = GameUtils.StripAnsi(symStr); } catch { visible = symStr; }
        if (visible.Length==0 || visible.Length>2) return false;
        if (symStr.Length>64) return false;
        if (dict.TryGetValue("desc", out var desc) && desc != null && desc is not string) return false;
        if (dict.TryGetValue("coord", out var coord) && coord != null)
        {
            if (coord is List<object?> lst)
            {
                if (lst.Count!=2) return false;
                if (lst.Any(x=> !(x is int))) return false;
            }
            else if (coord is System.Text.Json.JsonElement je2 && je2.ValueKind==System.Text.Json.JsonValueKind.Array)
            {
                var arr = je2.EnumerateArray().ToList();
                if (arr.Count!=2) return false;
                if (arr.Any(e=> !e.TryGetInt32(out _))) return false;
            }
            else return false;
        }
        if (dict.TryGetValue("show", out var show) && show != null)
        {
            if (show is not bool) return false;
        }
        bool IsFg(object? fg)
        {
            if (fg==null) return true;
            if (fg is int || fg is double || fg is float) return true;
            if (fg is System.Text.Json.JsonElement je && (je.ValueKind==System.Text.Json.JsonValueKind.Number || je.ValueKind==System.Text.Json.JsonValueKind.Null)) return true;
            if (fg is List<object?> lst && lst.Count==3 && lst.All(x=> x is int)) {
                int a=(int)lst[0]!; int b=(int)lst[1]!; int c=(int)lst[2]!;
                if (a==-1 && b==-1 && c==-1) return true;
                return a>=0&&a<=255&&b>=0&&b<=255&&c>=0&&c<=255;
            }
            return false;
        }
        bool IsBg(object? bg)
        {
            if (bg==null) return true;
            if (bg is int || bg is double || bg is float) return true;
            if (bg is System.Text.Json.JsonElement je && (je.ValueKind==System.Text.Json.JsonValueKind.Number || je.ValueKind==System.Text.Json.JsonValueKind.Null)) return true;
            if (bg is List<object?> lst && lst.Count==3 && lst.All(x=> x is int)) {
                int a=(int)lst[0]!; int b=(int)lst[1]!; int c=(int)lst[2]!;
                if (a==-1 && b==-1 && c==-1) return true;
                return a>=-1&&a<=255&&b>=-1&&b<=255&&c>=-1&&c<=255;
            }
            return false;
        }
        dict.TryGetValue("fg", out var fgVal);
        dict.TryGetValue("bg", out var bgVal);
        if (!IsFg(fgVal)) return false;
        if (!IsBg(bgVal)) return false;
        return true;
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
        if (o is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Array) return je.EnumerateArray().Select(ConnectionManager.JsonElementToObject).ToList()!;
        return new List<object?>();
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
            connection.SendCommand("map_edit_reject", new List<object?>{ result.Reason }, new Dictionary<string, object?>());
            return null;
        }
        return result;
    }

    // Port of inputfuncs.py:376-489 map_edit
    [InputFunc("map_edit")]
    public void MapEditHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3) return;
        var key = args[0] as string;
        object? seqObj = args[1];
        var cellsObj = args[2];
        if (key == null || cellsObj == null) return;
        // seq must be int
        int seq;
        if (seqObj is int si) seq=si;
        else if (seqObj is long sl) seq=(int)sl;
        else if (seqObj is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var jsi)) seq=jsi;
        else return;
        // key is a string here (null returned above), so only the cells shape
        // still needs checking.
        if (cellsObj is not List<object?> && cellsObj is not System.Text.Json.JsonElement) {
            // try to normalize cells via ToList? For JsonElement list it will be list of JsonElements -> still allowed but need validation path
            if (cellsObj is System.Text.Json.JsonElement je2 && je2.ValueKind==System.Text.Json.JsonValueKind.Array) { }
            else return;
        }
        var cells = ToList(cellsObj);
        // Validate cells
        foreach (var cellObj in cells)
        {
            var cell = ToList(cellObj);
            if (cell.Count==0) return;
            // Convert possible JsonElement string first element
            object? first = cell[0];
            if (first is System.Text.Json.JsonElement jef && jef.ValueKind==System.Text.Json.JsonValueKind.String) first = jef.GetString();
            if (first is string fs && fs=="room")
            {
                if (cell.Count!=5) return;
                for(int i=1;i<5;i++) {
                    var v = cell[i];
                    if (v is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number) { if (!je.TryGetInt32(out _)) return; }
                    else if (v is not int && v is not long) return;
                }
                continue;
            }
            if (cell.Count!=3 && cell.Count!=6) return;
            // first two must be int
            for(int i=0;i<2;i++) {
                var v=cell[i];
                if (v is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number) { if (!je.TryGetInt32(out _)) return; }
                else if (v is not int && v is not long) return;
            }
            if (cell[2] is not string)
            {
                if (cell[2] is System.Text.Json.JsonElement je3 && je3.ValueKind==System.Text.Json.JsonValueKind.String) { } else return;
            }
            if (cell.Count==6)
            {
                if (!IsColor(cell[3]) || !IsColor(cell[4]) || !IsAttrs(cell[5])) return;
            }
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result == null) return;
        if (result.Status == Globals.MapEditStatus.Retry)
        {
            connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, new Dictionary<string, object?>());
            return;
        }
        // Process edits
        var mh = MapHandlerFactory();
        var mi = mh.GetMapInfo(result.Chain!.Area, result.Chain.Z);
        if (mi != null)
        {
            using (mi.BatchUpdate())
            {
                mi.Lock.EnterWriteLock();
                try
                {
                    foreach (var cellObj in cells)
                    {
                        var cell = ToList(cellObj);
                        if (cell.Count>0)
                        {
                            object? f0 = cell[0];
                            if (f0 is System.Text.Json.JsonElement je0 && je0.ValueKind==System.Text.Json.JsonValueKind.String) f0 = je0.GetString();
                            if (f0 is string s0 && s0=="room") continue;
                        }
                        int x = ToInt(cell[0]); int y = ToInt(cell[1]);
                        string sym = cell[2] is string ss ? ss : (cell[2] is System.Text.Json.JsonElement je4 && je4.ValueKind==System.Text.Json.JsonValueKind.String ? je4.GetString()??"" : "");
                        if (sym=="") mi.PreGrid.Remove((x,y));
                        else if (cell.Count==3) mi.PreGrid[(x,y)] = sym;
                        else
                        {
                            var fg = cell[3]; var bg = cell[4]; var attrs = cell[5];
                            // decode fg/bg
                            (byte R,byte G,byte B)? fgT=null; (byte R,byte G,byte B)? bgT=null;
                            List<object?> fgList = ToList(fg); List<object?> bgList = ToList(bg);
                            bool fgTrans = fgList.Count==3 && fgList.All(x=> ToInt(x)==-1);
                            bool bgTrans = bgList.Count==3 && bgList.All(x=> ToInt(x)==-1);
                            if (!fgTrans && fgList.Count==3) fgT = ((byte)ToInt(fgList[0]), (byte)ToInt(fgList[1]), (byte)ToInt(fgList[2]));
                            if (!bgTrans && bgList.Count==3) bgT = ((byte)ToInt(bgList[0]), (byte)ToInt(bgList[1]), (byte)ToInt(bgList[2]));
                            var attrList = ToList(attrs);
                            bool bold = attrList.Any(a=> a is string s && s=="bold" || a is System.Text.Json.JsonElement je && je.GetString()=="bold");
                            bool italic = attrList.Any(a=> a is string s && s=="italic" || a is System.Text.Json.JsonElement je && je.GetString()=="italic");
                            bool underline = attrList.Any(a=> a is string s && s=="underline" || a is System.Text.Json.JsonElement je && je.GetString()=="underline");
                            string wrapped = GameUtils.WrapRgb(sym, fgT, bgT, bold, italic, underline);
                            mi.PreGrid[(x,y)] = wrapped;
                        }
                    }
                    mi.MapChanged = true;
                }
                finally { mi.Lock.ExitWriteLock(); }
            }
        }
        var roomMoves = new List<((int X,int Y) src,(int X,int Y) dst)>();
        foreach (var cellObj in cells)
        {
            var cell = ToList(cellObj);
            if (cell.Count>0)
            {
                object? f0 = cell[0];
                if (f0 is System.Text.Json.JsonElement je0 && je0.ValueKind==System.Text.Json.JsonValueKind.String) f0 = je0.GetString();
                if (f0 is string s0 && s0=="room")
                {
                    int fx=ToInt(cell[1]), fy=ToInt(cell[2]), tx=ToInt(cell[3]), ty=ToInt(cell[4]);
                    roomMoves.Add(((fx,fy),(tx,ty)));
                }
            }
        }
        if (roomMoves.Count>0)
        {
            var nh = NodeHandlerFactory();
            var areaObj = nh.GetArea(result.Chain.Area);
            var grid = areaObj?.GetGrid(result.Chain.Z);
            if (grid != null)
            {
                var failed = grid.ApplyMoves(roomMoves);
                // Port of mapedit.py apply: surface refused moves (the client
                // already validated — a refusal here means a cross-message
                // validate->apply TOCTOU or a stale chain, worth one line).
                if (failed.Count > 0)
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[MapEdit] ApplyMoves refused {failed.Count}/{roomMoves.Count} moves on {result.Chain.Area} z={result.Chain.Z}: {string.Join(";", failed.Take(5))}"); }
                    catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.MapEditHandler: " + logEx.Message, "ConnectionManager"); }
            }
        }
        connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, new Dictionary<string, object?>());
    }

    [InputFunc("map_validate_moves")]
    public void MapValidateMovesHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3 || args.Count > 4) return;
        var key = args[0] as string;
        object? seqObj = args[1];
        var movesObj = args[2];
        if (key==null || movesObj==null) return;
        int seq;
        if (seqObj is int si) seq=si;
        else if (seqObj is long sl) seq=(int)sl;
        else if (seqObj is System.Text.Json.JsonElement jeSeq && jeSeq.ValueKind==System.Text.Json.JsonValueKind.Number && jeSeq.TryGetInt32(out var jsi)) seq=jsi;
        else return;
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
            context = new List<((int,int),(int,int))>();
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
        if (result == null) return;
        if (result.Status == Globals.MapEditStatus.Retry)
        {
            SendMoveVerdict(connection, seq, result.NewKey!, result.Chain!.Validation ?? new List<int>());
            return;
        }
        var nh2 = NodeHandlerFactory();
        var areaObj2 = nh2.GetArea(result.Chain!.Area);
        var grid2 = areaObj2?.GetGrid(result.Chain.Z);
        List<int> denied;
        if (grid2==null) denied = Enumerable.Range(0, movesList.Count).ToList();
        else
        {
            var moves = movesList.Select(mObj=> {
                var m=ToList(mObj);
                return ((ToInt(m[0]), ToInt(m[1])), (ToInt(m[2]), ToInt(m[3])));
            }).ToList();
            var failed = grid2.CheckMoves(moves, context);
            denied = failed.OrderBy(x=>x).ToList();
        }
        Globals.MapEdit.SetValidation(result.NewKey!, denied);
        SendMoveVerdict(connection, seq, result.NewKey!, denied);
    }

    private void SendMoveVerdict(BaseConnection connection, int seq, string newKey, List<int> denied)
    {
        if (denied.Count>0) connection.SendCommand("moves_denied", new List<object?>{ seq, newKey, denied }, new Dictionary<string, object?>());
        else connection.SendCommand("moves_ok", new List<object?>{ seq, newKey }, new Dictionary<string, object?>());
    }

    [InputFunc("map_edit_legend")]
    public void MapEditLegendHandler(BaseConnection connection, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (args.Count < 3)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid legend payload." }, new Dictionary<string, object?>());
            return;
        }
        var key = args[0] as string;
        object? seqObj = args[1];
        var legendObj = args[2];
        if (key==null || legendObj==null)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid legend payload." }, new Dictionary<string, object?>());
            return;
        }
        int seq;
        if (seqObj is int si) seq=si;
        else if (seqObj is long sl) seq=(int)sl;
        else if (seqObj is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var jsi)) seq=jsi;
        else { connection.SendCommand("map_edit_reject", new List<object?>{ "Invalid legend payload." }, new Dictionary<string, object?>()); return; }
        var legend = ToList(legendObj);
        if (legend.Count>200) { connection.SendCommand("map_edit_reject", new List<object?>{ "Too many legend entries (max 200)." }, new Dictionary<string, object?>()); return; }
        for(int idx=0; idx<legend.Count; idx++)
        {
            var entry = legend[idx];
            // Normalize JsonElement to dict if needed
            object? norm = entry;
            if (entry is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                var dict = new Dictionary<string, object?>();
                foreach(var p in je.EnumerateObject()) dict[p.Name]= ConnectionManager.JsonElementToObject(p.Value);
                norm = dict;
            }
            if (!IsLegendEntry(norm))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid legend entry at index {idx}." }, new Dictionary<string, object?>());
                return;
            }
        }
        var result = ConsumeOrReply(connection, key, seq);
        if (result == null) return;
        if (result.Status == Globals.MapEditStatus.Retry)
        {
            connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, new Dictionary<string, object?>());
            // legacy also sends legend_ok? Python for retry only sends map_ack (no legend_ok). Check python: for retry it does connection.send_command("map_ack", seq, new_key) return (no legend_ok). So only ack.
            return;
        }
        var mh = MapHandlerFactory();
        var mi = mh.GetMapInfo(result.Chain!.Area, result.Chain.Z);
        if (mi==null)
        {
            mi = new MapInfo(result.Chain.Area);
            // atomic publish — concurrent creators converge on the
            // stored winner; a locally built loser is dropped, not last-wins.
            mi = mh.GetOrAddMapInfo(result.Chain.Area, result.Chain.Z, mi);
        }
        var newEntries = new List<LegendEntry>();
        foreach (var eObj in legend)
        {
            Dictionary<string, object?> dict;
            if (eObj is Dictionary<string, object?> d) dict=d;
            else if (eObj is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                dict = new Dictionary<string, object?>();
                foreach(var p in je.EnumerateObject()) dict[p.Name]= ConnectionManager.JsonElementToObject(p.Value);
            }
            else continue;
            var le = new LegendEntry();
            le.Symbol = dict.TryGetValue("symbol", out var sy) ? sy as string : null;
            var desc = dict.TryGetValue("desc", out var de) ? de : null;
            if (desc == null) le.Desc = "";
            else le.Desc = desc as string ?? "";
            if (dict.TryGetValue("coord", out var co) && co != null)
            {
                if (co is List<object?> lst && lst.Count>=2) le.Coord = (ToInt(lst[0]), ToInt(lst[1]));
                else if (co is System.Text.Json.JsonElement je2 && je2.ValueKind==System.Text.Json.JsonValueKind.Array)
                {
                    var arr = je2.EnumerateArray().Select(x=> x.TryGetInt32(out var iv)?iv:0).ToArray();
                    if (arr.Length>=2) le.Coord=(arr[0],arr[1]);
                }
            }
            else le.Coord=null;
            le.Show = dict.TryGetValue("show", out var sh) && sh is bool sb ? sb : true;
            if (dict.TryGetValue("fg", out var fg) && fg != null)
            {
                if (fg is double dd) le.Fg=dd;
                else if (fg is int ii) le.Fg=ii;
                else if (fg is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var dv)) le.Fg=dv;
                else le.Fg=170.0;
            }
            if (dict.TryGetValue("bg", out var bg) && bg != null)
            {
                if (bg is double db) le.Bg=db;
                else if (bg is int ib) le.Bg=ib;
                else if (bg is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var dv)) le.Bg=dv;
                else le.Bg=null;
            }
            newEntries.Add(le);
        }
        mi.Lock.EnterWriteLock();
        try { mi.LegendEntries.Clear(); mi.LegendEntries.AddRange(newEntries); mi.MapChanged=true; }
        finally { mi.Lock.ExitWriteLock(); }
        mi.RenderLegend();
        connection.SendCommand("map_ack", new List<object?>{ seq, result.NewKey }, new Dictionary<string, object?>());
        connection.SendCommand("legend_ok", new List<object?>{ seq, result.NewKey }, new Dictionary<string, object?>());
    }
}

/// <summary>
/// Port of atheriz/network/manager.py:41-229 ConnectionManager.
/// </summary>
public class ConnectionManager
{
    // Port of manager.py:10-24 malformed throttling — now via ThrottleWindow
    private static readonly object _malformedLock = new object();
    private static readonly Dictionary<string, double> _malformedLast = new();
    private const double MalformedWindow = 5.0; // port of manager.py:12

    private static string SummarizeRaw(string rawMessage, int limit = 80) // port of manager.py:14-15
    {
        var sub = rawMessage.Length > limit ? rawMessage.Substring(0, limit) : rawMessage;
        // Approximation of Python repr(sub) — quoted string with escapes
        return JsonSerializer.Serialize(sub);
    }

    private static bool ShouldLogMalformed(string host) // port of manager.py:17-24
        => ThrottleWindow.ShouldLog(_malformedLast, _malformedLock, host, MalformedWindow);

    // Port of websocket.py:15-27 oversize throttling (per-host 5s window),
    // for the shared HandleCommand size cap .
    private static readonly object _oversizeLock = new object();
    private static readonly Dictionary<string, double> _oversizeLast = new();
    private const double OversizeWindow = 5.0; // port of websocket.py:13
    private static bool ShouldLogOversize(string host)
        => ThrottleWindow.ShouldLog(_oversizeLast, _oversizeLock, host, OversizeWindow);

    // Reference equality comparer — mirrors id(connection) at manager.py:52,113,125
    private sealed class ReferenceEqualityComparer : IEqualityComparer<BaseConnection>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public bool Equals(BaseConnection? x, BaseConnection? y) => ReferenceEquals(x, y);
        public int GetHashCode(BaseConnection obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    // Port of manager.py:47-63 __init__
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion); // port of manager.py:54 RLock
    private readonly Dictionary<string, BaseConnection> _connections = new(); // port of manager.py:51
    private readonly Dictionary<BaseConnection, string> _connToId = new(ReferenceEqualityComparer.Instance); // port of manager.py:52
    private readonly Dictionary<string, int> _perIpCounts = new(); // port of manager.py:53
    // orphan-sweep timer (started lazily on first registration).
    // Reaping rides a 60s wall-clock cadence, not registration traffic.
    private System.Threading.Timer? _orphanSweepTimer;
    private int _sweepStarted;
    private readonly Dictionary<string, Delegate> _messageHandlers = new(StringComparer.OrdinalIgnoreCase); // port of manager.py:55
    private int _connectionCounter; // port of manager.py:56

    public AsyncThreadPool Atp { get; } // port of manager.py:57
    public InputFuncs InputFuncs { get; } // port of manager.py:59
    private readonly AtherizSettings _settings;

    // Global singleton — mirrors get_connection_manager() at globals/get.py:79-86
    private static ConnectionManager? _globalInstance;
    private static readonly object _globalLock = new();
    public static ConnectionManager? GlobalInstance
    {
        get { lock (_globalLock) return _globalInstance; }
        set { lock (_globalLock) _globalInstance = value; }
    }

    public ConnectionManager(AsyncThreadPool? pool = null, AtherizSettings? settings = null, InputFuncs? inputFuncs = null)
    {
        _settings = settings ?? AtherizSettings.Global;
        Atp = pool ?? new AsyncThreadPool(
            maxThreads: _settings.ThreadpoolLimit,
            queueLimit: _settings.ThreadpoolQueueLimit,
            reliefLimit: _settings.ThreadpoolReliefLimit,
            watchdogSeconds: TimeSpan.FromSeconds(_settings.ThreadpoolWatchdogSeconds),
            watchdogInterval: TimeSpan.FromSeconds(_settings.ThreadpoolWatchdogInterval));
        InputFuncs = inputFuncs ?? new InputFuncs();
        // port of manager.py:62-63 Register handlers from InputFuncs
        foreach (var kv in InputFuncs.GetHandlers())
            RegisterHandler(kv.Key, kv.Value);

        lock (_globalLock) _globalInstance ??= this;
    }

    // Port of manager.py:65-68 generate_connection_id. A bare counter needs
    // no manager lock: Interlocked owns the increment.
    public virtual string GenerateConnectionId()
    {
        var n = Interlocked.Increment(ref _connectionCounter);
        return $"conn_{n}";
    }

    // pre-spawn admission probe. Mirrors the RegisterConnection
    // gates (ban + per-IP + total) without creating a connection, so accept
    // loops can refuse floods before queueing handler tasks. Authoritative
    // enforcement stays in RegisterConnection (counts can shift between
    // probe and register); a refused probe only avoids the spawn.
    public bool ShouldRefusePreSpawn(string host)
    {
        if (ObjectRegistry.IsIpBanned(host)) return true;
        _lock.EnterReadLock();
        try
        {
            var limit = _settings.MaxConnectionsPerIp;
            if (limit > 0 && host != "?" && _perIpCounts.TryGetValue(host, out var cnt) && cnt >= limit)
                return true;
            if (_settings.MaxTotalConnections > 0 && _connections.Count >= _settings.MaxTotalConnections)
                return true;
            return false;
        }
        finally { _lock.ExitReadLock(); }
    }

    // refusal teardown runs outside the manager write lock (see
    // RefuseConnection). Close() does task/socket work that used to stall
    // every register/disconnect/count op while the lock was held.
    // Port of manager.py:70-119 register_connection
    public virtual bool RegisterConnection(string connId, BaseConnection connection)
    {
        var host = connection.ClientHost ?? "?"; // port of manager.py:76
        connection.RegisteredHost = host;
        var limit = _settings.MaxConnectionsPerIp; // port of manager.py:77
        string? refusal = null;
        _lock.EnterWriteLock();
        try
        {
            if (ObjectRegistry.IsIpBanned(host)) // port of manager.py:79-85
                refusal = $"[Network] Refusing connection from banned host {host}";
            else if (limit > 0 && host != "?") // port of manager.py:86-101
            {
                var sameHost = _perIpCounts.TryGetValue(host, out var cnt) ? cnt : 0;
                // if overwriting same conn_id, don't count itself twice — manager.py:88-91
                if (_connections.TryGetValue(connId, out var existing) && (existing.RegisteredHost ?? existing.ClientHost ?? "?") == host)
                    sameHost--;
                if (sameHost >= limit)
                    refusal = $"[Network] Refusing connection from {host}: per-IP limit ({limit}) reached";
            }
            // Total-connection admission cap (0 = unlimited). Checked after the
            // per-IP gate so the refusal reason stays specific.
            if (refusal == null && _settings.MaxTotalConnections > 0 && _connections.Count >= _settings.MaxTotalConnections)
                refusal = $"[Network] Refusing connection from {host}: total limit ({_settings.MaxTotalConnections}) reached";
            if (refusal == null)
            {
            // Re-registering the same conn id from the same host replaces the same
            // registration, so it must not increment the per-IP counter again —
            // double-counting leaks the bucket toward a false limit refusal.
            var sameHostReregister = false;
            // handle overwrite: adjust old host count — manager.py:102-113
            if (_connections.TryGetValue(connId, out var old))
            {
                var oldHost = old.RegisteredHost ?? old.ClientHost ?? "?";
                if (oldHost == host) sameHostReregister = true;
                else if (oldHost != "?" && oldHost != host)
                {
                    var cnt = _perIpCounts.TryGetValue(oldHost, out var c) ? c - 1 : -1;
                    if (cnt <= 0) _perIpCounts.Remove(oldHost);
                    else _perIpCounts[oldHost] = cnt;
                }
                _connToId.Remove(old);
            }
            _connections[connId] = connection; // port of manager.py:114
            _connToId[connection] = connId; // port of manager.py:115
            if (host != "?" && !sameHostReregister) // port of manager.py:116-117
                _perIpCounts[host] = _perIpCounts.TryGetValue(host, out var v) ? v + 1 : 1;
            }
        }
        finally { _lock.ExitWriteLock(); }
        if (refusal != null)
        {
            RefuseConnection(connection, refusal);
            return false;
        }
        try { Atheriz.Core.AtherizLogger.LogInformation($"[Network] Connection opened: {connId} (total: {ConnectionCount})"); } catch { Console.Error.WriteLine($"[Network] Connection opened: {connId} (total: {ConnectionCount})"); } // port of manager.py:118
        // timer-driven orphan sweep. The old every-50th-registration
        // amortization never reaped on a low-traffic server; the WS receive
        // path has no idle timeout of its own, so this 60s cadence covers
        // abandoned pre-login sockets on both transports. Starts lazily so
        // short-lived/test instances pay nothing.
        if (System.Threading.Interlocked.CompareExchange(ref _sweepStarted, 1, 0) == 0)
        {
            try { _orphanSweepTimer = new System.Threading.Timer(_ => { try { SweepOrphanedConnections(TimeSpan.FromMinutes(5)); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager orphan sweep: " + logEx.Message, "ConnectionManager"); } }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.RegisterConnection: " + logEx.Message, "ConnectionManager"); }
        }
        return true;
    }

    // refusal teardown. Runs after the manager write lock releases —
    // Close() does task/socket work that must not stall concurrent
    // register/disconnect/count ops. Messages mirror the old inline refuses.
    private static void RefuseConnection(BaseConnection connection, string reason)
    {
        try { Atheriz.Core.AtherizLogger.LogWarning(reason); } catch { Console.Error.WriteLine(reason); }
        try { connection.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.RefuseConnection: " + logEx.Message, "ConnectionManager"); }
    }

    /// <summary>
    /// Disconnects sockets that never attached an account/puppet and are older
    /// than <paramref name="maxPreLoginAge"/>. Returns the number swept.
    /// Same-account duplicate gating is intentionally absent (the login layer
    /// owns session replacement); this only reaps abandoned pre-login sockets.
    /// </summary>
    public int SweepOrphanedConnections(TimeSpan maxPreLoginAge)
    {
        var cutoff = DateTime.UtcNow - maxPreLoginAge;
        var stale = new List<BaseConnection>();
        foreach (var c in ConnectionsSnapshot.Values)
        {
            try
            {
                var s = c.Session;
                if (s == null || s.Puppet != null || s.Account != null) continue;
                if (c.ConnectedAtUtc > cutoff) continue;
                stale.Add(c);
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.SweepOrphanedConnections: " + logEx.Message, "ConnectionManager"); }
        }
        int swept = 0;
        foreach (var c in stale)
        {
            try { Disconnect(c); swept++; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ConnectionManager.SweepOrphanedConnections: " + logEx.Message, "ConnectionManager"); }
        }
        return swept;
    }

    // Port of manager.py:121-153 disconnect
    public virtual void Disconnect(BaseConnection connection)
    {
        string? connId = null;
        var host = connection.RegisteredHost ?? connection.ClientHost ?? "?"; // port of manager.py:123
        _lock.EnterWriteLock();
        try
        {
            if (_connToId.TryGetValue(connection, out var id))
            {
                connId = id;
                _connToId.Remove(connection);
                if (_connections.TryGetValue(connId, out var stored) && ReferenceEquals(stored, connection))
                {
                    _connections.Remove(connId);
                    if (host != "?")
                    {
                        var cnt = _perIpCounts.TryGetValue(host, out var c) ? c - 1 : -1;
                        if (cnt <= 0) _perIpCounts.Remove(host);
                        else _perIpCounts[host] = cnt;
                    }
                }
            }
        }
        finally { _lock.ExitWriteLock(); }

        if (string.IsNullOrEmpty(connId)) return; // port of manager.py:138

        // SetDisconnected locks internally; no outer connection lock needed.
        connection.SetDisconnected(true); // port of manager.py:139-140
        connection.ClearPendingInput(); // port of manager.py:141
        var session = connection.Session; // port of manager.py:142
        if (session != null)
        {
            // port of manager.py:144-148 run session teardown on game threadpool.
            // Fire-and-forget by design: disconnect() executes on the network
            // event loop and must not block on teardown (pinned by
            // DisconnectDoesNotBlockOnSlowTeardown: 0.5s teardown, <0.5s return).
            // Pool saturated but alive: never run teardown inline —
            // re-schedule with a short delay (same pattern as GameTime
            // alarms); the delayed task re-queues onto the pool once it
            // drains. Pool STOPPED: the delay would drop the teardown
            // silently (Delay never fires on a stopped pool), losing
            // session/puppet cleanup with only a log line — run it inline
            // instead . No event-loop throughput is left to protect
            // on a stopped pool.
            bool queued = false;
            try { queued = Atp.AddTask(() => DoSessionDisconnect(session)); }
            catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown could not be queued during disconnect: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown could not be queued during disconnect: {e}"); } }
            if (!queued)
            {
                bool stopped = false;
                try { stopped = Atp.IsStopped; } catch { }
                if (stopped) DoSessionDisconnect(session);
                else
                {
                    try { Atp.Delay(0.05, () => DoSessionDisconnect(session)); }
                    catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown could not be deferred during disconnect: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown could not be deferred during disconnect: {e}"); } }
                }
            }
        }
        try { connection.Close(); } // port of manager.py:149-152
        catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Connection cleanup failed: {e}"); } catch { Console.Error.WriteLine($"[Network] Connection cleanup failed: {e}"); } }
        try { (connection as IDisposable)?.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ReferenceEqualityComparer.Disconnect: " + logEx.Message, "ReferenceEqualityComparer"); }
        try { Atheriz.Core.AtherizLogger.LogInformation($"[Network] Connection closed: {connId} (total: {ConnectionCount})"); } catch { Console.Error.WriteLine($"[Network] Connection closed: {connId} (total: {ConnectionCount})"); } // port of manager.py:153
    }

    // Port of manager.py:155-162 _do_session_disconnect
    private void DoSessionDisconnect(Session session)
    {
        try { session.AtDisconnect(); }
        catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown failed: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown failed: {e}"); } }
    }

    // Port of manager.py:164-167 connection_count property
    public int ConnectionCount
    {
        get { _lock.EnterReadLock(); try { return _connections.Count; } finally { _lock.ExitReadLock(); } }
    }

    // Port of manager.py:169-171 get_all_connections
    public List<BaseConnection> GetAllConnections()
    {
        _lock.EnterReadLock();
        try { return _connections.Values.ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    // Port of manager.py:173-179 broadcast
    public void Broadcast(string text)
    {
        var connections = GetAllConnections(); // port of manager.py:174
        foreach (var conn in connections)
        {
            try { conn.Msg(text); } // port of manager.py:177
            catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Broadcast error: {e}"); } catch { Console.Error.WriteLine($"[Network] Broadcast error: {e}"); } } // port of manager.py:178-179
        }
    }

    // Port of manager.py:181-183 register_handler
    public void RegisterHandler(string messageType, Delegate handler)
    {
    // Port of manager.py:181-183 register_handler — exact match only.
        _lock.EnterWriteLock();
        try { _messageHandlers[messageType] = handler; } // port of manager.py:183
        finally { _lock.ExitWriteLock(); }
    }

    // Helper for strip — port of manager.py:31-38 _strip_input_value
    private static object? StripInputValue(object? value)
    {
        if (value is string s) return GameUtils.StripTerminalEscapes(s); // port of manager.py:32-33
        if (value is List<object?> lst) return lst.Select(StripInputValue).ToList(); // port of manager.py:34-35
        if (value is Dictionary<string, object?> dict) // port of manager.py:36-37
        {
            var res = new Dictionary<string, object?>();
            foreach (var kv in dict) res[kv.Key] = StripInputValue(kv.Value);
            return res;
        }
        if (value is JsonElement je)
        {
            // Should have been converted; but if raw Je, strip string case
            if (je.ValueKind == JsonValueKind.String) return GameUtils.StripTerminalEscapes(je.GetString() ?? "");
            return value;
        }
        return value;
    }

    // Port of manager.py:185-215 handle_command
    public virtual void HandleCommand(BaseConnection connection, string rawMessage)
    {
        try
        {
            // size cap precedes the parse. WebsocketMaxMessageSize was
            // enforced only at the WS edge (WebSocketProtocol); direct
            // callers of this shared entry point could force a large parse.
            int maxMessageSize = _settings.WebsocketMaxMessageSize;
            // Byte budget, not char count: Length is only a fast prefilter
            // (bytes always >= chars, so Length-over already refuses); a
            // short multibyte string can still carry 4x the nominal bytes.
            if (rawMessage.Length > maxMessageSize || System.Text.Encoding.UTF8.GetByteCount(rawMessage) > maxMessageSize)
            {
                var oversizeHost = connection.ClientHost ?? "?";
                if (ShouldLogOversize(oversizeHost))
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Message too large from {oversizeHost} ({rawMessage.Length} bytes > {maxMessageSize} bytes)"); } catch { Console.Error.WriteLine($"[Network] Message too large from {oversizeHost} ({rawMessage.Length} bytes > {maxMessageSize} bytes)"); }
                return;
            }
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1) // port of manager.py:194
            {
                var host = connection.ClientHost ?? "?"; // port of manager.py:195
                if (ShouldLogMalformed(host)) // port of manager.py:196
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Invalid message format from {host} ({rawMessage.Length} bytes): {SummarizeRaw(rawMessage)}"); } catch { Console.Error.WriteLine($"[Network] Invalid message format from {host} ({rawMessage.Length} bytes): {SummarizeRaw(rawMessage)}"); } // port of manager.py:197-199
                return;
            }
            var cmdElement = root[0];
            // Non-string commands are malformed input (port of manager.py:194):
            // GetString() would throw for numbers/arrays, routing them to the
            // error path instead of the malformed path. Reject them cleanly.
            if (cmdElement.ValueKind != JsonValueKind.String)
            {
                var host2 = connection.ClientHost ?? "?";
                if (ShouldLogMalformed(host2))
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Invalid message format from {host2} ({rawMessage.Length} bytes): {SummarizeRaw(rawMessage)}"); } catch { Console.Error.WriteLine($"[Network] Invalid message format from {host2} ({rawMessage.Length} bytes): {SummarizeRaw(rawMessage)}"); }
                return;
            }
            var cmd = cmdElement.GetString()!;
            List<object?> args = new(); // port of manager.py:203
            Dictionary<string, object?> kwargs = new(); // port of manager.py:204
            if (root.GetArrayLength() > 1) args = JsonElementToObject(root[1]) as List<object?> ?? new();
            if (root.GetArrayLength() > 2) kwargs = JsonElementToObject(root[2]) as Dictionary<string, object?> ?? new();

            Dispatch(connection, cmd, args, kwargs); // port of manager.py:206
        }
        catch (JsonException exc) // port of manager.py:208
        {
            var host = connection.ClientHost ?? "?"; // port of manager.py:209
            if (ShouldLogMalformed(host)) // port of manager.py:210
                try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Error decoding JSON from {host} ({rawMessage.Length} bytes): {exc.Message} at position {exc.BytePositionInLine}: {SummarizeRaw(rawMessage)}"); } catch { Console.Error.WriteLine($"[Network] Error decoding JSON from {host} ({rawMessage.Length} bytes): {exc.Message} at position {exc.BytePositionInLine}: {SummarizeRaw(rawMessage)}"); } // port of manager.py:211-213
        }
        catch (Exception e) // port of manager.py:214-215
        {
            try { Atheriz.Core.AtherizLogger.LogError($"[Network] Error handling message: {e}"); } catch { Console.Error.WriteLine($"[Network] Error handling message: {e}"); }
        }
    }

    // Port of manager.py:217-229 dispatch
    public void Dispatch(BaseConnection connection, string cmd, List<object?> args, Dictionary<string, object?> kwargs)
    {
        // Handlers run on game threadpool via connection's serialized input queue — manager.py:218-221
        if (_settings.StripInputEscapeSequences) // port of manager.py:222
        {
            // args = [_strip_input_value(value) for value in args] — manager.py:223
            var strippedArgs = new List<object?>();
            foreach (var v in args)
            {
                var sv = StripInputValue(v);
                // Unwrap if Strip returned List<object?> for element? For args list, element is object?; if it's List we keep as is? Actually _strip_input_value recurses but for string it returns string; for list it returns list; for args we have list of values, each may be string/list/dict.
                // If v was string, sv is string; if v was list, sv is List<object?>
                strippedArgs.Add(sv);
            }
            args = strippedArgs;
            var boxed = StripInputValue(kwargs);
            if (boxed is Dictionary<string, object?> d) kwargs = d;
        }
        Delegate? handler = null;
        _lock.EnterReadLock();
        try { _messageHandlers.TryGetValue(cmd, out handler); } // port of manager.py:225 exact .get(cmd)
        finally { _lock.ExitReadLock(); }
        if (handler != null) // port of manager.py:226-227
        {
            connection.EnqueueInput(handler, args, kwargs);
        }
        else
        {
            try { Atheriz.Core.AtherizLogger.LogDebug($"Unknown command: {cmd}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ReferenceEqualityComparer.Dispatch: " + logEx.Message, "ReferenceEqualityComparer"); } // port of manager.py:229 (logger.debug)
        }
    }

    // Helpers to convert JsonElement to List/Dict of objects: single family below.

    internal static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => ConvertArray(el),
            JsonValueKind.Object => ConvertDict(el),
            _ => null
        };

        // Array/object recursion lives here, inside the single converter family.
        static List<object?> ConvertArray(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Array) return new List<object?> { JsonElementToObject(a) };
            var list = new List<object?>();
            foreach (var item in a.EnumerateArray()) list.Add(JsonElementToObject(item));
            return list;
        }

        static Dictionary<string, object?> ConvertDict(JsonElement o)
        {
            if (o.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();
            var dict = new Dictionary<string, object?>();
            foreach (var prop in o.EnumerateObject()) dict[prop.Name] = JsonElementToObject(prop.Value);
            return dict;
        }
    }

    // For tests / introspection — expose internal state counts similar to Python's _connections
    public IReadOnlyDictionary<string, BaseConnection> ConnectionsSnapshot
    {
        get { _lock.EnterReadLock(); try { return new Dictionary<string, BaseConnection>(_connections); } finally { _lock.ExitReadLock(); } }
    }
}
