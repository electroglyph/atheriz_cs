// Port of atheriz/tests/test_webclient_sync.py:1
using Atheriz.Core.Settings;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedWebclientSyncTests
{
    private static void MakeTree(string root, Dictionary<string,string> files)
    {
        foreach (var kv in files)
        {
            var p = Path.Combine(root, kv.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, kv.Value);
        }
    }

    private static readonly Dictionary<string,string> EngineTemplates = new()
    {
        ["webclient/index.html"] = "<html>server</html>",
        ["webclient/fonts/font.css"] = "css",
    };
    private static readonly Dictionary<string,string> EngineStatic = new()
    {
        ["webclient/js/webclient.js"] = "js()",
        ["webclient/fonts/font.ttf"] = "binary",
        ["webclient/audio/tone.mp3"] = "mp3",
    };

    private static string MakeEngine(string tmp)
    {
        var engine = Path.Combine(tmp, "engine", "web");
        MakeTree(Path.Combine(engine, "templates"), EngineTemplates);
        MakeTree(Path.Combine(engine, "static"), EngineStatic);
        return engine;
    }

    private static string MakeGame(string tmp, Dictionary<string, Dictionary<string,string>>? files = null)
    {
        var game = Path.Combine(tmp, "game");
        if (files != null)
        {
            foreach (var area in files)
                MakeTree(Path.Combine(game, "web", area.Key), area.Value);
        }
        return game;
    }

    [Fact]
    public void IdenticalTrees_ReturnsNull()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new(EngineStatic),
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.Null(summary);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void GameWithoutWebDir_ReturnsNull()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = Path.Combine(tmp, "plain");
            Directory.CreateDirectory(game);
            Assert.Null(Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void RespectsSyncCheckSetting()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new() { ["templates"] = new(EngineTemplates), ["static"] = new(EngineStatic) });
            var prev = Atheriz.Core.Settings.AtherizSettings.Global.WebclientSyncCheck;
            try
            {
                Atheriz.Core.Settings.AtherizSettings.Global.WebclientSyncCheck = false;
                Assert.Null(Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine));
            }
            finally { Atheriz.Core.Settings.AtherizSettings.Global.WebclientSyncCheck = prev; }
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void MissingDifferentExtra_Classified()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new()
                {
                    ["webclient/index.html"] = "<html>game</html>",
                    ["webclient/custom.html"] = "custom",
                },
                ["static"] = new(),
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            var t = summary!["templates"];
            Assert.Equal(new[]{"index.html"}, t["different"].OrderBy(x=>x).ToArray());
            Assert.Equal(new[]{"fonts/font.css"}, t["missing"].OrderBy(x=>x).ToArray());
            Assert.Equal(new[]{"custom.html"}, t["extra"].OrderBy(x=>x).ToArray());
            var s = summary["static"];
            Assert.Equal(new[]{"js/webclient.js","fonts/font.ttf","audio/tone.mp3"}.OrderBy(x=>x).ToArray(),
                s["missing"].OrderBy(x=>x).ToArray());
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void EmptyGameWebDirFlagsEverythingMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp);
            var gameWebclient = Path.Combine(game, "web", "static", "webclient");
            Directory.CreateDirectory(gameWebclient);
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            Assert.Equal(2, summary!["templates"]["missing"].Count);
            Assert.Empty(summary["templates"]["different"]);
            Assert.Empty(summary["templates"]["extra"]);
            Assert.Equal(3, summary["static"]["missing"].Count);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void PosixCopyCommands()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new() { ["templates"] = new(EngineTemplates), ["static"] = new(EngineStatic) });
            var summary = new Dictionary<string, Dictionary<string, List<string>>>
            {
                ["templates"] = new() { ["missing"] = new(), ["different"] = new(), ["extra"] = new() },
                ["static"] = new() { ["missing"] = new(), ["different"] = new(), ["extra"] = new() },
            };
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary, game, "posix", engine);
            Assert.Contains("cp -r", msg);
            Assert.Contains("cp -r \"../engine/web/templates/webclient\" \"web/templates/\"", msg);
            Assert.Contains("cp -r \"../engine/web/static/webclient\" \"web/static/\"", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void WindowsXcopyCommands()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new() { ["templates"] = new(EngineTemplates), ["static"] = new(EngineStatic) });
            var summary = new Dictionary<string, Dictionary<string, List<string>>>
            {
                ["templates"] = new() { ["missing"] = new(), ["different"] = new(), ["extra"] = new() },
                ["static"] = new() { ["missing"] = new(), ["different"] = new(), ["extra"] = new() },
            };
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary, game, "nt", engine);
            Assert.Contains("xcopy", msg);
            Assert.Contains("\"web\\templates\\webclient\\\" /E /Y /I", msg);
            Assert.Contains("\"web\\static\\webclient\\\" /E /Y /I", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void SummaryLineAndExamples()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new()
                {
                    ["webclient/index.html"] = "<html>changed</html>",
                    ["webclient/new.html"] = "new",
                },
                ["static"] = new(),
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary!, game, "posix", engine);
            Assert.Contains("1 modified", msg);
            Assert.Contains("1 missing", msg);
            Assert.Contains("1 extra", msg);
            Assert.Contains("index.html", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void IdenticalSummaryPrintsOnlyHeaderAndCommands()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var game = MakeGame(tmp, new() { ["templates"] = new(EngineTemplates), ["static"] = new(EngineStatic) });
            var summary = new Dictionary<string, Dictionary<string, List<string>>>
            {
                ["templates"] = new() { ["missing"] = new(), ["different"] = new(), ["extra"] = new() },
                ["static"] = new() { ["missing"] = new(), ["different"] = new(), ["extra"] = new() },
            };
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary, game, "posix", engine);
            Assert.DoesNotContain("modified", msg);
            Assert.DoesNotContain("missing", msg);
            Assert.DoesNotContain("extra", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void CompiledWebclient_IgnoresPreservedLegacyFiles()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = Path.Combine(tmp, "engine", "web");
            MakeTree(Path.Combine(engine, "templates"), EngineTemplates);
            MakeTree(Path.Combine(engine, "static"), new Dictionary<string,string>{ ["webclient/index.html"] = "compiled" });
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new()
                {
                    ["webclient/index.html"] = "compiled",
                    ["webclient/js/webclient.js"] = "legacy",
                    ["webclient/css/xterm.css"] = "legacy",
                },
            });
            Assert.Null(Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void DrawEntryIdentical_ReturnsNull()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = Path.Combine(tmp, "engine", "web");
            MakeTree(Path.Combine(engine, "templates"), EngineTemplates);
            var engineStatic = new Dictionary<string,string>(EngineStatic) { ["atheriz_draw/index.html"] = "draw-v1" };
            MakeTree(Path.Combine(engine, "static"), engineStatic);
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new(engineStatic),
            });
            Assert.Null(Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void DrawEntryDifferent_FlagsModified()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = Path.Combine(tmp, "engine", "web");
            MakeTree(Path.Combine(engine, "templates"), EngineTemplates);
            var engineStatic = new Dictionary<string,string>(EngineStatic) { ["atheriz_draw/index.html"] = "draw-v1" };
            MakeTree(Path.Combine(engine, "static"), engineStatic);
            var gameStatic = new Dictionary<string,string>(EngineStatic) { ["atheriz_draw/index.html"] = "draw-v2" };
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new(gameStatic),
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            Assert.Equal(new[]{"index.html"}, summary!["atheriz_draw"]["different"].ToArray());
            Assert.Empty(summary["atheriz_draw"]["missing"]);
            Assert.Empty(summary["atheriz_draw"]["extra"]);
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary, game, "posix", engine);
            Assert.Contains("web/static/atheriz_draw", msg);
            Assert.Contains("1 modified", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void DrawEntryMissing_FlagsMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = Path.Combine(tmp, "engine", "web");
            MakeTree(Path.Combine(engine, "templates"), EngineTemplates);
            var engineStatic = new Dictionary<string,string>(EngineStatic) { ["atheriz_draw/index.html"] = "draw-v1" };
            MakeTree(Path.Combine(engine, "static"), engineStatic);
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new(EngineStatic),
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            Assert.Equal(new[]{"index.html"}, summary!["atheriz_draw"]["missing"].ToArray());
            Assert.Empty(summary["atheriz_draw"]["different"]);
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary, game, "posix", engine);
            Assert.Contains("web/static/atheriz_draw", msg);
            Assert.Contains("1 missing", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void NoEngineDrawEntry_DrawIgnored()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = MakeEngine(tmp);
            var gameStatic = new Dictionary<string,string>(EngineStatic) { ["atheriz_draw/index.html"] = "draw-v9" };
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new(gameStatic),
            });
            Assert.Null(Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void DrawOnlyEngine_WarningUsesDeployCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"wcsync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var engine = Path.Combine(tmp, "engine", "web");
            MakeTree(Path.Combine(engine, "templates"), EngineTemplates);
            MakeTree(Path.Combine(engine, "static"), new Dictionary<string,string>{ ["atheriz_draw/index.html"] = "draw-v1" });
            var game = MakeGame(tmp, new()
            {
                ["templates"] = new(EngineTemplates),
                ["static"] = new Dictionary<string,string>{ ["atheriz_draw/index.html"] = "draw-stale" },
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary!, game, null, engine);
            Assert.Contains("web/static/atheriz_draw", msg);
            Assert.Contains("deploy.py", msg);
            Assert.Contains($"game --web-root \"{Path.Combine(game, "web")}\"", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    // Engine baseline is always passed explicitly (never bare-resolved):
    // CWD is process-global and parallel tests repark it, so bare
    // ResolveEngineWeb can pick up a scaffolded game dir's web/ tree.
    // A nonexistent override mirrors exactly what the resolver returns in
    // the C# layout (contentRoot/web), exercising the same fork.
    private static readonly string NonExistentWeb = Path.Combine(Path.GetTempPath(), "atheriz_no_such_web_xyz");

    [Fact]
    public void CheckSync_WwwrootBaseline_ComparesStaticInsteadOfClean()
    {
        // C# layout: engine webclient under contentRoot/wwwroot, no web/ tree.
        // The static area must be compared against it, not reported clean.
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "static", "webclient"));
        Directory.CreateDirectory(Path.Combine(game, "web", "templates", "webclient"));
        File.WriteAllText(Path.Combine(game, "web", "static", "webclient", "app.js"), "game-v1");
        File.WriteAllText(Path.Combine(game, "web", "templates", "webclient", "extra.js"), "game-only");
        Directory.CreateDirectory(Path.Combine(env.TempPath, "wwwroot", "webclient"));
        File.WriteAllText(Path.Combine(env.TempPath, "wwwroot", "webclient", "app.js"), "engine-v1");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        var summary = WebclientSyncChecker.CheckSync(game, env.TempPath, NonExistentWeb, on);
        Assert.NotNull(summary);
        Assert.Contains("app.js", summary!["static"]["different"]);
        // No web/ baseline: game templates have nothing to judge them against,
        // so they must not be reported as extra.
        Assert.Empty(summary["templates"]["extra"]);
    }

    [Fact]
    public void CheckSync_WwwrootBaseline_IdenticalIsClean()
    {
        // Same layout with identical bytes stays clean (null).
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "static", "webclient"));
        File.WriteAllText(Path.Combine(game, "web", "static", "webclient", "app.js"), "same");
        Directory.CreateDirectory(Path.Combine(env.TempPath, "wwwroot", "webclient"));
        File.WriteAllText(Path.Combine(env.TempPath, "wwwroot", "webclient", "app.js"), "same");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        Assert.Null(WebclientSyncChecker.CheckSync(game, env.TempPath, NonExistentWeb, on));
    }

    [Fact]
    public void CheckSync_NoBaselineAtAll_ReturnsClean()
    {
        // Game web/ exists but there is no web/ baseline and no wwwroot
        // baseline anywhere: still clean (unchanged behavior).
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "static", "webclient"));
        File.WriteAllText(Path.Combine(game, "web", "static", "webclient", "app.js"), "game-v1");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        Assert.Null(WebclientSyncChecker.CheckSync(game, env.TempPath, NonExistentWeb, on));
    }
}
