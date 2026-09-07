using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// Offline `create` must stop when the world cannot be loaded.
[Collection("Ported")]
public class OfflineCreateLoadFailureTests
{
    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    [Fact]
    public async Task Create_AfterLoadFailure_DoesNotCreateCharacter()
    {
        // Behavior: when the world cannot be loaded, offline `create` must stop
        // instead of creating the account/character against an empty registry.
        // Today the load failure at StopHandler.cs:390-396 is only logged and
        // AtCharCreate at StopHandler.cs:398 still runs (ServerEvents.cs:48),
        // so a character is created with no loaded world behind it.
        // A closed database makes the load throw deterministically with no
        // network involved (no token file exists, so the HTTP branch is skipped).
        using var env = GlobalTestEnv.Enter();
        // Pre-warm the world so creation *would* succeed if reached: cached
        // handlers plus a home node at DefaultHome in the live registry.
        GlobalServices.GetNodeHandler();
        GlobalServices.GetServerChannel();
        _ = new Node(AtherizSettings.Global.DefaultHome);
        var before = ObjectRegistry.Count;
        var settings = new AtherizSettings { SavePath = env.TempPath, SecretPath = Path.Combine(env.TempPath, "secret") };
        Directory.CreateDirectory(settings.SecretPath);
        SetEffectiveSettings(settings);
        AtherizDbContext.CloseDatabase();
        var origOut = Console.Out;
        Console.SetOut(new StringWriter());
        try
        {
            await StopHandler.HandleCreateAsync(new[] { "loadfailacc", "LoadFailChar", "supersecret123" });
        }
        finally
        {
            Console.SetOut(origOut);
            AtherizDbContext.ReopenDatabase();
            SetEffectiveSettings(null);
        }
        Assert.Equal(before, ObjectRegistry.Count);
    }
}
