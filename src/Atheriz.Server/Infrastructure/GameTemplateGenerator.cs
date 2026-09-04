// Port of atheriz/new.py:541 create_game_folder + initial_setup.py:48
using System.Text.RegularExpressions;
using Atheriz.Core.Utils;
namespace Atheriz.Server.Infrastructure;
/// <summary>Generates game folder — C# analogue of <c>atheriz new my_game</c>. Mirrors <c>new.py:create_game_folder</c>.</summary>
public static class GameTemplateGenerator
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {"False","None","True","and","as","assert","async","await","break","class","continue","def","del","elif","else","except","finally","for","from","global","if","import","in","is","lambda","nonlocal","not","or","pass","raise","return","try","while","with","yield","abstract","base","bool","byte","case","catch","char","checked","const","decimal","default","delegate","do","double","enum","event","explicit","extern","false","fixed","float","foreach","goto","implicit","int","interface","internal","lock","long","namespace","new","null","object","operator","out","override","params","private","protected","public","readonly","ref","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile"};
    private static bool IsValidId(string n) => !string.IsNullOrEmpty(n) && n != "." && !char.IsDigit(n[0]) && Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]*$") && !Keywords.Contains(n);
    public static void CreateGameFolder(string targetPath, string gameName, bool overwrite = false) => CreateInternal(targetPath, gameName, overwrite);
    public static void CreateGameFolder(string targetPath, bool overwrite = false) => CreateInternal(targetPath, null, overwrite);
    private static void CreateInternal(string targetPath, string? gameName, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) { Console.WriteLine("Error: folder name cannot be empty."); return; }
        var trimmed = targetPath.Trim();
        var raw = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(raw)) raw = trimmed;
        if (string.IsNullOrEmpty(raw) || raw == "." || !Regex.IsMatch(raw, @"^[A-Za-z_][A-Za-z0-9_]*$") || Keywords.Contains(raw) || char.IsDigit(raw[0]))
        {
            Console.WriteLine($"Error: '{raw}' is not a valid Python identifier (hyphens/digits/spaces not allowed).");
            return;
        }
        var gName = string.IsNullOrWhiteSpace(gameName) ? raw : gameName!.Trim();
        if (!IsValidId(gName)) { Console.WriteLine($"Error: '{gName}' is not a valid Python identifier (hyphens/digits/spaces not allowed)."); return; }
        if (GameUtils.IsInGameFolder()) Console.WriteLine("Warning: already inside a game folder; creating nested game folder is not recommended.");
        var folderPath = Path.GetFullPath(targetPath);
        bool folderExistsInitially = Directory.Exists(folderPath);
        if (folderExistsInitially && !overwrite)
        {
            Console.Write($"Folder '{targetPath}' already exists. Replace files? (y/n): ");
            var ans = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (ans != "y")
            {
                Console.WriteLine("Aborted.");
                return;
            }
        }
        // Decide if we need to (re)create world — fresh folder OR overwrite forces fresh DB
        bool shouldSetup = !folderExistsInitially || overwrite;
        // When overwriting an existing folder, wipe stale DB so DoSetup starts fresh (handles `new test --overwrite` bare-name case)
        if (overwrite && folderExistsInitially)
        {
            try
            {
                var saveDirForWipe = Path.Combine(folderPath, "save");
                if (Directory.Exists(saveDirForWipe))
                {
                    foreach (var f in Directory.GetFiles(saveDirForWipe, "*", SearchOption.AllDirectories))
                        try { File.Delete(f); } catch { }
                }
                var dbFile = Path.Combine(folderPath, "save", "database.sqlite3");
                foreach (var f in new[] { dbFile, dbFile + "-wal", dbFile + "-shm", dbFile + ".journal" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
            } catch { }
        }
        string? username = null;
        string? password = null;
        if (shouldSetup)
        {
            username = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
            if (string.IsNullOrEmpty(username))
            {
                if (overwrite && folderExistsInitially && Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Error: ATHERIZ_SUPERUSER_USERNAME must be set for non-interactive overwrite.");
                    return;
                }
                Console.Write("Enter superuser username: ");
                username = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(username))
                {
                    Console.WriteLine("Error: Username cannot be empty.");
                    return;
                }
            }
            password = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
            if (string.IsNullOrEmpty(password))
            {
                if (overwrite && folderExistsInitially && Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Error: ATHERIZ_SUPERUSER_PASSWORD must be set for non-interactive overwrite.");
                    return;
                }
                Console.Write("Enter superuser password: ");
                try
                {
                    var sb = new System.Text.StringBuilder();
                    ConsoleKeyInfo k;
                    while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
                    {
                        if (k.Key == ConsoleKey.Backspace && sb.Length > 0) sb.Length--;
                        else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
                    }
                    Console.WriteLine();
                    password = sb.ToString().Trim();
                }
                catch { password = Console.ReadLine()?.Trim(); }
                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("Error: Password cannot be empty.");
                    return;
                }
            }
        }
        Console.WriteLine($"Creating game folder: {targetPath}");
        Directory.CreateDirectory(folderPath);
        try { Scaffold(folderPath, gName); } catch (Exception ex) { Console.Error.WriteLine($"Error scaffolding game folder: {ex.Message}"); return; }
        // Port of new.py:731 copy_web_folder — copy web (templates + static)
        try { CopyWebFolder(folderPath); } catch (Exception ex) { Console.Error.WriteLine($"Warning: could not copy web folder: {ex.Message}"); }
        var savePath = Path.Combine(folderPath, "save"); Directory.CreateDirectory(savePath);
        FsUtil.TryChmod0700(savePath);
        var secretPath = Path.Combine(folderPath, "secret"); Directory.CreateDirectory(secretPath);
        FsUtil.TryChmod0700(secretPath);
        try { var gi = Path.Combine(folderPath, ".gitignore"); if (!File.Exists(gi)) File.WriteAllText(gi, "save/\nsecret/\nbin/\nobj/\n"); } catch { }
        if (shouldSetup)
        {
            Console.WriteLine("\nSetting up initial world state...");
            try
            {
                var absSaveForSetup = Path.GetFullPath(savePath);
                var absSecretForSetup = Path.GetFullPath(secretPath);
                Atheriz.Core.InitialSetup.DoSetup(absSaveForSetup, username, password, absSecretForSetup);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: initial world setup failed: {ex.Message}");
                Console.WriteLine("  Run `create` to add superuser later or set ATHERIZ_SUPERUSER_USERNAME/PASSWORD and re-run.");
            }
        }
        Console.WriteLine($"\nSuccess! Game folder '{targetPath}' created/updated with:");
        Console.WriteLine("  Template files:");
        Console.WriteLine("    - GameSettings.cs, CustomObject.cs, CustomNode.cs, CustomAccount.cs, CustomChannel.cs, CustomScript.cs");
        Console.WriteLine($"    - {gName}.csproj (refs Atheriz.Core)");
        Console.WriteLine("    - README.md, save/, secret/");
        Console.WriteLine("    - web/ (templates and static files)");
        if (shouldSetup)
        {
            Console.WriteLine("  Initial world:");
            Console.WriteLine($"    - Superuser account: {username}");
            Console.WriteLine($"    - Starting room at {Atheriz.Core.Settings.AtherizSettings.Default.DefaultHome}");
        }
    }
    private static void Scaffold(string folderPath, string gameName)
    {
        var csprojName = gameName + ".csproj"; var csprojPath = Path.Combine(folderPath, csprojName);
        string coreRef = "../src/Atheriz.Core/Atheriz.Core.csproj";
        try
        {
            var asmDir = Path.GetDirectoryName(typeof(GameTemplateGenerator).Assembly.Location) ?? "";
            var cur = new DirectoryInfo(asmDir);
            for (int i = 0; i < 8 && cur != null; i++)
            {
                var cand = Path.Combine(cur.FullName, "src", "Atheriz.Core", "Atheriz.Core.csproj");
                if (File.Exists(cand)) { var rel = Path.GetRelativePath(folderPath, cand); coreRef = rel; if (!File.Exists(Path.Combine(folderPath, rel)) && Path.IsPathRooted(cand)) coreRef = cand; break; }
                cur = cur.Parent;
            }
        } catch { }
        var csproj = $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>\n  <ItemGroup><ProjectReference Include=\"{coreRef}\" /></ItemGroup>\n</Project>\n";
        Console.WriteLine($"  Creating {csprojName}...");
        File.WriteAllText(csprojPath, csproj);
        var files = new Dictionary<string,string>{["GameSettings.cs"]=GS(gameName),["CustomObject.cs"]=CO(gameName),["CustomNode.cs"]=CN(gameName),["CustomAccount.cs"]=CA(gameName),["CustomChannel.cs"]=CC(gameName),["CustomScript.cs"]=CS(gameName),["README.md"]=RM(gameName)};
        foreach (var kv in files)
        {
            Console.WriteLine($"  Creating {kv.Key}...");
            File.WriteAllText(Path.Combine(folderPath, kv.Key), kv.Value);
        }
        Console.WriteLine("  Copying web folder...");
    }
    private static string GS(string ns) => $"// Port of atheriz/new.py:292\n// Port of atheriz/settings.py\nnamespace {ns};\nusing Atheriz.Core.Settings;\n/// <summary>Game settings — mirrors settings.py. See AtherizSettings.</summary>\npublic static class GameSettings\n{{\n    public const string SavePath = \"save\";\n    public const string SecretPath = \"secret\";\n    public const string ServerName = \"{ns}\";\n    public const bool WebclientSyncCheck = true;\n}}\n";
    // Dynamic generation via reflection — mirrors new.py:ClassInspector.get_override_methods -> get_class_hooks (utils.py:701)
    // Patterns: at_ / access_ / format_ / pre_ / post_ (case-insensitive) + always setup_parser/run. In C# these are At* etc.
    private static IEnumerable<System.Reflection.MethodInfo> GetHookMethods(Type t)
    {
        // Only inspect methods declared on t itself (or its direct partials), mirroring new.py:ClassInspector per-class hook collection.
        // Using DeclaredOnly prevents inheriting GameObject hooks into Node/Channel/Script where Python test/* files have their own small sets.
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
        // Also include base class virtuals that are overridden in t via Flatten? No — we want only hooks defined on t (DeclaringType==t), falling back to Flatten if t has no declared hooks (e.g., Script).
        var all = t.GetMethods(flags);
        if (all.Length == 0) all = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var m in all)
        {
            if (m.IsSpecialName) continue;
            if (m.DeclaringType == typeof(object)) continue;
            if (m.DeclaringType != t && m.DeclaringType != typeof(Atheriz.Core.Objects.GameObject) && m.DeclaringType != typeof(Atheriz.Core.Objects.Node) && m.DeclaringType != typeof(Atheriz.Core.Objects.Account) && m.DeclaringType != typeof(Atheriz.Core.Objects.Channel) && m.DeclaringType != typeof(Atheriz.Core.Objects.Script)) { /* skip inherited from System */ }
            // Must be declared on t itself to keep per-file hook sets small like test/*.py (Object 35, Node 7, Channel 3, Account 4, Script 1)
            if (m.DeclaringType != t) continue;
            if (m.DeclaringType != null && m.DeclaringType.Namespace != null && m.DeclaringType.Namespace.StartsWith("System")) continue;
            if (!m.IsVirtual || m.IsFinal) continue;
            var n = m.Name;
            var ln = n.ToLowerInvariant();
            bool isHook = ln.StartsWith("at_") || ln.StartsWith("at") || ln.StartsWith("access") || ln.StartsWith("format") || ln == "setup_parser" || ln == "setupparser" || ln == "run";
            if (!isHook) continue;
            if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") || n.StartsWith("remove_")) continue;
            if (!(ln.StartsWith("at") || ln.StartsWith("access") || ln.StartsWith("format"))) continue;
            yield return m;
        }
    }
    private static string FriendlyType(Type t)
    {
        if (t == typeof(void)) return "void";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(long)) return "long";
        if (t == typeof(double)) return "double";
        if (t == typeof(float)) return "float";
        if (t == typeof(object)) return "object";
        if (t.IsGenericParameter) return t.Name;
        if (t.IsArray) return FriendlyType(t.GetElementType()!) + "[]";
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments().Select(FriendlyType);
            var name = def.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            if (def == typeof(Nullable<>)) return FriendlyType(t.GetGenericArguments()[0]) + "?";
            if (def == typeof(List<>)) return $"List<{string.Join(", ", args)}>";
            if (def == typeof(Dictionary<,>)) return $"Dictionary<{string.Join(", ", args)}>";
            if (def == typeof(IEnumerable<>)) return $"IEnumerable<{string.Join(", ", args)}>";
            if (name.StartsWith("ValueTuple")) return $"({string.Join(", ", args)})";
            return $"{name}<{string.Join(", ", args)}>";
        }
        if (t.IsNested)
        {
            // Nested type like GameTime.GameTimeInfo — preserve outer name
            return $"{FriendlyType(t.DeclaringType!)}.{t.Name}";
        }
        var n2 = t.Name;
        if (t.Namespace != null && t.Namespace.StartsWith("Atheriz")) return t.Name;
        // Common BCL that needs import
        if (t == typeof(System.Text.Json.JsonElement)) return "JsonElement";
        return n2;
    }
    private static string BuildParamList(System.Reflection.MethodInfo m)
    {
        var ps = m.GetParameters();
        var parts = new List<string>();
        // Use NullabilityInfoContext where available to preserve ? annotations (matches base virtual signatures)
        System.Reflection.NullabilityInfoContext? nic = null;
        try { nic = new System.Reflection.NullabilityInfoContext(); } catch { }
        foreach (var p in ps)
        {
            var t = FriendlyType(p.ParameterType);
            // If param is nullable reference (e.g., GameObject? ) but FriendlyType lost ?, restore via nullability context or default-null heuristic
            bool isNullable = false;
            if (nic != null) { try { var ni = nic.Create(p); isNullable = ni.WriteState == System.Reflection.NullabilityState.Nullable; } catch { } }
            if (!isNullable && p.HasDefaultValue && p.DefaultValue == null && !p.ParameterType.IsValueType) isNullable = true;
            // Also check NullableAttribute directly
            if (!isNullable && p.ParameterType.IsClass && t != "string" && t != "object")
            {
                // Heuristic: many base hooks use nullable GameObject? — if FriendlyType is GameObject without ?, and param allows null, add ?
                if (p.HasDefaultValue && p.DefaultValue == null) isNullable = true;
            }
            if (isNullable && !t.EndsWith("?") && p.ParameterType.IsClass) t += "?";
            // ValueTuple nullable not needed
            var name = p.Name ?? "arg";
            string decl = $"{t} {name}";
            if (p.HasDefaultValue)
            {
                var dv = p.DefaultValue;
                string ds;
                if (dv == null) ds = "null";
                else if (dv is string s) ds = $"\"{s}\"";
                else if (dv is bool b) ds = b ? "true" : "false";
                else ds = dv.ToString() ?? "null";
                decl += $" = {ds}";
            }
            parts.Add(decl);
        }
        return string.Join(", ", parts);
    }
    private static string BuildArgList(System.Reflection.MethodInfo m)
    {
        var ps = m.GetParameters();
        return string.Join(", ", ps.Select(p => p.Name ?? "arg"));
    }
    private static string GenerateHooksFor(Type t)
    {
        var methods = GetHookMethods(t).GroupBy(m => m.Name).Select(g => g.First()).OrderBy(m => m.Name).ToList();
        if (methods.Count == 0) return "    // No hooks discovered — base class has no virtual At*/Access*/Format* hooks\n";
        var sb = new System.Text.StringBuilder();
        foreach (var m in methods)
        {
            var ret = FriendlyType(m.ReturnType);
            var paramList = BuildParamList(m);
            var argList = BuildArgList(m);
            var isVoid = m.ReturnType == typeof(void);
            var sig = $"    public override {ret} {m.Name}({paramList})";
            sb.AppendLine(sig);
            sb.AppendLine("    {");
            // Preserve empty vs non-empty via base call — mirrors TemplateGenerator._format_body
            if (isVoid) sb.AppendLine($"        base.{m.Name}({argList});");
            else sb.AppendLine($"        return base.{m.Name}({argList});");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + "\n";
    }
    private static string CO(string ns)
    {
        var header = $"// Port of atheriz/new.py:522 TEMPLATE_CONFIGS (\"object\",\"Object\",\"atheriz.objects.base_obj\")\n// Dynamically generated via get_class_hooks (atheriz/utils.py:701) — mirrors test/object.py full hook list\n#nullable enable\nnamespace {ns};\nusing System.Text.Json;\nusing Atheriz.Core.Objects;\nusing Atheriz.Core;\nusing Atheriz.Core.Globals;\n/// <summary>Custom Object — mirrors test/object.py. Override methods below to customize behavior.</summary>\npublic class CustomObject : GameObject\n{{\n";
        var ctor = "    public CustomObject() : base() { }\n    public CustomObject(string name, bool isPc = false) : base() { Name = name; IsPc = isPc; }\n\n";
        var hooks = GenerateHooksFor(typeof(Atheriz.Core.Objects.GameObject));
        return header + ctor + hooks + "}\n";
    }
    private static string CN(string ns)
    {
        var header = $"// Port of atheriz/new.py:522 (\"node\",\"Node\",\"atheriz.objects.nodes\")\n// Dynamically generated via get_class_hooks\n#nullable enable\nnamespace {ns};\nusing Atheriz.Core;\nusing Atheriz.Core.Objects;\n/// <summary>Custom Node — mirrors test/node.py</summary>\npublic class CustomNode : Node\n{{\n";
        var ctor = "    public CustomNode() : base() { }\n    public CustomNode(Coord coord, string name = \"room\", string desc = \"\") : base(coord, name, desc) { }\n\n";
        var hooks = GenerateHooksFor(typeof(Atheriz.Core.Objects.Node));
        return header + ctor + hooks + "}\n";
    }
    private static string CA(string ns)
    {
        var header = $"// Port of atheriz/new.py:522 (\"account\",\"Account\",\"atheriz.objects.base_account\")\n#nullable enable\nnamespace {ns};\nusing Atheriz.Core.Objects;\n/// <summary>Custom Account — mirrors test/account.py</summary>\npublic class CustomAccount : Account\n{{\n";
        var ctor = "    public CustomAccount() : base() { }\n";
        var hooks = GenerateHooksFor(typeof(Atheriz.Core.Objects.Account));
        return header + ctor + hooks + "}\n";
    }
    private static string CC(string ns)
    {
        var header = $"// Port of atheriz/new.py:522 (\"channel\",\"Channel\",\"atheriz.objects.base_channel\")\n#nullable enable\nnamespace {ns};\nusing Atheriz.Core.Objects;\n/// <summary>Custom Channel — mirrors test/channel.py</summary>\npublic class CustomChannel : Channel\n{{\n";
        var ctor = "    public CustomChannel(int historyLimit = 50) : base(historyLimit) { }\n";
        var hooks = GenerateHooksFor(typeof(Atheriz.Core.Objects.Channel));
        return header + ctor + hooks + "}\n";
    }
    private static string CS(string ns)
    {
        var header = $"// Port of atheriz/new.py:522 (\"script\",\"Script\",\"atheriz.objects.base_script\")\n#nullable enable\nnamespace {ns};\nusing Atheriz.Core.Objects;\n/// <summary>Custom Script — mirrors test/script.py</summary>\npublic class CustomScript : Script\n{{\n";
        var ctor = "    public CustomScript() : base() { }\n";
        var hooks = GenerateHooksFor(typeof(Atheriz.Core.Objects.Script));
        return header + ctor + hooks + "}\n";
    }
    private static string RM(string ns) => $"# {ns} — Atheriz Game Folder\nGenerated via `atheriz-cs new {ns}` (ports `atheriz/new.py:784`).\n## Run\n```\ndotnet run --project {ns}.csproj -- --foreground\n# or dotnet run --project ../src/Atheriz.Server -- --foreground\n```\n";
    // Port of atheriz/new.py:530 copy_web_folder
    public static void CopyWebFolder(string destination, string? webSrc = null)
    {
        if (webSrc != null)
        {
            if (!Directory.Exists(webSrc))
                throw new DirectoryNotFoundException($"Web folder not found at {webSrc}");
            var destWeb2 = Path.Combine(destination, "web");
            CopyDirectory(webSrc, destWeb2);
            return;
        }
        string? src = TryResolveWebSrc();
        var destWeb = Path.Combine(destination, "web");
        if (src != null && Directory.Exists(src))
            CopyDirectory(src, destWeb);
        var wwwroot = TryResolveWwwRoot();
        if (wwwroot != null && Directory.Exists(wwwroot))
        {
            var destStatic = Path.Combine(destWeb, "static");
            CopyDirectory(wwwroot, destStatic);
        }
        if (src == null && wwwroot == null)
            throw new DirectoryNotFoundException("Web folder not found (checked web/ and wwwroot)");
    }
    private static string? TryResolveWebSrc()
    {
        var asmDir = Path.GetDirectoryName(typeof(GameTemplateGenerator).Assembly.Location) ?? AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new List<string>
        {
            Path.Combine(asmDir, "web"),
            Path.Combine(AppContext.BaseDirectory, "web"),
            Path.Combine(cwd, "src", "Atheriz.Server", "web"),
            Path.Combine(cwd, "web"),
            Path.Combine(asmDir, "..", "web"),
            Path.Combine(asmDir, "..", "..", "web"),
        };
        var cur = new DirectoryInfo(asmDir);
        for (int i = 0; i < 8 && cur != null; i++) { candidates.Add(Path.Combine(cur.FullName, "src", "Atheriz.Server", "web")); candidates.Add(Path.Combine(cur.FullName, "web")); cur = cur.Parent; }
        cur = new DirectoryInfo(cwd);
        for (int i = 0; i < 8 && cur != null; i++) { candidates.Add(Path.Combine(cur.FullName, "src", "Atheriz.Server", "web")); cur = cur.Parent; }
        var resolved = AssetPathResolver.ResolveCandidates(candidates.Select(Path.GetFullPath));
        return resolved;
    }
    private static string? TryResolveWwwRoot()
    {
        var asmDir = Path.GetDirectoryName(typeof(GameTemplateGenerator).Assembly.Location) ?? AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new List<string>
        {
            Path.Combine(asmDir, "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(cwd, "src", "Atheriz.Server", "wwwroot"),
            Path.Combine(asmDir, "..", "wwwroot"),
        };
        var cur = new DirectoryInfo(asmDir);
        for (int i = 0; i < 8 && cur != null; i++) { candidates.Add(Path.Combine(cur.FullName, "src", "Atheriz.Server", "wwwroot")); candidates.Add(Path.Combine(cur.FullName, "wwwroot")); cur = cur.Parent; }
        cur = new DirectoryInfo(cwd);
        for (int i = 0; i < 8 && cur != null; i++) { candidates.Add(Path.Combine(cur.FullName, "src", "Atheriz.Server", "wwwroot")); cur = cur.Parent; }
        var resolved = AssetPathResolver.ResolveCandidates(candidates.Select(Path.GetFullPath));
        return resolved;
    }
    public static void CopyWebFolder(string destination, string webSrcPath, bool _unused) => CopyWebFolder(destination, webSrcPath);
    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var dest = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }
}
