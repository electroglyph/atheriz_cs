using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class Audit10ResidualBatch1Tests
{
    [Fact]
    public void DbContextSettingsCtor_HonorsEnvOverride()
    {
        using var env = GlobalTestEnv.Enter();
        var dirSettings = Path.Combine(env.TempPath, "ctor-settings");
        var dirEnv = Path.Combine(env.TempPath, "ctor-env");
        Directory.CreateDirectory(dirSettings);
        Directory.CreateDirectory(dirEnv);
        var settings = new AtherizSettings { SavePath = dirSettings, SecretPath = dirSettings };
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", dirEnv);
        try
        {
            using (var db = new AtherizDbContext(settings))
            {
                db.Database.EnsureCreated();
            }
            Assert.True(File.Exists(Path.Combine(dirEnv, "database.sqlite3")));
            Assert.False(File.Exists(Path.Combine(dirSettings, "database.sqlite3")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", orig);
        }
    }

    [Fact]
    public void MapHandler_Save_WritesConstructedSettingsDatabase()
    {
        using var env = GlobalTestEnv.Enter();
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", null);
        try
        {
            var dirA = Path.Combine(env.TempPath, "mapsave-a");
            Directory.CreateDirectory(dirA);
            var settingsA = new AtherizSettings { SavePath = dirA, SecretPath = dirA };
            var mh = new MapHandler(settingsA, autoLoad: false);
            var mi = new MapInfo { Name = "save-pin-area" };
            mi.SetPreCell((1, 2), "@");
            mh.SetMapInfo("save-pin-area", 0, mi);
            mh.Save(force: true);
            Assert.True(File.Exists(Path.Combine(dirA, "database.sqlite3")));

            var reloaded = new MapHandler(settingsA);
            var got = reloaded.GetMapInfo("save-pin-area", 0);
            Assert.NotNull(got);
            Assert.Equal("@", got!.PreGrid[(1, 2)]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", orig);
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void MapHandler_Save_HonorsEnvOverrideOverSettings()
    {
        using var env = GlobalTestEnv.Enter();
        var dirA = Path.Combine(env.TempPath, "mapsave-env-a");
        var dirEnv = Path.Combine(env.TempPath, "mapsave-env-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirEnv);
        var settingsA = new AtherizSettings { SavePath = dirA, SecretPath = dirA };
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", dirEnv);
        try
        {
            var mh = new MapHandler(settingsA, autoLoad: false);
            var mi = new MapInfo { Name = "env-win-area" };
            mi.SetPreCell((3, 4), "#");
            mh.SetMapInfo("env-win-area", 0, mi);
            mh.Save(force: true);
            Assert.True(File.Exists(Path.Combine(dirEnv, "database.sqlite3")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", orig);
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void Node_RegisterPersistedSubtype_SameNameNewType_PrunesOldTypeKey()
    {
        using var _ = GlobalTestEnv.Enter();
        const string name = "audit10residuenode10a";
        Node.RegisterPersistedSubtype(name, typeof(Node), c => new Node(c));
        Node.RegisterPersistedSubtype(name, typeof(TestNodePruneA), c => new TestNodePruneA(c));
        var field = typeof(Node).GetField("_persistedSubtypeNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var dict = (System.Collections.IDictionary)field.GetValue(null)!;
        int ours = dict.Keys.Cast<object>().Count(k => k is Type && dict[k] is string s && s == name);
        Assert.Equal(1, ours);
        Assert.True(Node.TryCreatePersistedSubtype(name, new Coord("limbo", 0, 0, 0), out var node));
        Assert.IsType<TestNodePruneA>(node);
    }

    private sealed class TestNodePruneA(Coord c) : Node(c)
    {
    }

    [Fact]
    public void EvictStaleCommands_NullAssembly_LogsAndReturnsZero()
    {
        using var _ = GlobalTestEnv.Enter();
        var err = new StringWriter();
        var orig = Console.Error;
        Console.SetError(err);
        try
        {
            int n = Atheriz.Core.Plugins.PluginReloader.EvictStaleCommands(null, []);
            Assert.Equal(0, n);
            Assert.Contains("no prior assembly", err.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(orig);
        }
    }

    [Fact]
    public void MigrateTransitions_Log_NamesConstraintViolations()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContextFactory.cs");
        Assert.Contains("constraint-violating", src, StringComparison.Ordinal);
    }
}
