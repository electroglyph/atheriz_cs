
namespace Atheriz.Server.Cli;

public static class ReloadHandler
{
    private static readonly TimeSpan PluginBuildTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Hot reloads game logic on the running server. When the current folder
    /// holds game plugin projects they are rebuilt (Release) first, so the
    /// reload picks up fresh code. A failed build aborts before contacting
    /// the server, leaving the running code untouched.
    /// </summary>
    public static Task<int> ReloadAsync(int? portOverride)
        => ReloadAsync(portOverride, Directory.GetCurrentDirectory(), BuildPluginAsync);

    /// <summary>
    /// Build-then-reload with the plugin folder and build step injected
    /// (production passes the current folder and the dotnet build runner).
    /// </summary>
    public static async Task<int> ReloadAsync(
        int? portOverride,
        string pluginDirectory,
        Func<string, CancellationToken, Task<bool>> buildAsync)
    {
        ArgumentNullException.ThrowIfNull(pluginDirectory);
        ArgumentNullException.ThrowIfNull(buildAsync);
        using var buildTimeout = new CancellationTokenSource(PluginBuildTimeout);
        foreach (var csproj in FindPluginProjects(pluginDirectory))
        {
            bool built;
            try
            {
                built = await buildAsync(csproj, buildTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Build of {Path.GetFileName(csproj)} failed: {ex.Message}");
                return 1;
            }
            if (!built) return 1;
        }

        var settings = StopHandler.EffectiveSettingsValue;
        var port = portOverride ?? settings.WebserverPort;
        var tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}/_internal/hot_reload";
        Console.WriteLine($"Triggering hot reload at {url}...");
        var sw = Stopwatch.StartNew();
        // Single end-to-end token resolution — the helper locates
        // admin.token itself. On a scheme mismatch retry once flipped.
        var resp = await ShutdownClient.PostAdminWithTlsFallbackAsync(port, settings.SecretPath, "/_internal/hot_reload", null, tlsOn).ConfigureAwait(false);
        sw.Stop();
        if (resp is null) { Console.WriteLine($"Error connecting to server at {url}"); return 1; }
        var body = resp.Body;
        try
        {
            var status = resp.GetStatus("ok");
            var msg = resp.GetMessage();
            if (status == "ok") { Console.WriteLine($"Success! {msg}"); Console.WriteLine($"Reload took {sw.Elapsed.TotalMilliseconds:F2}ms"); return 0; }
            Console.WriteLine($"Failed: {msg}");
            return 1;
        }
        catch { Console.WriteLine($"Response: {body}"); return 1; }
    }

    /// <summary>
    /// Game plugin projects in a folder: top-level *.csproj files, excluding
    /// the server project itself and test projects (reload never builds those).
    /// </summary>
    public static IReadOnlyList<string> FindPluginProjects(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory)) return [];
        var found = new List<string>();
        foreach (var csproj in Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(csproj);
            if (name.Equals("Atheriz.Server.csproj", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)) continue;
            found.Add(csproj);
        }
        return found;
    }

    /// <summary>
    /// Runs dotnet build on one plugin project (Release). Build output is
    /// printed only on failure. Returns false when the SDK is missing, the
    /// build fails, or the timeout elapses.
    /// </summary>
    public static async Task<bool> BuildPluginAsync(string csprojPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csprojPath);
        Console.WriteLine($"Building {Path.GetFileName(csprojPath)}...");
        var output = new StringBuilder();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Release --nologo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        try
        {
            if (!process.Start())
            {
                Console.WriteLine($"Could not start dotnet to build {csprojPath}.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not start dotnet to build {csprojPath}: {ex.Message}");
            return false;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Build timed out; stopping the build left: {ex.Message}");
            }
            Console.WriteLine($"Build of {csprojPath} timed out after {PluginBuildTimeout.TotalMinutes:F0} minutes.");
            return false;
        }
        if (process.ExitCode != 0)
        {
            Console.WriteLine($"Build of {csprojPath} failed (exit {process.ExitCode}):");
            Console.WriteLine(output.ToString());
            return false;
        }
        Console.WriteLine($"Built {Path.GetFileName(csprojPath)}.");
        return true;
    }
}
