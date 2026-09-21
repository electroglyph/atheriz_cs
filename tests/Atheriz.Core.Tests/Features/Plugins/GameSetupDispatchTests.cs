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
}
