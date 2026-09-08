using Atheriz.Core.Settings;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace Atheriz.Core.Tests.Features.Hosting;

// AdminRoutes / StaticFileConfig / ProtocolBootstrap / ReloadHandler exercised
// through a real in-process Kestrel on a dynamic localhost port (no TestHost package).
[Collection("Ported")]
public class ServerHostingDirectTests
{
    private sealed class Booted : IAsyncDisposable
    {
        public WebApplication App { get; init; } = null!;
        public HttpClient Client { get; init; } = null!;
        public string Tmp { get; init; } = string.Empty;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            try { Directory.Delete(Tmp, true); } catch { }
        }
    }

    private static async Task<Booted> BootAsync(bool withWwwroot = false)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ahost_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        if (withWwwroot)
        {
            var assets = Path.Combine(tmp, "wwwroot", "assets");
            Directory.CreateDirectory(assets);
            await File.WriteAllTextAsync(Path.Combine(assets, "app.ab12cd34ef.js"), "console.log(1);");
            await File.WriteAllTextAsync(Path.Combine(assets, "app.js"), "console.log(1);");
            // Hashed bundle outside assets/: proves the hash-infix rule beyond the prefix rule.
            await File.WriteAllTextAsync(Path.Combine(tmp, "wwwroot", "app.ab12cd34ef.js"), "console.log(1);");
        }
        var settings = new AtherizSettings
        {
            SecretPath = Path.Combine(tmp, "secret"),
            ServerName = "TestSrv",
            NetworkProtocols = Array.Empty<string>(),
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = tmp });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapAdminRoutes(settings);
        StaticFileConfig.Configure(app, settings);
        ProtocolBootstrap.RegisterProtocols(app, settings);
        await app.StartAsync();
        var addr = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
        return new Booted
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(addr), Timeout = TimeSpan.FromSeconds(10) },
            Tmp = tmp,
        };
    }

    private static async Task<JsonDocument> PostJson(HttpClient c, string path)
    {
        var resp = await c.PostAsync(path, new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task AdminRoutes_Shutdown_NoToken_Returns200ErrorContract()
    {
        // atheriz.py:348-350 port — auth failures stay HTTP 200 {status:error}.
        await using var b = await BootAsync();
        using var doc = await PostJson(b.Client, "/_internal/shutdown");
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task AdminRoutes_HotReload_NoToken_Returns200ErrorFast()
    {
        // Auth gate runs before any ticker/plugin work — must return quickly.
        await using var b = await BootAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var resp = await b.Client.PostAsync("/_internal/hot_reload", new StringContent(string.Empty), cts.Token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task StaticFile_Health_ReturnsOkWithServerName()
    {
        await using var b = await BootAsync();
        var body = await b.Client.GetStringAsync("/health");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("TestSrv", doc.RootElement.GetProperty("server").GetString());
    }

    [Fact]
    public async Task StaticFile_IndexNoFiles_NoCacheHeadersAndFallbackBody()
    {
        await using var b = await BootAsync();
        var resp = await b.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("no-store", resp.Headers.GetValues("Cache-Control").First());
        Assert.Contains("no-cache", resp.Headers.GetValues("Pragma").First());
        // No wwwroot files: the compiled-missing fallback template (title AtheriZ;
        // the fallback does not interpolate the configured server name).
        Assert.Contains("AtheriZ", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticFile_Ready_RouteExists()
    {
        // Status depends on global ServerLifecycle state — only pin the route shape.
        await using var b = await BootAsync();
        var resp = await b.Client.GetAsync("/ready");
        Assert.True(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.ServiceUnavailable);
        Assert.Contains("server", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticFile_HashedBundle_ServedImmutable_PlainIsNot()
    {
        await using var b = await BootAsync(withWwwroot: true);
        var hashed = await b.Client.GetAsync("/static/assets/app.ab12cd34ef.js");
        Assert.Equal(HttpStatusCode.OK, hashed.StatusCode);
        Assert.Contains("immutable", hashed.Headers.GetValues("Cache-Control").First());
        var plain = await b.Client.GetAsync("/static/assets/app.js");
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        // Pre-existing assets/* prefix rule makes everything under assets/ immutable.
        Assert.Contains("immutable", string.Join(";", plain.Headers.GetValues("Cache-Control")));
        // The hash-infix rule covers hashed bundles outside assets/.
        var rooted = await b.Client.GetAsync("/static/app.ab12cd34ef.js");
        Assert.Equal(HttpStatusCode.OK, rooted.StatusCode);
        Assert.Contains("immutable", rooted.Headers.GetValues("Cache-Control").First());
    }

    [Fact]
    public async Task ReloadHandler_MissingToken_ReturnsQuietly()
    {
        // Port 1 listens on nothing; global secret path has no token in test env.
        await ReloadHandler.HandleReloadAsync(new[] { "--port", "1" });
    }

    [Fact]
    public async Task ShutdownClient_NoToken_ReturnsNull()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "aclient_" + Guid.NewGuid().ToString("N"));
        try
        {
            // PostAdminAsync is internal (StopSafetyTests pattern): invoke via reflection.
            var m = typeof(ShutdownClient).GetMethod("PostAdminAsync", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m);
            var task = (Task)m!.Invoke(null, new object?[] { 1, Path.Combine(tmp, "secret"), "/_internal/x", null, false })!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task);
            Assert.Null(result);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
