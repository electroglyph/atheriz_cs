using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// reload must rebuild game plugin projects first (new commands are picked
// up), abort before contacting the server when the build fails, and never
// attempt to build the server or test projects.
[Collection("Ported")]
public class ReloadBuildTests
{
    private static int FindFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }

    private static string MakePluginDir(params string[] fileNames)
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_reloadbuild_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var name in fileNames)
        {
            var full = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "<Project />");
        }
        return root;
    }

    [Fact]
    public void FindPluginProjects_PicksGame_SkipsServerAndTests()
    {
        var root = MakePluginDir("mygame.csproj", "Atheriz.Server.csproj", "Foo.Tests.csproj");
        try
        {
            var found = ReloadHandler.FindPluginProjects(root);
            Assert.Single(found);
            Assert.Equal("mygame.csproj", Path.GetFileName(found[0]));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void FindPluginProjects_IgnoresNestedProjects()
    {
        var root = MakePluginDir(Path.Combine("sub", "nested.csproj"));
        try
        {
            Assert.Empty(ReloadHandler.FindPluginProjects(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task ReloadAsync_SuccessfulBuild_ProceedsToReload()
    {
        var root = MakePluginDir("mygame.csproj");
        try
        {
            int calls = 0;
            string? built = null;
            Task<bool> build(string csproj, CancellationToken ct)
            {
                calls++;
                built = csproj;
                return Task.FromResult(true);
            }
            Assert.Equal(1, await ReloadHandler.ReloadAsync(FindFreePort(), root, build));
            Assert.Equal(1, calls);
            Assert.Equal(Path.Combine(root, "mygame.csproj"), built);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task ReloadAsync_FailedBuild_AbortsBeforeContactingServer()
    {
        // A listener is up, but the failed build must return 1 without ever
        // connecting: the serve task stays uncompleted after ReloadAsync returns.
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var serve = listener.AcceptTcpClientAsync();
        var root = MakePluginDir("mygame.csproj");
        try
        {
            int calls = 0;
            Task<bool> build(string csproj, CancellationToken ct)
            {
                calls++;
                return Task.FromResult(false);
            }
            Assert.Equal(1, await ReloadHandler.ReloadAsync(port, root, build));
            Assert.Equal(1, calls);
            Assert.False(serve.IsCompleted);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { listener.Stop(); } catch { }
            try { await serve; } catch { }
        }
    }

    [Fact]
    public async Task ReloadAsync_NoPluginProject_SkipsBuildEntirely()
    {
        var root = MakePluginDir();
        try
        {
            Task<bool> build(string csproj, CancellationToken ct) =>
                throw new InvalidOperationException("must not be called");
            Assert.Equal(1, await ReloadHandler.ReloadAsync(FindFreePort(), root, build));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task BuildPluginAsync_BogusProject_ReturnsFalse()
    {
        var root = MakePluginDir("bogus.csproj");
        try
        {
            Assert.False(await ReloadHandler.BuildPluginAsync(
                Path.Combine(root, "bogus.csproj"), CancellationToken.None));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
