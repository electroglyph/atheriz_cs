using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Atheriz.Core.Tests.Features.Hosting;

// The CLI reload triggers the server's /_internal/hot_reload route and
// never builds locally: the server runs the same build-first path as the
// in-game `reload` verb, so both verbs share one behavior. Discovery and
// build helpers live on PluginBuilder (pinned here); the CLI is a thin
// trigger reporting the server's message. The Slow test drives the real
// CLI path end to end: HTTP POST, server-side build, source edit, reload,
// new behavior live.
[Collection("Ported")]
public class ReloadBuildTests
{
    private const string AlphaGeneration = "GENERATION_ALPHA_7F3A9C";
    private const string BetaGeneration = "GENERATION_BETA_4D2E81";

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

    // Locate the engine project without hardcoding the checkout path (same
    // shape as PluginRebuildReloadTests: walk up from the test binaries).
    private static string CoreCsprojPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d is not null)
            {
                var cand = Path.Combine(d.FullName, "src", "Atheriz.Core", "Atheriz.Core.csproj");
                if (File.Exists(cand)) return cand;
                d = d.Parent;
            }
        }
        throw new InvalidOperationException("Atheriz.Core.csproj not found above test binaries or CWD.");
    }

    private static void WriteDotnetProject(string dir, string name, string generation)
    {
        var coreCsproj = CoreCsprojPath();
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{coreCsproj}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Probe.cs"),
            $$"""
            using Atheriz.Core.Objects;
            using Atheriz.Core.Plugins;
            [assembly: EntityReplacement(typeof(GameObject), typeof(MiniPlugin.ProbeWidget))]
            namespace MiniPlugin;
            public sealed class ProbeWidget : GameObject
            {
                public const string Generation = "{{generation}}";
            }
            """);
    }

    private static string? LiveProbeGeneration()
    {
        var probe = ObjectRegistry.FilterBy(o =>
            string.Equals(o.GetType().FullName, "MiniPlugin.ProbeWidget", StringComparison.Ordinal)).FirstOrDefault();
        var field = probe?.GetType().GetField("Generation", BindingFlags.Public | BindingFlags.Static);
        return field?.GetRawConstantValue() as string;
    }

    private static bool DllContainsGeneration(string dllPath, string generation)
    {
        var dll = File.ReadAllBytes(dllPath);
        var needle = System.Text.Encoding.Unicode.GetBytes(generation);
        return dll.AsSpan().IndexOf(needle) >= 0;
    }

    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    [Fact]
    public void FindPluginProjects_PicksGame_SkipsServerAndTests()
    {
        var root = MakePluginDir("mygame.csproj", "Atheriz.Server.csproj", "Foo.Tests.csproj");
        try
        {
            var found = PluginBuilder.FindPluginProjects(root);
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
            Assert.Empty(PluginBuilder.FindPluginProjects(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task ReloadAsync_UnreachableServer_ReturnsOne()
    {
        // No server on the port (and no token): the trigger reports the
        // connection failure instead of throwing.
        Assert.Equal(1, await ReloadHandler.ReloadAsync(FindFreePort()));
    }

    [Fact]
    public async Task BuildPluginAsync_BogusProject_ReturnsFalse()
    {
        var root = MakePluginDir("bogus.csproj");
        try
        {
            Assert.False(await PluginBuilder.BuildAsync(
                Path.Combine(root, "bogus.csproj"), CancellationToken.None));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task CliReload_EndToEnd_ServerBuildsAndPicksUpChange()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_clireload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var gameDir = Path.Combine(tmp, "game");
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(Path.Combine(gameDir, "save"));
        var secret = Path.Combine(tmp, "secret");
        Directory.CreateDirectory(secret);
        await File.WriteAllTextAsync(Path.Combine(secret, "admin.token"), new string('a', 64));
        try
        {
            WriteDotnetProject(gameDir, "minigame", AlphaGeneration);
            var settings = new AtherizSettings
            {
                SavePath = Path.Combine(gameDir, "save"),
                SecretPath = secret,
                ServerName = "CliReloadPin",
                NetworkProtocols = Array.Empty<string>(),
                AutosaveOnShutdown = false,
                TimeSystemEnabled = false,
            };
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = tmp });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(settings);
            AdminAuthServices.AddAdminAuth(builder.Services);
            await using var app = builder.Build();
            app.MapAdminRoutes(settings);
            app.UseAuthentication();
            app.UseAuthorization();
            await app.StartAsync();
            try
            {
                var addr = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
                var port = new Uri(addr).Port;
                SetEffectiveSettings(new AtherizSettings
                {
                    SecretPath = secret,
                    WebserverPort = port,
                });
                ObjectRegistry.AddObject(new GameObject());

                // No bin output staged: the server must build it (the CLI
                // itself never builds — a bogus local project would abort
                // under the old split, here it is simply never consulted).
                Assert.Equal(0, await ReloadHandler.ReloadAsync(port));
                Assert.Equal(AlphaGeneration, LiveProbeGeneration());

                // Edit the source; the CLI trigger must pick up the new
                // generation via the server-side build.
                WriteDotnetProject(gameDir, "minigame", BetaGeneration);
                Assert.Equal(0, await ReloadHandler.ReloadAsync(port));
                var dllPath = Path.Combine(gameDir, "bin", "Release", "net10.0", "minigame.dll");
                Assert.True(DllContainsGeneration(dllPath, BetaGeneration), "rebuilt dll must contain the new generation");
                Assert.Equal(BetaGeneration, LiveProbeGeneration());
            }
            finally
            {
                SetEffectiveSettings(null);
                try { await app.StopAsync(); } catch { }
            }
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
