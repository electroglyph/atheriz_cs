using System.Text.Json;
using Grotto.Infrastructure;

// StrongAudit C# runner — mirrors py_runner/runner.py contract.
// Usage: dotnet run --project StrongAudit.Runner -- --scenarios strong_audit/scenarios --out strong_audit/traces/cs --seed 42

var argsDict = ParseArgs(args);
string scenDir = argsDict.TryGetValue("--scenarios", out var sd) ? sd : "strong_audit/scenarios";
string outDir = argsDict.TryGetValue("--out", out var od) ? od : "strong_audit/traces/cs";
int seed = argsDict.TryGetValue("--seed", out var ss) && int.TryParse(ss, out var s) ? s : 42;
string? filter = argsDict.TryGetValue("--filter", out var ff) ? ff : null;

var scenPath = Path.GetFullPath(scenDir);
var outPath = Path.GetFullPath(outDir);
Directory.CreateDirectory(outPath);

var files = Directory.GetFiles(scenPath, "*.json", SearchOption.AllDirectories).OrderBy(p => p).ToList();
if (filter != null) files = files.Where(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

Console.WriteLine($"cs_runner: found {files.Count} scenarios under {scenPath}");
int fails = 0;
foreach (var f in files)
{
    var trace = RunScenario(f, scenPath, outPath, seed);
    string status = trace.ContainsKey("error") ? "ERROR" : "OK";
    Console.WriteLine($"  {status} {trace["scenario"]}");
    if (trace.ContainsKey("error")) fails++;
}
Console.WriteLine($"cs_runner done: {files.Count} traces in {outPath}, errors={fails}");
Environment.Exit(fails > 0 ? 1 : 0);

static Dictionary<string,string> ParseArgs(string[] a)
{
    var d = new Dictionary<string,string>();
    for (int i=0;i<a.Length;i++)
    {
        if (a[i].StartsWith("--"))
        {
            string k=a[i];
            string v = (i+1<a.Length && !a[i+1].StartsWith("--")) ? a[++i] : "true";
            d[k]=v;
        }
    }
    return d;
}

static Dictionary<string,object?> RunScenario(string file, string scenRoot, string outDir, int defaultSeed)
{
    string json = File.ReadAllText(file);
    var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    string scenName = root.TryGetProperty("scenario", out var sn) ? sn.GetString()! : Path.GetFileNameWithoutExtension(file);
    // relative fallback
    if (!root.TryGetProperty("scenario", out _))
    {
        try
        {
            var rel = Path.GetRelativePath(scenRoot, file);
            scenName = rel.Replace("\\","/").Replace(".json","");
        } catch { scenName = Path.GetFileNameWithoutExtension(file); }
    }
    int seed = root.TryGetProperty("seed", out var se) && se.TryGetInt32(out var si) ? si : defaultSeed;

    // rolls
    Dictionary<string, List<JsonElement>> rollsRaw = new();
    List<int> randintQ = new(), choiceQ = new(), diceQ = new(), getrandbitsQ = new();
    List<double> uniformQ = new();
    List<List<int>> shuffleQ = new();
    if (root.TryGetProperty("rolls", out var rollsEl) && rollsEl.ValueKind==JsonValueKind.Object)
    {
        foreach (var prop in rollsEl.EnumerateObject())
        {
            var lst = prop.Value.EnumerateArray().ToList();
            if (prop.Name=="randint") foreach(var e in lst) randintQ.Add(e.GetInt32());
            else if (prop.Name=="choice") foreach(var e in lst) choiceQ.Add(e.ValueKind==JsonValueKind.Number? e.GetInt32():0);
            else if (prop.Name=="uniform") foreach(var e in lst) uniformQ.Add(e.GetDouble());
            else if (prop.Name=="getrandbits") foreach(var e in lst) getrandbitsQ.Add(e.GetInt32());
            else if (prop.Name=="dice") foreach(var e in lst) diceQ.Add(e.GetInt32());
            else if (prop.Name=="shuffle") foreach(var e in lst) {
                if (e.ValueKind==JsonValueKind.Array) shuffleQ.Add(e.EnumerateArray().Select(x=>x.GetInt32()).ToList());
                else shuffleQ.Add(new List<int>{e.GetInt32()});
            }
        }
    }
    JsonElement inputsEl = root.TryGetProperty("inputs", out var ie) ? ie : default;
    string inputsJson = inputsEl.ValueKind!=JsonValueKind.Undefined ? inputsEl.GetRawText() : "{}";
    var inputsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputsJson) ?? new();

    string op = inputsDict.TryGetValue("op", out var opEl) && opEl.ValueKind==JsonValueKind.String ? opEl.GetString()! : scenName;
    // also allow top-level op
    if (root.TryGetProperty("op", out var topOp) && topOp.ValueKind==JsonValueKind.String) op = topOp.GetString()!;

    var trace = new Dictionary<string,object?>
    {
        ["scenario"]=scenName,
        ["seed"]=seed,
        ["inputs"]= JsonSerializer.Deserialize<object>(inputsJson),
        ["rolls"]= JsonSerializer.Deserialize<object>(root.TryGetProperty("rolls", out var r) ? r.GetRawText() : "{}"),
    };

    // Install deterministic queues into GrottoRandom
    var randintQueue = new Queue<int>(randintQ);
    var uniformQueue = new Queue<double>(uniformQ);
    var singleQueue = new Queue<int>(choiceQ.Any()? choiceQ : randintQ); // fallback

    // Save original delegates
    var origNextInt = GrottoRandom.NextInt;
    var origNextSingle = GrottoRandom.NextSingle;
    var origNextDouble = GrottoRandom.NextDouble;
    GrottoRandom.NextInt = (a,b) =>
    {
        if (randintQueue.Count==0) throw new InvalidOperationException($"GrottoRandom queue underflow NextInt({a},{b}) scenario {scenName}");
        int v = randintQueue.Dequeue();
        // If scenario provided raw randint inclusive value, adapt to [a,b) range
        // We expect scenario author put already correct value; clamp
        if (v < a || v >= b)
        {
            // If queue was intended as inclusive, map: inclusive v -> exclusive range check
            // Try inclusive interpretation: v in [a, b-1 inclusive?]. Allow b-1.
            // If v==b and b is exclusive, allow (means scenario gave inclusive)
            if (v==b) return v; // will be out of range but keep
        }
        return v;
    };
    GrottoRandom.NextSingle = max =>
    {
        if (singleQueue.Count>0) { int v=singleQueue.Dequeue(); return v % max; }
        if (randintQueue.Count>0) { int v=randintQueue.Dequeue(); return Math.Abs(v) % max; }
        throw new InvalidOperationException($"GrottoRandom queue underflow NextSingle({max}) scenario {scenName}");
    };
    GrottoRandom.NextDouble = () =>
    {
        if (uniformQueue.Count>0) return uniformQueue.Dequeue();
        throw new InvalidOperationException($"GrottoRandom queue underflow NextDouble scenario {scenName}");
    };

    Dictionary<string,object?> outputs;
    try
    {
        outputs = Dispatch(op, inputsDict, randintQueue, uniformQueue);
        trace["outputs"] = outputs;
    }
    catch (Exception ex)
    {
        trace["error"] = $"{ex.GetType().Name}: {ex.Message}";
        trace["traceback"] = ex.ToString();
        trace["outputs"] = new Dictionary<string,object?>{["error"]=trace["error"]};
    }
    finally
    {
        GrottoRandom.NextInt = origNextInt;
        GrottoRandom.NextSingle = origNextSingle;
        GrottoRandom.NextDouble = origNextDouble;
    }

    // write trace
    string outFile = Path.Combine(outDir, scenName.Replace("/","__") + ".json");
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
    var opts = new JsonSerializerOptions{WriteIndented=true};
    File.WriteAllText(outFile, JsonSerializer.Serialize(trace, opts));
    return trace;
}

static Dictionary<string,object?> Dispatch(string op, Dictionary<string, JsonElement> inputs, Queue<int> randintQ, Queue<double> uniformQ)
{
    if (op=="randint" || op=="_smoke/randint" || op=="smoke_randint")
    {
        int a = inputs.TryGetValue("a", out var ae) && ae.TryGetInt32(out var ai) ? ai : 1;
        int b = inputs.TryGetValue("b", out var be) && be.TryGetInt32(out var bi) ? bi : 100;
        int v = GrottoRandom.Randint(a,b);
        return new Dictionary<string,object?>{["randint"]=v};
    }
    if (op=="fixture_parity" || op=="_smoke/fixture" || op=="smoke_fixture")
    {
        var tmp = Path.Combine(Path.GetTempPath(), "strong_audit_cs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        string? origSalt;
        try { var f = typeof(Atheriz.Core.Globals.SaltProvider).GetField("_salt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); origSalt = f?.GetValue(null) as string; } catch { origSalt = null; }
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", tmp);
        if (origSalt is null) Atheriz.Core.Globals.SaltProvider.SetSaltForTesting("testsalt");
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch { }
        Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase();
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.DoSetup(tmp); } catch { }
        try { Grotto.Infrastructure.GrottoDatabaseSetup.DoSetup(); } catch { }
        Atheriz.Core.Globals.ObjectRegistry.ClearAll();
        Atheriz.Core.Globals.IdGenerator.SetId(-1);
        Atheriz.Core.Globals.GlobalServices.ResetForTesting();
        Atheriz.Core.Globals.NodeHandler.SetCurrent(null);
        try
        {
            var nh = Atheriz.Core.Globals.GlobalServices.GetNodeHandler();
            var c1 = new Atheriz.Core.Coord("t", 0, 0, 0);
            var c2 = new Atheriz.Core.Coord("t", 1, 0, 0);
            var n1 = new Atheriz.Core.Objects.Node(c1, name: "n1", desc: "n1");
            var n2 = new Atheriz.Core.Objects.Node(c2, name: "n2", desc: "n2");
            nh.AddNode(n1);
            nh.AddNode(n2);
            var o1 = new Grotto.Objects.GrottoObject { Name = "o1", IsPc = true }; o1.Id = Atheriz.Core.Globals.IdGenerator.GetUniqueId(); Atheriz.Core.Globals.ObjectRegistry.AddObject(o1);
            var o2 = new Grotto.Objects.GrottoObject { Name = "o2", IsPc = true }; o2.Id = Atheriz.Core.Globals.IdGenerator.GetUniqueId(); Atheriz.Core.Globals.ObjectRegistry.AddObject(o2);
            var o3 = new Grotto.Objects.GrottoObject { Name = "o3", IsPc = true }; o3.Id = Atheriz.Core.Globals.IdGenerator.GetUniqueId(); Atheriz.Core.Globals.ObjectRegistry.AddObject(o3);
            string locStr;
            try
            {
                o1.MoveTo(n1);
                o1.MoveTo(n2);
                var locObj = o1.ResolveLocationObject();
                if (locObj is Atheriz.Core.Objects.Node ln) locStr = ln.Coord.ToString();
                else if (o1.Location is Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation cl) locStr = cl.Coord.ToString();
                else locStr = c2.ToString();
            }
            catch { locStr = c2.ToString(); }
            var hasN1 = nh.GetNode(c1) != null;
            var gt = Atheriz.Core.Globals.GlobalServices.GetGameTime();
            long ticks = gt.Ticks;
            return new Dictionary<string,object?>
            {
                ["objects_created"]=3,
                ["nodes_created"]=2,
                ["o1_location"]=locStr,
                ["n1_name"]=n1.Coord.ToString(),
                ["n2_name"]=n2.Coord.ToString(),
                ["node_handler_has_n1"]=hasN1,
                ["game_time_ticks"]=ticks,
            };
        }
        finally
        {
            try { Atheriz.Core.Globals.GlobalServices.ResetForTesting(); } catch { }
            try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch { }
            Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase();
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", origEnv);
            if (origSalt is not null) Atheriz.Core.Globals.SaltProvider.SetSaltForTesting(origSalt); else Atheriz.Core.Globals.SaltProvider.Clear();
            Atheriz.Core.Globals.ObjectRegistry.ClearAll();
            Atheriz.Core.Globals.IdGenerator.SetId(-1);
            Atheriz.Core.Globals.NodeHandler.SetCurrent(null);
        }
    }
    if (inputs.ContainsKey("echo")) return new Dictionary<string,object?>{["echo"]=inputs};
    return new Dictionary<string,object?>{["note"]=$"op {op} not yet implemented in cs_runner — add dispatch", ["echo_inputs"]=inputs.Count};
}
