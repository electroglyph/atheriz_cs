using Atheriz.Core;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// World-creation dispatch pins: reset/new run the registered game setup when
// present, else the engine template. No game types here — a local stub.
[Collection("Ported")]
public class GameSetupDispatchTests
{
    private sealed class StubSetup : IGameSetup
    {
        public List<string> Calls = [];
        public void DoSetup(string savePath, string? username, string? password, string? secretPath, bool prompt)
            => Calls.Add(string.Join("|", savePath, username, password, secretPath, prompt));
    }

    // Staged as the fake game (StageFakeGame copies this assembly): discovery
    // must instantiate the public IGameSetup entry on load, because module
    // initializers do not run eagerly on collectible-ALC loads.
    public sealed class StubGameSetup : IGameSetup
    {
        public void DoSetup(string savePath, string? username, string? password, string? secretPath, bool prompt) { }
    }

    [Fact]
    public void RunSetup_GameRegistered_UsesGameSetup()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InitialSetup.GameSetup;
        var stub = new StubSetup();
        try
        {
            InitialSetup.GameSetup = stub;
            InitialSetup.RunSetup("somedir/save", "u", "p", "somedir/secret", prompt: false);
            Assert.Single(stub.Calls);
            Assert.Contains("somedir/save|u|p|somedir/secret|False", stub.Calls[0]);
        }
        finally { InitialSetup.GameSetup = prev; }
    }

    [Fact]
    public void RunSetup_NoGame_FallsBackToTemplate()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = InitialSetup.GameSetup;
        try
        {
            InitialSetup.GameSetup = null;
            // Template setup into a temp dir must not throw and must build limbo.
            string dir = Path.Combine(Path.GetTempPath(), "setup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                InitialSetup.RunSetup(Path.Combine(dir, "save"), prompt: false);
                Assert.True(Directory.Exists(Path.Combine(dir, "save")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
        finally { InitialSetup.GameSetup = prev; }
    }

    [Fact]
    public void BootLoad_GameSetupEntry_InstantiatedOnLoad()
    {
        // StageFakeGame copies this assembly: its public StubGameSetup must be
        // instantiated and registered by discovery (compared by name — the
        // instance lives in the plugin ALC by design).
        using var env = GlobalTestEnv.Enter();
        var prev = InitialSetup.GameSetup;
        try
        {
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
                Atheriz.Core.Plugins.PluginReloader.LoadGameAssembliesAtBoot(settings);
                Assert.NotNull(InitialSetup.GameSetup);
                Assert.Equal(typeof(StubGameSetup).FullName, InitialSetup.GameSetup!.GetType().FullName);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
        finally { InitialSetup.GameSetup = prev; }
    }
}
