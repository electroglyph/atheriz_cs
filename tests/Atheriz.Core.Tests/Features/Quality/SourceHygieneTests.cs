using System.Text.RegularExpressions;

namespace Atheriz.Core.Tests.Features.Quality;

// Source-hygiene policy suite for production code: bans reflection/dynamic/
// sleeps/console/bare-catch patterns and test-only seams in production, and
// prescribes file organization. Each test FAILS while the violation is present
// and PASSES once removed. Production code is untouched.
//
// Method: source scan of src/ (comments stripped). Absolute path with CWD
// fallback mirrors PortedAtherizMainTests / PortedCriticalFixesTests precedent.
[Collection("Ported")]
public class SourceHygieneTests
{
    private static string SrcRoot()
    {
        var candidates = new[]
        {
            "/home/anon/atheriz-cs",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        };
        foreach (var c in candidates)
            if (Directory.Exists(Path.Combine(c, "src"))) return c;
        var cwd = Directory.GetCurrentDirectory();
        var d = new DirectoryInfo(cwd);
        while (d != null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src"))) return d.FullName;
            d = d.Parent;
        }
        throw new InvalidOperationException("repo root with src/ not found");
    }

    private static List<(string File, int Line, string Text)> Scan(
        string[] areas, string pattern, string[]? excludeFiles = null, string? fileNameContains = null)
    {
        var root = SrcRoot();
        var rx = new Regex(pattern);
        var hits = new List<(string, int, string)>();
        foreach (var area in areas)
        {
            var dir = Path.Combine(root, "src", area);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (fileNameContains != null && !Path.GetFileName(file).Contains(fileNameContains)) continue;
                if (excludeFiles != null && excludeFiles.Any(e => file.EndsWith(e))) continue;
                var lines = File.ReadAllLines(file);
                bool inBlock = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var ln = lines[i];
                    // strip /* */ blocks (naive, single/multi-line)
                    while (true)
                    {
                        if (inBlock)
                        {
                            int end = ln.IndexOf("*/", StringComparison.Ordinal);
                            if (end < 0) { ln = ""; break; }
                            ln = ln.Substring(end + 2);
                            inBlock = false;
                        }
                        else
                        {
                            int start = ln.IndexOf("/*", StringComparison.Ordinal);
                            if (start < 0) break;
                            int end = ln.IndexOf("*/", start + 2, StringComparison.Ordinal);
                            if (end < 0) { ln = ln.Substring(0, start); inBlock = true; break; }
                            ln = ln.Substring(0, start) + ln.Substring(end + 2);
                        }
                    }
                    int c = ln.IndexOf("//", StringComparison.Ordinal);
                    if (c >= 0) ln = ln.Substring(0, c);
                    if (rx.IsMatch(ln)) hits.Add((file, i + 1, lines[i].Trim()));
                }
            }
        }
        return hits;
    }

    private static void AssertNoHits(List<(string File, int Line, string Text)> hits, string rule)
    {
        Assert.True(hits.Count == 0,
            $"{rule}: {hits.Count} violation(s):\n" +
            string.Join("\n", hits.Take(15).Select(h => $"{h.File}:{h.Line}: {h.Text}")));
    }

    private static readonly string[] CoreAll = ["Atheriz.Core"];
    private static readonly string[] Commands = ["Atheriz.Core/Commands"];
    private static readonly string[] Objects = ["Atheriz.Core/Objects"];
    private static readonly string[] GlobalsPersist =
        ["Atheriz.Core/Globals", "Atheriz.Core/Persistence", "Atheriz.Core/Settings"];
    private static readonly string[] NetConcurrency =
        ["Atheriz.Core/Network", "Atheriz.Core/Concurrency"];
    private static readonly string[] ServerUtils =
        ["Atheriz.Server", "Atheriz.Core/Utils", "Atheriz.Core/Plugins"];

    private const string ReflectionPat =
        @"\.GetMethod\(|\.GetProperty\(|\.GetField\(|Activator\.CreateInstance|GetTypes\(\)|\.DynamicInvoke\(|Type\.GetType\(";

    // --- Reflection guard: production uses explicit typed members, never reflection ---

    [Fact] public void NoReflection_Commands() =>
        AssertNoHits(Scan(Commands, ReflectionPat), "reflection in Commands");

    [Fact] public void NoReflection_Objects() =>
        AssertNoHits(Scan(Objects, ReflectionPat), "reflection in Objects");

    [Fact] public void NoReflection_GlobalsPersistence() =>
        AssertNoHits(Scan(GlobalsPersist, ReflectionPat), "reflection in Globals/Persistence");

    [Fact] public void NoReflection_NetworkConcurrency() =>
        AssertNoHits(Scan(NetConcurrency, ReflectionPat), "reflection in Network/Concurrency");

    [Fact] public void NoReflection_ServerUtils() =>
        AssertNoHits(Scan(ServerUtils, ReflectionPat), "reflection in Server/Utils");

    // --- Dynamic guard: production uses static typing, never dynamic dispatch ---

    [Fact] public void NoDynamic_Network() =>
        AssertNoHits(Scan(NetConcurrency, @"\(\(dynamic\)|\bFunc<dynamic,"),
            "dynamic in Network/Concurrency");

    [Fact] public void NoDynamic_Objects_Commands() =>
        AssertNoHits(Scan(["Atheriz.Core/Objects", "Atheriz.Core/Commands"], @"\(\(dynamic\)"),
            "((dynamic)) casts in Objects/Commands");

    // --- Sleep/console/bare-catch guard: no blocking sleeps, console logging, or empty catches ---

    [Fact] public void NoThreadSleep_Production() =>
        AssertNoHits(Scan(["Atheriz.Core", "Atheriz.Server"], @"Thread\.Sleep\("),
            "Thread.Sleep in production");

    [Fact] public void NoConsoleLogging_CoreLibrary() =>
        AssertNoHits(Scan(["Atheriz.Core"], @"Console\.(Error|WriteLine|Write)\("),
            "Console logging in Atheriz.Core (must go through AtherizLogger)");

    [Fact] public void NoBareCatch_Commands() =>
        AssertNoHits(Scan(Commands, @"catch\s*\{\s*\}"), "bare catch{} in Commands");

    [Fact] public void NoBareCatch_Objects() =>
        AssertNoHits(Scan(Objects, @"catch\s*\{\s*\}"), "bare catch{} in Objects");

    [Fact] public void NoBareCatch_GlobalsPersistence() =>
        AssertNoHits(Scan(GlobalsPersist, @"catch\s*\{\s*\}"), "bare catch{} in Globals/Persistence");

    // --- Test-seam guard: production holds no test-only hooks or duplicated helpers ---

    [Fact] public void NoTestSeams_Production() =>
        AssertNoHits(Scan(["Atheriz.Core", "Atheriz.Server"],
            @"ForTesting|CallCount|AtCreateHook|AtDeleteHook|RawQueueCount|BusyLock\s*=>|TestSerializeHook|SetLockForTesting|IncrementTracker|Override\s*\{"),
            "test-only seams in production");

    [Fact] public void SingleLockScope_Production() =>
        Assert.True(Scan(CoreAll, @"(private|internal|public)[^;{]*\bclass LockScope\b").Count <= 1,
            "LockScope must be defined once (shared), not copy-pasted per handler");

    [Fact] public void NoClosedStringMatching_Production() =>
        AssertNoHits(Scan(CoreAll, @"\.Message\.Contains\(\s*""closed"""),
            "string-matched closed-DB detection (use IsClosed / dedicated exception)");

    [Fact] public void RequirePuppetHelper_Exists() =>
        Assert.True(Scan(Commands, @"RequirePuppet").Count > 0,
            "the ~35x RequirePuppet guard must be extracted to one helper");

    // --- File organization: prescribed file and folder layout ---

    private static bool ProdFileExists(params string[] parts)
    {
        var p = Path.Combine(new[] { SrcRoot(), "src" }.Concat(parts).ToArray());
        return File.Exists(p) || Directory.Exists(p);
    }

    private static int ProdMatchCount(string[] areas, string pattern) =>
        Scan(areas, pattern).Count;

    [Fact] public void Org_GameObjectDelete_SplitFromPuppet() =>
        Assert.True(ProdFileExists("Atheriz.Core", "Objects", "GameObject.Delete.cs"),
            "190-line Delete must move out of GameObject.Puppet.cs");

    [Fact] public void Org_GameObjectLook_SplitFromMove() =>
        Assert.True(ProdFileExists("Atheriz.Core", "Objects", "GameObject.Look.cs"),
            "ExecuteCommand/AtLook/ReturnAppearance must move out of GameObject.Move.cs");

    [Fact] public void Org_Transition_MergedIntoNodeGrid() =>
        Assert.False(ProdFileExists("Atheriz.Core", "Objects", "Transition.cs"),
            "21-line Transition must merge into NodeGrid.cs");

    [Fact] public void Org_NodeLinks_Renamed() =>
        Assert.True(ProdFileExists("Atheriz.Core", "Objects", "Node.Links.cs"),
            "Node.Partial.cs must be renamed to Node.Links.cs");

    [Fact] public void Org_FuncParser_Folder() =>
        Assert.True(ProdFileExists("Atheriz.Core", "Objects", "FuncParser"),
            "FuncParser.cs + FuncParserHelpers.cs belong in Objects/FuncParser/");

    [Fact] public void Org_ExamFormatter_Extracted() =>
        Assert.True(ProdMatchCount(Commands, @"GetField|GetProperty") == 0,
            "ExamCommand must delegate to a typed ExamFormatter (no reflection)");

    [Fact] public void Org_NodeDtos_LiveInPersistence() =>
        Assert.True(ProdMatchCount(["Atheriz.Core/Globals"], @"class Node(Dto|GridDto|AreaDto)\b") == 0,
            "Node DTOs must move from NodeHandler.cs to Persistence/");

    [Fact] public void Org_SingleMapHandlerSingleton() =>
        Assert.True(ProdMatchCount(CoreAll, @"MapHandlerHolder") == 0,
            "MapHandlerHolder must go; Door uses the MoveTo resolver");

    [Fact] public void Org_Hooks_SingleFile() =>
        Assert.False(ProdFileExists("Atheriz.Core", "Objects", "Hooks"),
            "Hooks/ (63+10 lines) must merge into one Hooks.cs");

    [Fact] public void Org_BaseChannelCommand_LivesInCommands() =>
        Assert.False(ProdFileExists("Atheriz.Core", "Objects", "BaseChannelCommand.cs"),
            "BaseChannelCommand references Commands/registry; it belongs in Commands/");

    [Fact] public void Org_StopHandler_Split() =>
        Assert.True(ProdFileExists("Atheriz.Server", "Cli", "ShutdownClient.cs"),
            "653-line StopHandler must split (ShutdownClient/DaemonSpawner/...)");

    [Fact] public void Org_StopHandler_Small() =>
        Assert.True(
            File.ReadAllLines(Path.Combine(SrcRoot(), "src", "Atheriz.Server", "Cli", "StopHandler.cs")).Length < 200,
            "StopHandler.cs must be under 200 lines after the split");

    [Fact] public void Org_NoIsPortListeningDup() =>
        Assert.True(ProdMatchCount(["Atheriz.Server"], @"IsPortListeningStatic") == 0,
            "IsPortListeningStatic duplicates PidFile.IsPortListening; merge them");

    [Fact] public void Org_Protocol_Collapsed() =>
        Assert.True(ProdMatchCount(["Atheriz.Core/Network"], @"class Protocol\b") == 0,
            "vacuous Protocol : BaseProtocol must collapse to one typed concept");

    [Fact] public void Org_JsonTableLoader_TwoHelpers() =>
        Assert.True(Scan(["Atheriz.Core/Persistence"], @"public static .*Load\w+").Count <= 2,
            "JsonTableLoader keeps two helpers (buffered + lock-aware), not four");

    // --- Structural oracles: duplicated helpers with zero behavioral delta ---

    [Fact] public void Dup_JsonConverters_Unified() =>
        Assert.True(Scan(["Atheriz.Core/Network"], @"static\s+\S.*\sJsonElementTo(Object|List|Dict)\s*\(").Count <= 1,
            "JsonElement converters must be one family, not Local + List/Dict/Object");

    [Fact] public void Dup_MapWebSocketAlias_Removed() =>
        Assert.True(Scan(["Atheriz.Server/Hosting"], @"MapWebSocketAsync").Count == 0,
            "MapWebSocketAsync duplicates the entry point; delete the alias");

    [Fact] public void Dup_GameTemplateCtorOverload_Removed() =>
        Assert.True(Scan(["Atheriz.Server/Infrastructure"], @"public static void CreateGameFolder").Count <= 1,
            "the (string,string,bool) CreateGameFolder overload is a pointless alias");

    [Fact] public void Dup_SeqConsumeAck_Unified() =>
        Assert.True(Scan(["Atheriz.Core/Network"], @"ConsumeOrReply").Count > 0,
            "the 3x seq/key/consume/ack cycle must be one ConsumeOrReply() helper");

    [Fact] public void Dup_GetAllDistinct_Removed() =>
        Assert.True(Scan(["Atheriz.Core/Commands"], @"GetAllDistinct").Count == 0,
            "unused GetAllDistinct duplicates GetAll; delete it");

    [Fact] public void Dup_CommandKeys_SingleCaseInsensitiveDict() =>
        Assert.True(Scan(["Atheriz.Core/Network"], @"OrdinalIgnoreCase", fileNameContains: "ConnectionManager.cs").Count > 0,
            "case-insensitive dispatch must be one OrdinalIgnoreCase dict, not Pascal+lower key triplication");

    // --- Host behavior: structural oracles for CLI and hosting ---

    [Fact] public void Host_KestrelBadHost_ThrowsInsteadOfBindingAnywhere() =>
        Assert.True(Scan(["Atheriz.Server/Hosting"], @"ip = IPAddress\.Any", fileNameContains: "KestrelConfig.cs").Count == 0,
            "unparseable listen host must throw, not silently bind 0.0.0.0");

    [Fact] public void Host_ShutdownClient_Unified() =>
        Assert.True(Scan(["Atheriz.Server/Cli"], @"PostAdminAsync").Count > 0,
            "the 3x HTTP shutdown client must be one PostAdminAsync helper");

    [Fact] public void Host_NoEnvironmentExit_InCli() =>
        Assert.True(Scan(["Atheriz.Server/Cli"], @"Environment\.Exit").Count == 0,
            "Environment.Exit must not live in a library method (HandleTest)");

    [Fact] public void Host_RestartForwardsForegroundFlag() =>
        Assert.True(Scan(["Atheriz.Server/Cli"], @"Task<bool> HandleRestartAsync", fileNameContains: "RestartHandler.cs").Count > 0,
            "RestartHandler must preserve StopHandler's Task<bool> foreground flag");

    [Fact] public void Host_PidFile_Fsyncs() =>
        Assert.True(Scan(["Atheriz.Server/Infrastructure"], @"Flush\(true\)", fileNameContains: "PidFile.cs").Count > 0,
            "pid file must Flush(true) like AdminToken (crash can leave an empty pid file)");
}
