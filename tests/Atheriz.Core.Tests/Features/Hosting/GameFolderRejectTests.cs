using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// `new` must stop when the game folder was refused, not move into it.
[Collection("Ported")]
public class GameFolderRejectTests
{
    [Fact]
    public async Task New_WithRejectedFolderName_DoesNotChdirOrStartServer()
    {
        // Behavior: when folder creation is rejected (invalid identifier),
        // `new` must stop without changing directory or starting a server.
        // Today CreateGameFolder at GameTemplateGenerator.cs:19-23 only prints
        // and HandleNewAsync at StopHandler.cs:421-424 unconditionally chdirs
        // and proceeds — with an existing directory it even starts a server
        // for a folder it refused to create.
        var root = Path.Combine(Path.GetTempPath(), "atheriz_newrej_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "my-game"));
            var result = await StopHandler.HandleNewAsync(new[] { "my-game", "--foreground" });
            Assert.False(result);
            Assert.Equal(root, Directory.GetCurrentDirectory());
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
