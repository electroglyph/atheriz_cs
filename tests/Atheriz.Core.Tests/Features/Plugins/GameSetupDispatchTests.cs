using Atheriz.Core;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// World-creation dispatch pins: reset/new run the game setup handed over
// from discovery when present, else the engine template. No game types
// here — a local stub. The game value rides an explicit parameter, so
// these tests pass it directly with no global save/restore.
[Collection("Ported")]
public class GameSetupDispatchTests
{
    private sealed class StubSetup : IGameSetup
    {
        public List<string> Calls = [];
        public void DoSetup(SetupOptions options)
            => Calls.Add(string.Join("|", options.SavePath, options.Username, options.Password, options.SecretPath, options.Prompt));
    }

    // Staged as the fake game (StageFakeGame copies this assembly): discovery
    // must instantiate the public IGameSetup entry on load, because module
    // initializers do not run eagerly on collectible-ALC loads.
    public sealed class StubGameSetup : IGameSetup
    {
        public void DoSetup(SetupOptions options) { }
    }

    [Fact]
    public void RunSetup_GameRegistered_UsesGameSetup()
    {
        using var env = GlobalTestEnv.Enter();
        var stub = new StubSetup();
        InitialSetup.RunSetup(new SetupOptions("somedir/save", "u", "p", "somedir/secret", Prompt: false), stub);
        Assert.Single(stub.Calls);
        Assert.Contains("somedir/save|u|p|somedir/secret|False", stub.Calls[0]);
    }

    [Fact]
    public void RunSetup_NoGame_FallsBackToTemplate()
    {
        using var env = GlobalTestEnv.Enter();
        // Template setup into a temp dir must not throw and must build limbo.
        string dir = Path.Combine(Path.GetTempPath(), "setup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            InitialSetup.RunSetup(new SetupOptions(Path.Combine(dir, "save"), Prompt: false));
            Assert.True(Directory.Exists(Path.Combine(dir, "save")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BootLoad_GameSetupEntry_InstantiatedOnLoad()
    {
        // StageFakeGame copies this assembly: its public StubGameSetup must be
        // instantiated and handed back by discovery (compared by name — the
        // instance lives in the plugin ALC by design).
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_setupgame_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, "setupgame.csproj");
        File.WriteAllText(csproj, "<Project></Project>");
        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(-5));
        var binDir = Path.Combine(dir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(binDir);
        var dllPath = Path.Combine(binDir, "setupgame.dll");
        File.Copy(typeof(GameSetupDispatchTests).Assembly.Location, dllPath);
        File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow);
        try
        {
            var settings = new Atheriz.Core.Settings.AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Atheriz.Core.Plugins.PluginReloader.LoadGameAssembliesAtBoot(settings, out var game);
            Assert.NotNull(game);
            Assert.Equal(typeof(StubGameSetup).FullName, game!.GetType().FullName);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
