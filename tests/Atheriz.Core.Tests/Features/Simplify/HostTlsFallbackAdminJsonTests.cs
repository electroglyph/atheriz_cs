using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared TLS-flipped retry + per-caller admin JSON defaults: create ("error"),
// reload ("ok"), shutdown (""). Unifying the default would change behavior.
[Collection("Ported")]
public class HostTlsFallbackAdminJsonTests
{
    private static Type AdminResponseType()
        => typeof(ShutdownClient).Assembly.GetType("Atheriz.Server.Cli.AdminResponse")!;

    private static object NewResponse(string body)
    {
        var t = AdminResponseType();
        Assert.NotNull(t);
        var ctor = t!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return ctor.Invoke([200, body]);
    }

    private static string? GetStatus(object resp, string defaultValue)
    {
        var m = AdminResponseType().GetMethod("GetStatus", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string?)m.Invoke(resp, [defaultValue]);
    }

    private static string? GetMessage(object resp)
    {
        var m = AdminResponseType().GetMethod("GetMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string?)m.Invoke(resp, null);
    }

    [Fact]
    public void AdminJson_StatusAndMessage_Read()
    {
        var resp = NewResponse("""{"status":"ok","message":"hi"}""");
        Assert.Equal("ok", GetStatus(resp, "error"));
        Assert.Equal("hi", GetMessage(resp));
    }

    [Fact]
    public void AdminJson_MissingStatus_UsesCallerDefault()
    {
        var resp = NewResponse("""{"message":"m"}""");
        Assert.Equal("error", GetStatus(resp, "error"));
        Assert.Equal("ok", GetStatus(resp, "ok"));
        Assert.Equal("", GetStatus(resp, ""));
    }

    [Fact]
    public void AdminJson_MissingMessage_FallsBackToBody()
    {
        var body = """{"status":"ok"}""";
        Assert.Equal(body, GetMessage(NewResponse(body)));
    }

    [Fact]
    public void Shutdown_UsesTlsFallbackRetry_WithEmptyDefault()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        var region = SourceScan.Region(src, "internal static async Task<ShutdownRequestResult> TryRequestShutdownAsync(");
        Assert.Contains("PostAdminWithTlsFallbackAsync", region);
        Assert.Contains("GetStatus(\"\")", region);
    }

    [Fact]
    public void Create_And_Reload_KeepTheirOwnDefaults()
    {
        var create = SourceScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("PostAdminWithTlsFallbackAsync", create);
        Assert.Contains("GetStatus(\"error\")", create);
        var reload = SourceScan.Read("src", "Atheriz.Server", "Cli", "ReloadHandler.cs");
        Assert.Contains("PostAdminWithTlsFallbackAsync", reload);
        Assert.Contains("GetStatus(\"ok\")", reload);
    }
}
