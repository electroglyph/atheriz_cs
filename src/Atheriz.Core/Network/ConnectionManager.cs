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
        var handlers = new Dictionary<string, Delegate>();
        var methods = GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var m in methods)
        {
            var attr = m.GetCustomAttribute<InputFuncAttribute>();
            if (attr != null)
            {
                var name = attr.Name ?? m.Name;
                // Store both original and lower for case-insensitive dispatch (webclient sends snake_case lower, tests check PascalCase)
                var lower = name.ToLowerInvariant();
                // Create delegate of signature Action<BaseConnection, List<object?>, Dictionary<string,object?>>
                try
                {
                    var del = Delegate.CreateDelegate(typeof(Action<BaseConnection, List<object?>, Dictionary<string, object?>>), this, m, false);
                    if (del != null) { handlers[m.Name] = del; handlers[name] = del; if (lower != name) handlers[lower] = del; }
                    else
                    {
                        // fallback generic Delegate
                        var del2 = m.CreateDelegate(typeof(Action<BaseConnection, List<object?>, Dictionary<string, object?>>), this);
                        handlers[m.Name] = del2; handlers[name] = del2; if (lower != name) handlers[lower] = del2;
                    }
                }
                catch { }
            }
        }
        return handlers;
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
                    try { connection.SendCommand("echo_on"); } catch { }
                }
                // port of inputfuncs.py:277 atp.loop.call_soon_threadsafe(future.set_result, text)
                try { future.TrySetResult(text); } catch { }
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
            if (lst[0] is int a && lst[1] is int b && lst[2] is int c)
            {
                if (a==-1 && b==-1 && c==-1) return true;
                return a>=0 && a<=255 && b>=0 && b<=255 && c>=0 && c<=255;
            }
            if (lst.All(x=> x is int)) {
                var ints = lst.Cast<int>().ToArray();
                if (ints[0]==-1 && ints[1]==-1 && ints[2]==-1) return true;
                return ints.All(x=> x>=0 && x<=255);
            }
            // JsonElement case
            if (v is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Array)
            {
                var arr = je.EnumerateArray().Select(e=> e.TryGetInt32(out var iv)?iv:-999).ToArray();
                if (arr.Length!=3) return false;
                if (arr[0]==-1 && arr[1]==-1 && arr[2]==-1) return true;
                return arr.All(x=> x>=0 && x<=255);
            }
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
                foreach (var p in je.EnumerateObject()) dict[p.Name]= JsonElementToObjectLocal(p.Value);
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
        if (o is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Array) return je.EnumerateArray().Select(JsonElementToObjectLocal).ToList()!;
        return new List<object?>();
    }

    private static object? JsonElementToObjectLocal(System.Text.Json.JsonElement el)
    {
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => el.GetString(),
            System.Text.Json.JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.TryGetInt64(out var l) ? l : el.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObjectLocal).ToList(),
            System.Text.Json.JsonValueKind.Object => el.EnumerateObject().ToDictionary(p=>p.Name, p=> JsonElementToObjectLocal(p.Value)),
            _ => null
        };
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
        if (key is not string || cellsObj is not List<object?> && cellsObj is not System.Text.Json.JsonElement) {
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
        string ip = connection.ClientHost ?? "?";
        var result = Globals.MapEdit.Consume(key, ip, seq);
        if (result.Status == Globals.MapEditStatus.Reject)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ result.Reason }, new Dictionary<string, object?>());
            return;
        }
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
                // log warnings; for test we ignore
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
        string ip = connection.ClientHost ?? "?";
        var result = Globals.MapEdit.Consume(key, ip, seq);
        if (result.Status == Globals.MapEditStatus.Reject)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ result.Reason }, new Dictionary<string, object?>());
            return;
        }
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
        result.Chain.Validation = denied;
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
                foreach(var p in je.EnumerateObject()) dict[p.Name]= JsonElementToObjectLocal(p.Value);
                norm = dict;
            }
            if (!IsLegendEntry(norm))
            {
                connection.SendCommand("map_edit_reject", new List<object?>{ $"Invalid legend entry at index {idx}." }, new Dictionary<string, object?>());
                return;
            }
        }
        string ip = connection.ClientHost ?? "?";
        var result = Globals.MapEdit.Consume(key, ip, seq);
        if (result.Status == Globals.MapEditStatus.Reject)
        {
            connection.SendCommand("map_edit_reject", new List<object?>{ result.Reason }, new Dictionary<string, object?>());
            return;
        }
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
            mh.SetMapInfo(result.Chain.Area, result.Chain.Z, mi);
        }
        var newEntries = new List<LegendEntry>();
        foreach (var eObj in legend)
        {
            Dictionary<string, object?> dict;
            if (eObj is Dictionary<string, object?> d) dict=d;
            else if (eObj is System.Text.Json.JsonElement je && je.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                dict = new Dictionary<string, object?>();
                foreach(var p in je.EnumerateObject()) dict[p.Name]= JsonElementToObjectLocal(p.Value);
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
    private readonly Dictionary<string, Delegate> _messageHandlers = new(); // port of manager.py:55
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
        _settings = settings ?? AtherizSettings.Default;
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

    // Port of manager.py:65-68 generate_connection_id
    public virtual string GenerateConnectionId()
    {
        _lock.EnterWriteLock();
        try { _connectionCounter++; return $"conn_{_connectionCounter}"; }
        finally { _lock.ExitWriteLock(); }
    }

    // Port of manager.py:70-119 register_connection
    public virtual bool RegisterConnection(string connId, BaseConnection connection)
    {
        var host = connection.ClientHost ?? "?"; // port of manager.py:76
        var limit = _settings.MaxConnectionsPerIp; // port of manager.py:77
        _lock.EnterWriteLock();
        try
        {
            if (ObjectRegistry.IsIpBanned(host)) // port of manager.py:79-85
            {
                try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Refusing connection from banned host {host}"); } catch { Console.Error.WriteLine($"[Network] Refusing connection from banned host {host}"); }
                try { connection.Close(); } catch { }
                return false;
            }
            if (limit > 0 && host != "?") // port of manager.py:86-101
            {
                var sameHost = _perIpCounts.TryGetValue(host, out var cnt) ? cnt : 0;
                // if overwriting same conn_id, don't count itself twice — manager.py:88-91
                if (_connections.TryGetValue(connId, out var existing) && (existing.ClientHost ?? "?") == host)
                    sameHost--;
                if (sameHost >= limit)
                {
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Refusing connection from {host}: per-IP limit ({limit}) reached"); } catch { Console.Error.WriteLine($"[Network] Refusing connection from {host}: per-IP limit ({limit}) reached"); }
                    try { connection.Close(); } catch { }
                    return false;
                }
            }
            // handle overwrite: adjust old host count — manager.py:102-113
            if (_connections.TryGetValue(connId, out var old))
            {
                var oldHost = old.ClientHost ?? "?";
                if (oldHost != "?" && oldHost != host)
                {
                    var cnt = _perIpCounts.TryGetValue(oldHost, out var c) ? c - 1 : -1;
                    if (cnt <= 0) _perIpCounts.Remove(oldHost);
                    else _perIpCounts[oldHost] = cnt;
                }
                _connToId.Remove(old);
            }
            _connections[connId] = connection; // port of manager.py:114
            _connToId[connection] = connId; // port of manager.py:115
            if (host != "?") // port of manager.py:116-117
                _perIpCounts[host] = _perIpCounts.TryGetValue(host, out var v) ? v + 1 : 1;
        }
        finally { _lock.ExitWriteLock(); }
        try { Atheriz.Core.AtherizLogger.LogInformation($"[Network] Connection opened: {connId} (total: {ConnectionCount})"); } catch { Console.Error.WriteLine($"[Network] Connection opened: {connId} (total: {ConnectionCount})"); } // port of manager.py:118
        return true;
    }

    // Port of manager.py:121-153 disconnect
    public virtual void Disconnect(BaseConnection connection)
    {
        string? connId = null;
        var host = connection.ClientHost ?? "?"; // port of manager.py:123
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
            else if (connId == null && connection != null)
            {
                // legacy fallback without O(N) scan under lock: lookup via id map only — manager.py:134-136
            }
        }
        finally { _lock.ExitWriteLock(); }

        if (string.IsNullOrEmpty(connId)) return; // port of manager.py:138

        lock (connection.Lock) { connection.SetDisconnected(true); } // port of manager.py:139-140
        connection.ClearPendingInput(); // port of manager.py:141
        var session = connection.Session; // port of manager.py:142
        if (session != null)
        {
            // port of manager.py:144-148 run session teardown on game threadpool
            if (!Atp.AddTask(() => DoSessionDisconnect(session)))
            {
                try { session.AtDisconnect(); }
                catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Session teardown failed during disconnect: {e}"); } catch { Console.Error.WriteLine($"[Network] Session teardown failed during disconnect: {e}"); } }
            }
        }
        try { connection.Close(); } // port of manager.py:149-152
        catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[Network] Connection cleanup failed: {e}"); } catch { Console.Error.WriteLine($"[Network] Connection cleanup failed: {e}"); } }
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
        _lock.EnterWriteLock();
        try { _messageHandlers[messageType] = handler; _messageHandlers[messageType.ToLowerInvariant()] = handler; } // port of manager.py:183
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
            var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1) // port of manager.py:194
            {
                var host = connection.ClientHost ?? "?"; // port of manager.py:195
                if (ShouldLogMalformed(host)) // port of manager.py:196
                    try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Invalid message format from {host} ({rawMessage.Length} bytes): {SummarizeRaw(rawMessage)}"); } catch { Console.Error.WriteLine($"[Network] Invalid message format from {host} ({rawMessage.Length} bytes): {SummarizeRaw(rawMessage)}"); } // port of manager.py:197-199
                return;
            }
            var cmdElement = root[0];
            var cmd = cmdElement.GetString() ?? cmdElement.ToString(); // port of manager.py:202
            List<object?> args = new(); // port of manager.py:203
            Dictionary<string, object?> kwargs = new(); // port of manager.py:204
            if (root.GetArrayLength() > 1) args = JsonElementToList(root[1]);
            if (root.GetArrayLength() > 2) kwargs = JsonElementToDict(root[2]);

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
        var lowerCmd = cmd.ToLowerInvariant();
        _lock.EnterReadLock();
        try { _messageHandlers.TryGetValue(lowerCmd, out handler); } // port of manager.py:225
        finally { _lock.ExitReadLock(); }
        if (handler != null) // port of manager.py:226-227
        {
            connection.EnqueueInput(handler, args, kwargs);
        }
        else
        {
            try { Atheriz.Core.AtherizLogger.LogDebug($"Unknown command: {cmd}"); } catch { Console.Error.WriteLine($"Unknown command: {cmd}"); } // port of manager.py:229 (logger.debug)
        }
    }

    // Helpers to convert JsonElement to List/Dict of objects
    private static List<object?> JsonElementToList(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return new List<object?> { JsonElementToObject(el) };
        var list = new List<object?>();
        foreach (var item in el.EnumerateArray()) list.Add(JsonElementToObject(item));
        return list;
    }

    private static Dictionary<string, object?> JsonElementToDict(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();
        var dict = new Dictionary<string, object?>();
        foreach (var prop in el.EnumerateObject()) dict[prop.Name] = JsonElementToObject(prop.Value);
        return dict;
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => JsonElementToList(el),
            JsonValueKind.Object => JsonElementToDict(el),
            _ => null
        };
    }

    // For tests / introspection — expose internal state counts similar to Python's _connections
    public IReadOnlyDictionary<string, BaseConnection> ConnectionsSnapshot
    {
        get { _lock.EnterReadLock(); try { return new Dictionary<string, BaseConnection>(_connections); } finally { _lock.ExitReadLock(); } }
    }
}
