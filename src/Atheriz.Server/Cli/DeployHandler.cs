using Atheriz.Server.Deployment;

namespace Atheriz.Server.Cli;

// `deploy game`: stage the engine's built webclient into a game web
// folder. Paths resolve from the engine checkout (never the game CWD)
// and are injected for tests; production passes Console streams.
public static class DeployHandler
{
    public static Task<int> DeployGameAsync(string webRoot, bool noClean)
        => Task.FromResult(DeployGame(webRoot, noClean, Console.Out, Console.Error));

    internal static int DeployGame(string webRoot, bool noClean, TextWriter log, TextWriter err)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(err);
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            err.WriteLine("error: --web-root <game/web> is required");
            return 1;
        }
        var root = WebDeployer.FindEngineRoot();
        if (root is null)
        {
            err.WriteLine("error: engine checkout not found (set ATHERIZ_ROOT to the engine checkout)");
            return 1;
        }
        try
        {
            return WebDeployer.DeployGame(
                Path.Combine(root, "webclient", "dist"),
                Path.Combine(root, "webclient", "fonts"),
                Path.Combine(root, "src", "Atheriz.Server", "web", "templates", "index.html"),
                webRoot, clean: !noClean, log, err);
        }
        catch (Exception ex)
        {
            err.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
