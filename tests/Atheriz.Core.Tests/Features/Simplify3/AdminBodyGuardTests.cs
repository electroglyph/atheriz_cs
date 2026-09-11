using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Hosting;
using Atheriz.Server.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Shared 4096-byte guard for the hot_reload/shutdown admin routes:
// oversized declared bodies are rejected with byte-identical JSON,
// a missing Content-Length (chunked) still falls through.
[Collection("Ported")]
public class AdminBodyGuardTests
{
    private sealed class Booted : IAsyncDisposable
    {
        public WebApplication App { get; init; } = null!;
        public HttpClient Client { get; init; } = null!;
        public string Token { get; init; } = string.Empty;
        public string Tmp { get; init; } = string.Empty;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            try { Directory.Delete(Tmp, true); } catch { }
        }
    }

    private static async Task<Booted> BootAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "aguard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var secretPath = Path.Combine(tmp, "secret");
        var token = AdminToken.EnsureToken(secretPath);
        var settings = new AtherizSettings
        {
            SecretPath = secretPath,
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
            Token = token,
            Tmp = tmp,
        };
    }

    private static async Task<JsonDocument> PostSizedBody(HttpClient c, string path, string token, int size)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("X-Admin-Token", token);
        req.Content = new ByteArrayContent(new byte[size]);
        var resp = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    private static bool IsBodyTooLarge(long? contentLength)
    {
        var m = typeof(AdminRoutes).GetMethod("IsBodyTooLarge", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentLength = contentLength;
        return (bool)m!.Invoke(null, new object?[] { ctx })!;
    }

    [Fact]
    public void IsBodyTooLarge_NullContentLength_ReturnsFalse()
    {
        // Chunked bodies (no declared length) fall through to the handlers.
        Assert.False(IsBodyTooLarge(null));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4096, false)]
    [InlineData(4097, true)]
    [InlineData(100000, true)]
    public void IsBodyTooLarge_BoundarySizes_MatchDeclaredLimit(long size, bool expected)
    {
        Assert.Equal(expected, IsBodyTooLarge(size));
    }

    [Fact]
    public async Task HotReload_OversizedBody_ReturnsTooLargeError()
    {
        await using var b = await BootAsync();
        using var doc = await PostSizedBody(b.Client, "/_internal/hot_reload", b.Token, 5000);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Request body too large.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Shutdown_OversizedBody_ReturnsTooLargeErrorWithoutQueueing()
    {
        await using var b = await BootAsync();
        using var doc = await PostSizedBody(b.Client, "/_internal/shutdown", b.Token, 5000);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Request body too large.", doc.RootElement.GetProperty("message").GetString());
        // Early return runs before the shutdown queueing: the app still serves.
        using var health = JsonDocument.Parse(await b.Client.GetStringAsync("/health"));
        Assert.Equal("ok", health.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateAccount_SmallInvalidBody_DoesNotUseAdminLimit()
    {
        // create_account caps through its own 64 KiB JSON reader, not the
        // 4096-byte admin guard: an empty body reports invalid JSON.
        await using var b = await BootAsync();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/_internal/create_account");
        req.Headers.Add("X-Admin-Token", b.Token);
        req.Content = new StringContent(string.Empty);
        var resp = await b.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Invalid JSON body.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void AdminBodyGuard_DefinedOnce_UsedTwice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.Equal(1, SourceScan.Count(src, "MaxAdminBodyBytes = 4096"));
        Assert.Equal(2, SourceScan.Count(src, "IsBodyTooLarge(ctx)"));
        Assert.Equal(0, SourceScan.Count(src, "ContentLength > 4096"));
        Assert.Equal(2, SourceScan.Count(src, "\"Request body too large.\""));
        Assert.Contains("ReadCappedJsonBodyAsync(ctx, 64 * 1024)", src);
    }
}
