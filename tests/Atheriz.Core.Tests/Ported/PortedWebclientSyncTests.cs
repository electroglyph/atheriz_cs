// Port of atheriz/tests/test_webclient_sync.py:1
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
    public void CompiledWebclient_WarningUsesDeployCommand()
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
                ["static"] = new() { ["webclient/index.html"] = "stale" },
            });
            var summary = Atheriz.Server.Infrastructure.WebclientSyncChecker.CheckSync(game, engine);
            Assert.NotNull(summary);
            var msg = Atheriz.Server.Infrastructure.WebclientSyncChecker.FormatWarning(summary!, game, null, engine);
            Assert.Contains("deploy.py", msg);
            Assert.Contains($"game --web-root \"{Path.Combine(game, "web")}\"", msg);
            Assert.DoesNotContain("npm run", msg);
            Assert.DoesNotContain("cp -r", msg);
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }
}
