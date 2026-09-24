using System.Text.RegularExpressions;
namespace Atheriz.Server.Infrastructure;
/// <summary>Generates game folder — C# analogue of <c>atheriz new my_game</c>. Mirrors <c>new.py:create_game_folder</c>.</summary>
// Reflection note: GetHookMethods/BuildParamList/GenerateHooksFor use
// System.Reflection to EMIT game source text (compile-time-style codegen,
// like a source generator) — never to invoke or inspect live objects.
// This is legitimate codegen, not runtime reflection.
public static class GameTemplateGenerator
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {"False","None","True","and","as","assert","async","await","break","class","continue","def","del","elif","else","except","finally","for","from","global","if","import","in","is","lambda","nonlocal","not","or","pass","raise","return","try","while","with","yield","abstract","base","bool","byte","case","catch","char","checked","const","decimal","default","delegate","do","double","enum","event","explicit","extern","false","fixed","float","foreach","goto","implicit","int","interface","internal","lock","long","namespace","new","null","object","operator","out","override","params","private","protected","public","readonly","ref","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile"};
    private static bool IsValidId(string n) => !string.IsNullOrEmpty(n) && n != "." && !char.IsDigit(n[0]) && Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]*$") && !Keywords.Contains(n);
    public static bool CreateGameFolder(string targetPath, string gameName, bool overwrite = false) => CreateInternal(targetPath, gameName, overwrite);
    public static bool CreateGameFolder(string targetPath, bool overwrite = false) => CreateInternal(targetPath, null, overwrite);
    private static bool CreateInternal(string targetPath, string? gameName, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) { Console.WriteLine("Error: folder name cannot be empty."); return false; }
        var trimmed = targetPath.Trim();
        var raw = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(raw)) raw = trimmed;
        if (!IsValidId(raw))
        {
            Console.WriteLine($"Error: '{raw}' is not a valid C# identifier (hyphens/digits/spaces not allowed).");
            return false;
        }
        var gName = string.IsNullOrWhiteSpace(gameName) ? raw : gameName!.Trim();
        if (!IsValidId(gName)) { Console.WriteLine($"Error: '{gName}' is not a valid C# identifier (hyphens/digits/spaces not allowed)."); return false; }
        if (GameUtils.IsInGameFolder()) Console.WriteLine("Warning: already inside a game folder; creating nested game folder is not recommended.");
        var folderPath = Path.GetFullPath(targetPath);
        try { Atheriz.Core.Utils.PathGuards.DenyRoot(folderPath); } catch (Exception ex) { Console.WriteLine(ex.Message); return false; }
        bool folderExistsInitially = Directory.Exists(folderPath);
        if (folderExistsInitially && !overwrite)
        {
            Console.Write($"Folder '{targetPath}' already exists. Replace files? (y/n): ");
            var ans = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (ans != "y")
            {
                Console.WriteLine("Aborted.");
                return false;
            }
        }
        // Decide if we need to (re)create world — fresh folder OR overwrite forces fresh DB
        bool shouldSetup = !folderExistsInitially || overwrite;
        if (folderExistsInitially && !overwrite)
        {
            // interactive Replace rewrites every template — the saved
            // world is re-created with it (new code on the old DB desyncs
            // hooks/alarms). Credentials are prompted below like a fresh setup.
            shouldSetup = true;
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
                    return false;
                }
                Console.Write("Enter superuser username: ");
                username = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(username))
                {
                    Console.WriteLine("Error: Username cannot be empty.");
                    return false;
                }
            }
            password = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
            if (string.IsNullOrEmpty(password))
            {
                if (overwrite && folderExistsInitially && Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Error: ATHERIZ_SUPERUSER_PASSWORD must be set for non-interactive overwrite.");
                    return false;
                }
                Console.Write("Enter superuser password: ");
                if (Console.IsInputRedirected)
                {
                    // No ReadKey without a console: input would throw, so read
                    // the line with an echo warning instead of failing.
                    // Verbatim (no Trim): passwords are significant at login,
                    // so trimming here would store a different password (C3).
                    Console.Error.WriteLine("Warning: input is redirected; password will be echoed.");
                    password = Console.ReadLine();
                }
                else try { password = GameUtils.ReadSecretLine(); }
                catch { password = Console.ReadLine(); }
                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("Error: Password cannot be empty.");
                    return false;
                }
            }
        }
        // When overwriting an existing folder, wipe the save leaf so DoSetup
        // starts fresh (handles `new test --overwrite` bare-name case).
        // This runs AFTER credential validation (a failed prompt must leave the
        // save dir intact) and AFTER a liveness probe (wiping under a live
        // server must be refused, not raced).
        // Containment: the wipe is confined to <folder>/save/** (asserted
        // below — folder top-level files are never deleted). This is
        // deliberate operator intent (`--overwrite` names the folder), not a
        // world-membership decision, so it does NOT consult GuardWipePath
        // (which gates `reset` on initialized-world markers): a
        // stale save dir with no DB markers (aborted setup) still wipes, and
        // the pre-existing integration test pins stale.txt removal for
        // exactly that shape.
        if (overwrite && folderExistsInitially)
        {
            if (IsLiveServerFolder(folderPath))
            {
                Console.Error.WriteLine($"Error: a live server looks to own '{targetPath}' (verified server.pid); stop it before --overwrite.");
                return false;
            }
            try
            {
                var saveDirForWipe = Path.Combine(folderPath, "save");
                // Confinement asserts: exactly the save leaf of the designated
                // folder, never a root, never outside the folder.
                Atheriz.Core.Utils.PathGuards.DenyRoot(saveDirForWipe);
                if (!string.Equals(Path.GetFullPath(saveDirForWipe).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        Path.Combine(Path.GetFullPath(folderPath), "save"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Refusing to wipe outside the game save leaf: {saveDirForWipe}");
                if (Directory.Exists(saveDirForWipe))
                {
                    foreach (var f in Directory.GetFiles(saveDirForWipe, "*", SearchOption.AllDirectories))
                        try { File.Delete(f); } catch { }
                }
                // A stale secret/admin.token would survive as the "fresh"
                // game's credential: delete it so setup regenerates a fresh
                // token below (AdminToken.EnsureToken creates when missing).
                try
                {
                    var staleToken = Path.Combine(folderPath, "secret", "admin.token");
                    if (File.Exists(staleToken)) File.Delete(staleToken);
                }
                catch { }
                var dbFile = Path.Combine(folderPath, "save", "database.sqlite3");
                foreach (var f in new[] { dbFile, dbFile + "-wal", dbFile + "-shm", dbFile + ".journal" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
            } catch { }
        }
        Console.WriteLine($"Creating game folder: {targetPath}");
        Directory.CreateDirectory(folderPath);
        try { Scaffold(folderPath, gName); } catch (Exception ex) { Console.Error.WriteLine($"Error scaffolding game folder: {ex.Message}"); return false; }
// copy web (templates + static)
        try { CopyWebFolder(folderPath); } catch (Exception ex) { Console.Error.WriteLine($"Warning: could not copy web folder: {ex.Message}"); }
        var savePath = Path.Combine(folderPath, "save"); Directory.CreateDirectory(savePath);
        FsUtil.TryChmod0700(savePath);
        var secretPath = Path.Combine(folderPath, "secret"); Directory.CreateDirectory(secretPath);
        FsUtil.TryChmod0700(secretPath);
        try { var gi = Path.Combine(folderPath, ".gitignore"); if (!File.Exists(gi)) File.WriteAllText(gi, "save/\nsecret/\nbin/\nobj/\n"); } catch { }
        if (shouldSetup)
        {
            // Fail fast on invalid credentials: DoSetup would either throw
            // (half-built world) or skip the superuser silently, and the
            // Success banner below names the superuser — never print it
            // for credentials that cannot produce one (E1).
            var errU = Atheriz.Core.Commands.UnloggedIn.Validation.ValidateAccountName(username);
            if (errU is not null) { Console.WriteLine($"Error: invalid superuser username: {errU}"); return false; }
            var errP = Atheriz.Core.Commands.UnloggedIn.Validation.ValidatePassword(password!);
            if (errP is not null) { Console.WriteLine($"Error: invalid superuser password: {errP}"); return false; }
        }
        if (shouldSetup)
        {
            Console.WriteLine("\nSetting up initial world state...");
            try
            {
                var absSaveForSetup = Path.GetFullPath(savePath);
                var absSecretForSetup = Path.GetFullPath(secretPath);
                // Load the game (if any) so setup below dispatches to it;
                // best-effort and never fatal — a brand-new game has no dll
                // yet, so the template setup still runs.
                Atheriz.Core.IGameSetup? game = null;
                try { Atheriz.Core.Plugins.PluginReloader.LoadGameAssembliesAtBoot(new Atheriz.Core.Settings.AtherizSettings { SavePath = absSaveForSetup }, out game); }
                catch (Exception lex) { Console.Error.WriteLine($"[new] Game load failed ({lex.Message}); using template setup."); }
                Atheriz.Core.InitialSetup.RunSetup(new Atheriz.Core.SetupOptions(absSaveForSetup, username, password, absSecretForSetup), game);
            }
            catch (Exception ex)
            {
                // Setup failure is fatal: the world is half-built (or the
                // superuser missing), so report failure instead of falling
                // through to the Success banner and `return true` (E1).
                Console.Error.WriteLine($"Error: initial world setup failed: {ex.Message}");
                Console.WriteLine("  Run `create` to add superuser later or set ATHERIZ_SUPERUSER_USERNAME/PASSWORD and re-run.");
                return false;
            }
        }
        Console.WriteLine($"\nSuccess! Game folder '{targetPath}' created/updated with:");
        Console.WriteLine("  Template files:");
        Console.WriteLine("    - GameSettings.cs, CustomObject.cs, CustomNode.cs, CustomAccount.cs, CustomChannel.cs, CustomScript.cs, AssemblyInfo.cs");
        Console.WriteLine($"    - {gName}.csproj (refs Atheriz.Core)");
        Console.WriteLine("    - README.md, save/, secret/");
        Console.WriteLine("    - web/ (templates and static files)");
        Console.WriteLine("    - atheriz.sh, atheriz.cmd (per-game launchers: ./atheriz.sh start)");
        Console.WriteLine("    - build.sh, build.cmd (per-game build: ./build.sh [--no-web] [--web] [--reload])");
        if (shouldSetup)
        {
            Console.WriteLine("  Initial world:");
            Console.WriteLine($"    - Superuser account: {username}");
            Console.WriteLine($"    - Starting room at {Atheriz.Core.Settings.AtherizSettings.Global.DefaultHome}");
        }
        return true;
    }

    // Liveness probe: refuse --overwrite while a verified server owns the
    // folder. Only a pid file naming a live, verified server process refuses
    // — stale/dead/unparseable/foreign pids fall through to the wipe (aborted
    // setups must stay re-creatable). Uses the same per-PID verification as
    // stop (never name-prefix trust).
    private static bool IsLiveServerFolder(string folderPath)
    {
        try
        {
            var pidFile = Path.Combine(folderPath, "save", "server.pid");
            if (!File.Exists(pidFile)) return false;
            // Same pid-file read as the CLI liveness probes (create/reset/
            // restart/stop): TryParse failure is a dead claim, exactly like
            // the hand-rolled parse this replaced.
            var pid = PidFile.TryReadPid(pidFile);
            if (pid is null) return false;
            return PidFile.IsServerProcess(pid.Value);
        }
        catch { return false; }
    }
    private static void Scaffold(string folderPath, string gameName)
    {
        var csprojName = gameName + ".csproj"; var csprojPath = Path.Combine(folderPath, csprojName);
        string coreRef = "../src/Atheriz.Core/Atheriz.Core.csproj";
        bool coreFound = false;
        // Engine checkout root (the directory containing src/) for the
        // per-game launchers baked below: same upward search as coreRef so
        // the wrappers point at the engine that generated them.
        string engineRoot = "";
        try
        {
            var asmDir = Path.GetDirectoryName(typeof(GameTemplateGenerator).Assembly.Location) ?? "";
            var cur = new DirectoryInfo(asmDir);
            for (int i = 0; i < 8 && cur is not null; i++)
            {
                var cand = Path.Combine(cur.FullName, "src", "Atheriz.Core", "Atheriz.Core.csproj");
                if (File.Exists(cand)) { var rel = Path.GetRelativePath(folderPath, cand); coreRef = rel; if (!File.Exists(Path.Combine(folderPath, rel)) && Path.IsPathRooted(cand)) coreRef = cand; coreFound = true; engineRoot = cur.FullName; break; }
                cur = cur.Parent;
            }
        } catch { }
        // When the upward search fails (published/relocated engine) the
        // relative default dangles, so fall back to the NuGet package and
        // the scaffold always references a resolvable Atheriz.Core.
        string refXml;
        if (coreFound) refXml = $"<ProjectReference Include=\"{coreRef}\" />";
        else
        {
            var coreVersion = typeof(Atheriz.Core.Globals.ObjectRegistry).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            refXml = $"<PackageReference Include=\"Atheriz.Core\" Version=\"{coreVersion}\" />";
        }
        var csproj = $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>\n  <ItemGroup>{refXml}</ItemGroup>\n</Project>\n";
        Console.WriteLine($"  Creating {csprojName}...");
        File.WriteAllText(csprojPath, csproj);
        // Custom files ride the kind table (same order as before: object,
        // node, account, channel, script) so scaffold output order is stable.
        var files = new Dictionary<string, string> { ["GameSettings.cs"] = GS(gameName) };
        foreach (var kind in CustomKinds) files[kind.File] = kind.Emit(gameName);
        files["AssemblyInfo.cs"] = AI(gameName);
        files["README.md"] = RM(gameName);
        // Baked relative engine path for the per-game build scripts: valid
        // wherever the game is created and moves with engine+games together
        // (unlike the launchers' absolute fallback). Empty when the engine
        // root is unknown — those builds rely on ATHERIZ_ROOT/upward search.
        string engineRel = "";
        try { if (!string.IsNullOrEmpty(engineRoot)) engineRel = Path.GetRelativePath(folderPath, engineRoot); } catch { }
        // Per-game launchers: forward every command to the engine launcher
        // with the game folder as CWD. Plain writes (like every template
        // above) so `new --overwrite` refreshes customized wrappers back to
        // generated ones.
        files["atheriz.sh"] = ShWrapper(engineRoot);
        files["atheriz.cmd"] = CmdWrapper(engineRoot);
        // Per-game builds: Release plugin build + webclient redeploy into
        // this game. Same plain-write refresh semantics as the launchers.
        files["build.sh"] = BuildSh(gameName, engineRel);
        files["build.cmd"] = BuildCmd(gameName, engineRel);
        foreach (var kv in files)
        {
            Console.WriteLine($"  Creating {kv.Key}...");
            File.WriteAllText(Path.Combine(folderPath, kv.Key), kv.Value);
        }
        // Best-effort executable bit for the shell wrappers (no-op on Windows).
        Atheriz.Core.Utils.FsUtil.TryChmod0755(Path.Combine(folderPath, "atheriz.sh"));
        Atheriz.Core.Utils.FsUtil.TryChmod0755(Path.Combine(folderPath, "build.sh"));
        Console.WriteLine("  Copying web folder...");
    }
    private static string GS(string ns) => $" // mirrors settings.py. See AtherizSettings.</summary>\npublic static class GameSettings\n{{\n    public const string SavePath = \"save\";\n    public const string SecretPath = \"secret\";\n    public const string ServerName = \"{ns}\";\n    public const bool WebclientSyncCheck = true;\n}}\n";
    // Dynamic generation via reflection — mirrors new.py:ClassInspector.get_override_methods -> get_class_hooks (utils.py:1098).
    // Python OVERRIDE_PATTERNS = ("at_", "access_", "format_", "pre_", "post_") + ALWAYS (setup_parser, run).
    // C# ports are PascalCase (AtPreMove ↔ at_pre_move), so the boundary rule is: the char after the stem
    // must be uppercase, '_' or end-of-name. A bare StartsWith("at") would also match a hypothetical
    // "Attach" (F012); the boundary check closes that hole with identical output for all real hooks.
    private static bool IsHookStem(string name, string stem)
    {
        if (!name.StartsWith(stem, StringComparison.Ordinal)) return false;
        if (name.Length == stem.Length) return true;
        char next = name[stem.Length];
        return char.IsUpper(next) || next == '_';
    }
    private static IEnumerable<System.Reflection.MethodInfo> GetHookMethods(Type t)
    {
        // Only inspect methods declared on t itself (or its direct partials), mirroring new.py:ClassInspector per-class hook collection.
        // Using DeclaredOnly prevents inheriting GameObject hooks into Node/Channel/Script where Python test/* files have their own small sets.
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
        // Only hooks declared on t itself (DeclaringType==t); no base-class fallback.
        var all = t.GetMethods(flags);
        foreach (var m in all)
        {
            if (m.IsSpecialName) continue;
            if (m.DeclaringType == typeof(object)) continue;
            // Must be declared on t itself to keep per-file hook sets small like test/*.py (Object 35, Node 7, Channel 3, Account 4, Script 1)
            if (m.DeclaringType != t) continue;
            if (!m.IsVirtual || m.IsFinal) continue;
            var n = m.Name;
            var ln = n.ToLowerInvariant();
            bool isHook = IsHookStem(n, "At") || IsHookStem(n, "Access") || IsHookStem(n, "Format") || IsHookStem(n, "Pre") || IsHookStem(n, "Post") || ln == "setup_parser" || ln == "setupparser" || ln == "run";
            if (!isHook) continue;
            if (n.StartsWith("get_", StringComparison.Ordinal) || n.StartsWith("set_", StringComparison.Ordinal) || n.StartsWith("add_", StringComparison.Ordinal) || n.StartsWith("remove_", StringComparison.Ordinal)) continue;
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
            if (name.StartsWith("ValueTuple", StringComparison.Ordinal)) return $"({string.Join(", ", args)})";
            return $"{name}<{string.Join(", ", args)}>";
        }
        if (t.IsNested)
        {
            // Nested type like GameTime.GameTimeInfo — preserve outer name
            return $"{FriendlyType(t.DeclaringType!)}.{t.Name}";
        }
        var n2 = t.Name;
        if (t.Namespace is not null && t.Namespace.StartsWith("Atheriz", StringComparison.Ordinal)) return t.Name;
        // Common BCL that needs import
        if (t == typeof(System.Text.Json.JsonElement)) return "JsonElement";
        return n2;
    }
    // Escape a default for emitted C#: a quote, backslash or newline would
    // otherwise emit an uncompilable Custom*.cs override signature.
    private static string EscapeCsString(string s) => s
        .Replace("\\", "\\\\").Replace("\0", "\\0").Replace("\r", "\\r")
        .Replace("\n", "\\n").Replace("\t", "\\t").Replace("\"", "\\\"");
    // Single ref/out/in/params rule shared by the declaration builder
    // (BuildParamList) and the call-site builder (BuildArgList) below: a
    // dropped modifier emits an override that does not match the base
    // signature (compile break in the generated game) or silently changes
    // semantics.
    private static string ModifierPrefix(System.Reflection.ParameterInfo p)
    {
        if (!p.ParameterType.IsByRef) return "";
        return p.IsOut ? "out " : (p.IsIn ? "in " : "ref ");
    }
    private static string BuildParamList(System.Reflection.MethodInfo m)
    {
        var ps = m.GetParameters();
        List<string> parts = [];
        // Use NullabilityInfoContext where available to preserve ? annotations (matches base virtual signatures)
        System.Reflection.NullabilityInfoContext? nic = null;
        try { nic = new System.Reflection.NullabilityInfoContext(); } catch { }
        foreach (var p in ps)
        {
            // preserve ref/out/in/params — a dropped modifier emits
            // an override that does not match the base signature (compile
            // break in the generated game) or silently changes semantics.
            var pt = p.ParameterType;
            string modifier = "";
            if (pt.IsByRef)
            {
                pt = pt.GetElementType() ?? pt;
                modifier = ModifierPrefix(p);
            }
            bool isParams = p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0;
            var t = FriendlyType(pt);
            // If param is nullable reference (e.g., GameObject? ) but FriendlyType lost ?, restore via nullability context or default-null heuristic
            bool isNullable = false;
            if (nic is not null) { try { var ni = nic.Create(p); isNullable = ni.WriteState == System.Reflection.NullabilityState.Nullable; } catch { } }
            if (!isNullable && p.HasDefaultValue && p.DefaultValue is null && !pt.IsValueType) isNullable = true;
            // Also check NullableAttribute directly
            if (!isNullable && pt.IsClass && t != "string" && t != "object")
            {
                // Heuristic: many base hooks use nullable GameObject? — if FriendlyType is GameObject without ?, and param allows null, add ?
                if (p.HasDefaultValue && p.DefaultValue is null) isNullable = true;
            }
            if (isNullable && !t.EndsWith("?", StringComparison.Ordinal) && pt.IsClass) t += "?";
            // ValueTuple nullable not needed
            var name = p.Name ?? "arg";
            string decl = $"{(isParams ? "params " : "")}{modifier}{t} {name}";
            if (p.HasDefaultValue && !p.IsOut)
            {
                var dv = p.DefaultValue;
                string ds;
                if (dv is null) ds = "null";
                else if (dv is string s) ds = "\"" + EscapeCsString(s) + "\"";
                else if (dv is bool b) ds = b ? "true" : "false";
                else if (dv is char c) ds = "'" + EscapeCsString(c.ToString()).Replace("'", "\\'") + "'";
                // InvariantCulture: a locale decimal comma would emit
                // uncompilable code (e.g. `= 1,5` parses as two args).
                else if (dv is IFormattable fmt) ds = fmt.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
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
        // Call-site mirrors of the modifiers (ref/out/in/params pass-through).
        return string.Join(", ", ps.Select(p => ModifierPrefix(p) + (p.Name ?? "arg")));
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
    // Shared custom-file shape for the five emitters below: every file is
    // header + constructor + generated hooks + closing brace. One method so
    // a shape change cannot land in four emitters and miss the fifth.
    private static string GenCustom(string ns, Type type, string header, string ctor)
        => header + ctor + GenerateHooksFor(type) + "}\n";
    // Kind table driving the five custom-file emitters: (file, emitter).
    // Scaffold walks this so a new custom kind cannot be added to the
    // emitters but missed from the scaffold (or vice versa).
    private static readonly (string File, Func<string, string> Emit)[] CustomKinds =
    [
        ("CustomObject.cs", CO),
        ("CustomNode.cs", CN),
        ("CustomAccount.cs", CA),
        ("CustomChannel.cs", CC),
        ("CustomScript.cs", CS),
    ];
    private static string CO(string ns)
    {
        var header = $" // mirrors test/object.py full hook list\n#nullable enable\nnamespace {ns};\nusing System.Text.Json;\nusing Atheriz.Core.Objects;\nusing Atheriz.Core;\nusing Atheriz.Core.Globals;\n/// <summary>Custom Object — mirrors test/object.py. Override methods below to customize behavior.</summary>\npublic class CustomObject : GameObject\n{{\n";
        var ctor = "    public CustomObject() : base() { }\n    public CustomObject(string name, bool isPc = false) : base() { Name = name; IsPc = isPc; }\n\n";
        return GenCustom(ns, typeof(Atheriz.Core.Objects.GameObject), header, ctor);
    }
    private static string CN(string ns)
    {
        var header = $" // mirrors test/node.py</summary>\npublic class CustomNode : Node\n{{\n";
        var ctor = "    public CustomNode() : base() { }\n    public CustomNode(Coord coord, string name = \"room\", string desc = \"\") : base(coord, name, desc) { }\n\n";
        return GenCustom(ns, typeof(Atheriz.Core.Objects.Node), header, ctor);
    }
    private static string CA(string ns)
    {
        var header = $" // mirrors test/account.py</summary>\npublic class CustomAccount : Account\n{{\n";
        var ctor = "    public CustomAccount() : base() { }\n";
        return GenCustom(ns, typeof(Atheriz.Core.Objects.Account), header, ctor);
    }
    private static string CC(string ns)
    {
        var header = $" // mirrors test/channel.py</summary>\npublic class CustomChannel : Channel\n{{\n";
        var ctor = "    public CustomChannel(int historyLimit = 50) : base(historyLimit) { }\n";
        return GenCustom(ns, typeof(Atheriz.Core.Objects.Channel), header, ctor);
    }
    private static string CS(string ns)
    {
        var header = $" // mirrors test/script.py</summary>\npublic class CustomScript : Script\n{{\n";
        var ctor = "    public CustomScript() : base() { }\n";
        return GenCustom(ns, typeof(Atheriz.Core.Objects.Script), header, ctor);
    }
    private static string RM(string ns) => $"# {ns} — Atheriz Game Folder\nGenerated via `atheriz-cs new {ns}` (ports `atheriz/new.py:784`).\n## Run\n```\n# From this folder (the wrappers forward to the engine launcher):\n./atheriz.sh start\n# (atheriz.cmd start on Windows; set ATHERIZ_ROOT if the engine moved)\n# Rebuild this game (plugin Release + webclient redeploy):\n./build.sh\n# (build.cmd on Windows; --no-web for code only, --web for web only,\n# --reload to hot-load a running server)\n# Game code is a class library loaded by the server (no Program.cs needed).\n# Direct alternative:\ndotnet run --project ../src/Atheriz.Server -- start\n```\n";
    // Assembly attributes for scaffolded games (checked-in template carries AssemblyInfo.cs with the same shape).
    private static string AI(string ns) => $"using System.Reflection;\n[assembly: AssemblyDescription(\"{ns} — Atheriz game plugin, loaded by Atheriz.Server via PluginLoader.\")]\n";
    // Per-game launchers: thin forwarders to the engine's atheriz.sh/cmd.
    // Engine resolution is ATHERIZ_ROOT env override first, then an upward
    // search for the engine checkout (covers moved/copied game folders),
    // then the absolute path baked in at `new` time. Arguments pass through
    // untouched and CWD (the game folder) is never changed, so every engine
    // command works: start|stop|restart|reload|reset|create|new|test.
    private static string ShWrapper(string engineRoot) => """
        #!/usr/bin/env bash
        # Auto-generated by `atheriz.sh new` — per-game launcher. Forwards every
        # command to the engine launcher; the game folder (current directory) is
        # never changed. Override the engine location with ATHERIZ_ROOT.
        set -euo pipefail
        ENGINE_ROOT="${ATHERIZ_ROOT:-}"
        if [ -z "$ENGINE_ROOT" ]; then
          dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
          depth=0
          while [ "$depth" -lt 12 ]; do
            if [ -f "$dir/src/Atheriz.Server/Atheriz.Server.csproj" ]; then ENGINE_ROOT="$dir"; break; fi
            parent="$(dirname "$dir")"
            if [ "$parent" = "$dir" ]; then break; fi
            dir="$parent"
            depth=$((depth + 1))
          done
        fi
        if [ -z "$ENGINE_ROOT" ]; then ENGINE_ROOT="__ATHERIZ_ENGINE_ROOT__"; fi
        if [ ! -f "$ENGINE_ROOT/atheriz.sh" ]; then
          echo "error: engine launcher not found under '$ENGINE_ROOT' (set ATHERIZ_ROOT to the engine checkout)" >&2
          exit 1
        fi
        exec "$ENGINE_ROOT/atheriz.sh" "$@"
        """.Replace("__ATHERIZ_ENGINE_ROOT__", (engineRoot ?? "").Replace('\\', '/')) + "\n";
    private static string CmdWrapper(string engineRoot) => """
        @echo off
        REM Auto-generated by `atheriz.cmd new` — per-game launcher. Forwards every
        REM command to the engine launcher; the game folder (current directory) is
        REM never changed. Override the engine location with ATHERIZ_ROOT.
        set "ENGINE_ROOT=__ATHERIZ_ENGINE_ROOT__"
        if defined ATHERIZ_ROOT set "ENGINE_ROOT=%ATHERIZ_ROOT%"
        if not exist "%ENGINE_ROOT%\atheriz.cmd" (
          echo error: engine launcher not found under '%ENGINE_ROOT%' (set ATHERIZ_ROOT to the engine checkout) 1>&2
          exit /b 1
        )
        call "%ENGINE_ROOT%\atheriz.cmd" %*
        exit /b %errorlevel%
        """.Replace("__ATHERIZ_ENGINE_ROOT__", engineRoot ?? "") + "\n";
    // Per-game builds: `dotnet build -c Release` the game plugin (reload
    // discovers the Release dll, falling back to Debug) plus a webclient
    // redeploy into this game via the engine's deploy.py (which rebuilds
    // the bundle itself). Default runs both steps; --no-web is plugin-only,
    // --web is web-only, --reload forwards to the sibling atheriz wrapper
    // so a running server hot-loads the fresh dll (loud failure when no
    // server runs — a build must not mask that). Engine resolution mirrors
    // the launchers, except the baked fallback is the relative path above.
    private static string BuildSh(string gameName, string engineRel) => """
        #!/usr/bin/env bash
        # Auto-generated by `atheriz.sh new` — per-game build. Rebuilds the game
        # plugin (Release) and redeploys the webclient into this game's web/ folder.
        # Usage: ./build.sh [--no-web] [--web] [--reload]
        #   (no flags)  build the plugin AND redeploy the webclient
        #   --no-web    plugin only (skip web redeploy)
        #   --web       web redeploy only (skip plugin build)
        #   --reload    after a successful build, reload the running server so it
        #               hot-loads the fresh dll (fails loudly if no server runs)
        # Engine resolution: ATHERIZ_ROOT wins, then an upward search for the
        # engine checkout, then the relative path baked in at `new` time.
        set -euo pipefail
        GAME_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
        ENGINE_ROOT="${ATHERIZ_ROOT:-}"
        if [ -z "$ENGINE_ROOT" ]; then
          dir="$GAME_DIR"
          depth=0
          while [ "$depth" -lt 12 ]; do
            if [ -f "$dir/src/Atheriz.Server/Atheriz.Server.csproj" ]; then ENGINE_ROOT="$dir"; break; fi
            parent="$(dirname "$dir")"
            if [ "$parent" = "$dir" ]; then break; fi
            dir="$parent"
            depth=$((depth + 1))
          done
        fi
        if [ -z "$ENGINE_ROOT" ]; then ENGINE_ROOT="$GAME_DIR/__ATHERIZ_ENGINE_REL__"; fi
        usage() {
          echo "Usage: ./build.sh [--no-web] [--web] [--reload]"
          echo "  (no flags)  build the game plugin (Release) and redeploy the webclient"
          echo "  --no-web    plugin only"
          echo "  --web       web redeploy only"
          echo "  --reload    reload the running server after a successful build"
        }
        DO_BUILD=1
        DO_WEB=1
        DO_RELOAD=0
        for arg in "$@"; do
          case "$arg" in
            --no-web) DO_WEB=0 ;;
            --web) DO_BUILD=0 ;;
            --reload) DO_RELOAD=1 ;;
            --help|-h) usage; exit 0 ;;
            *) echo "error: unknown arg: $arg" >&2; usage >&2; exit 1 ;;
          esac
        done
        if [ "$DO_BUILD" -eq 0 ] && [ "$DO_WEB" -eq 0 ]; then
          echo "error: --no-web and --web together leave nothing to do" >&2
          exit 1
        fi
        CSPROJ="$GAME_DIR/__GAME_NAME__.csproj"
        if [ "$DO_BUILD" -eq 1 ]; then
          if ! command -v dotnet >/dev/null 2>&1; then
            echo "error: dotnet SDK required (see engine global.json)" >&2
            exit 1
          fi
          if [ ! -f "$CSPROJ" ]; then
            echo "error: game project not found: $CSPROJ" >&2
            exit 1
          fi
          dotnet build "$CSPROJ" -c Release
        fi
        if [ "$DO_WEB" -eq 1 ]; then
          PY=python
          if ! command -v python >/dev/null 2>&1; then
            if command -v python3 >/dev/null 2>&1; then PY=python3; else
              echo "error: python required for the webclient deploy" >&2
              exit 1
            fi
          fi
          if [ ! -f "$ENGINE_ROOT/webclient/deploy.py" ]; then
            echo "error: deploy.py not found under '$ENGINE_ROOT' (set ATHERIZ_ROOT to the engine checkout)" >&2
            exit 1
          fi
          "$PY" "$ENGINE_ROOT/webclient/deploy.py" game --web-root "$GAME_DIR/web"
        fi
        if [ "$DO_RELOAD" -eq 1 ]; then
          if [ ! -f "$GAME_DIR/atheriz.sh" ]; then
            echo "error: per-game launcher missing: $GAME_DIR/atheriz.sh" >&2
            exit 1
          fi
          "$GAME_DIR/atheriz.sh" reload
        fi
        echo "Build complete."
        """.Replace("__GAME_NAME__", gameName).Replace("__ATHERIZ_ENGINE_REL__", (engineRel ?? "").Replace('\\', '/')) + "\n";
    private static string BuildCmd(string gameName, string engineRel) => """
        @echo off
        REM Auto-generated by `atheriz.cmd new` — per-game build. Rebuilds the game
        REM plugin (Release) and redeploys the webclient into this game's web/ folder.
        REM Usage: build.cmd [--no-web] [--web] [--reload]
        REM   (no flags)  build the plugin AND redeploy the webclient
        REM   --no-web    plugin only (skip web redeploy)
        REM   --web       web redeploy only (skip plugin build)
        REM   --reload    after a successful build, reload the running server so it
        REM               hot-loads the fresh dll (fails loudly if no server runs)
        REM Engine resolution: ATHERIZ_ROOT wins, else the relative path baked in
        REM at `new` time, resolved against this script's directory.
        setlocal EnableDelayedExpansion
        set "GAME_DIR=%~dp0"
        if "%GAME_DIR:~-1%"=="\" set "GAME_DIR=%GAME_DIR:~0,-1%"
        set "ENGINE_ROOT="
        if defined ATHERIZ_ROOT set "ENGINE_ROOT=%ATHERIZ_ROOT%"
        if not defined ENGINE_ROOT (
          pushd "%GAME_DIR%\__ATHERIZ_ENGINE_REL__" 2>nul
          if not errorlevel 1 (
            set "ENGINE_ROOT=!CD!"
            popd
          )
        )
        if not defined ENGINE_ROOT (
          echo error: engine checkout not found (set ATHERIZ_ROOT to the engine checkout) 1>&2
          exit /b 1
        )
        set "DO_BUILD=1"
        set "DO_WEB=1"
        set "DO_RELOAD=0"
        :parse
        if "%~1"=="" goto :parsed
        if /i "%~1"=="--no-web" ( set "DO_WEB=0" ) else if /i "%~1"=="--web" ( set "DO_BUILD=0" ) else if /i "%~1"=="--reload" ( set "DO_RELOAD=1" ) else if /i "%~1"=="--help" ( call :usage & exit /b 0 ) else ( echo error: unknown arg: %~1 1>&2 & exit /b 1 )
        shift
        goto :parse
        :parsed
        if "%DO_BUILD%"=="0" if "%DO_WEB%"=="0" (
          echo error: --no-web and --web together leave nothing to do 1>&2
          exit /b 1
        )
        if "%DO_BUILD%"=="1" (
          where dotnet >nul 2>nul
          if errorlevel 1 ( echo error: dotnet SDK required 1>&2 & exit /b 1 )
          if not exist "%GAME_DIR%\__GAME_NAME__.csproj" ( echo error: game project not found: %GAME_DIR%\__GAME_NAME__.csproj 1>&2 & exit /b 1 )
          dotnet build "%GAME_DIR%\__GAME_NAME__.csproj" -c Release
          if errorlevel 1 exit /b 1
        )
        if "%DO_WEB%"=="1" (
          set "PY=python"
          where python >nul 2>nul
          if errorlevel 1 set "PY=python3"
          if not exist "%ENGINE_ROOT%\webclient\deploy.py" ( echo error: deploy.py not found under '%ENGINE_ROOT%' (set ATHERIZ_ROOT to the engine checkout) 1>&2 & exit /b 1 )
          "!PY!" "%ENGINE_ROOT%\webclient\deploy.py" game --web-root "%GAME_DIR%\web"
          if errorlevel 1 exit /b 1
        )
        if "%DO_RELOAD%"=="1" (
          if not exist "%GAME_DIR%\atheriz.cmd" ( echo error: per-game launcher missing: %GAME_DIR%\atheriz.cmd 1>&2 & exit /b 1 )
          call "%GAME_DIR%\atheriz.cmd" reload
          if errorlevel 1 exit /b 1
        )
        echo Build complete.
        exit /b 0
        :usage
        echo Usage: build.cmd [--no-web] [--web] [--reload]
        echo   (no flags)  build the game plugin (Release) and redeploy the webclient
        echo   --no-web    plugin only
        echo   --web       web redeploy only
        echo   --reload    reload the running server after a successful build
        exit /b 0
        """.Replace("__GAME_NAME__", gameName).Replace("__ATHERIZ_ENGINE_REL__", engineRel ?? "") + "\n";
    public static void CopyWebFolder(string destination, string? webSrc = null)
    {
        if (webSrc is not null)
        {
            if (!Directory.Exists(webSrc))
                throw new DirectoryNotFoundException($"Web folder not found at {webSrc}");
            var destWeb2 = Path.Combine(destination, "web");
            CopyDirectory(webSrc, destWeb2);
            return;
        }
        string? src = TryResolveWebSrc();
        var destWeb = Path.Combine(destination, "web");
        if (src is not null && Directory.Exists(src))
            CopyDirectory(src, destWeb);
        var wwwroot = TryResolveWwwRoot();
        if (wwwroot is not null && Directory.Exists(wwwroot))
        {
            var destStatic = Path.Combine(destWeb, "static");
            CopyDirectory(wwwroot, destStatic);
        }
        if (src is null && wwwroot is null)
            throw new DirectoryNotFoundException("Web folder not found (checked web/ and wwwroot)");
    }
    private static List<string> BuildAssetCandidates(string leaf, bool includeCwdLeaf, bool includeGrandparentLeaf)
    {
        var asmDir = Path.GetDirectoryName(typeof(GameTemplateGenerator).Assembly.Location) ?? AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        List<string> candidates =
        [
            Path.Combine(asmDir, leaf),
            Path.Combine(AppContext.BaseDirectory, leaf),
            Path.Combine(cwd, "src", "Atheriz.Server", leaf),
        ];
        if (includeCwdLeaf) candidates.Add(Path.Combine(cwd, leaf));
        candidates.Add(Path.Combine(asmDir, "..", leaf));
        if (includeGrandparentLeaf) candidates.Add(Path.Combine(asmDir, "..", "..", leaf));
        var cur = new DirectoryInfo(asmDir);
        for (int i = 0; i < 8 && cur is not null; i++) { candidates.Add(Path.Combine(cur.FullName, "src", "Atheriz.Server", leaf)); candidates.Add(Path.Combine(cur.FullName, leaf)); cur = cur.Parent; }
        cur = new DirectoryInfo(cwd);
        for (int i = 0; i < 8 && cur is not null; i++) { candidates.Add(Path.Combine(cur.FullName, "src", "Atheriz.Server", leaf)); cur = cur.Parent; }
        return candidates;
    }
    private static string? TryResolveWebSrc()
        => AssetPathResolver.ResolveCandidates(BuildAssetCandidates("web", includeCwdLeaf: true, includeGrandparentLeaf: true).Select(Path.GetFullPath));
    private static string? TryResolveWwwRoot()
        => AssetPathResolver.ResolveCandidates(BuildAssetCandidates("wwwroot", includeCwdLeaf: false, includeGrandparentLeaf: false).Select(Path.GetFullPath));
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
