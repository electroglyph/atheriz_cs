using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atheriz.Server.Hosting;

// Typed admin contract: {"status":...,"message":...} — lowercase names match
// what the CLI clients parse (case-sensitive "status"/"message" reads).
public sealed record AdminResult(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("message")] string? Message);

// Bearer admin auth: loopback peer + token file compare. The token file is
// re-read per request (no cache — a 64-byte read is cheaper than
// cache-coherence doubt, and rotation applies immediately).
public sealed class AdminTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Admin";
    public const string FailureItem = "atheriz.admin_failure";

    public AdminTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var services = Context.RequestServices;
        var settings = services.GetService(typeof(AtherizSettings)) as AtherizSettings
            ?? (services.GetService(typeof(IOptionsMonitor<AtherizSettings>)) as IOptionsMonitor<AtherizSettings>)?.CurrentValue
            ?? AtherizSettings.Global;
        var remoteIp = Context.Connection.RemoteIpAddress?.ToString();
        var provided = Request.Headers["X-Admin-Token"].FirstOrDefault() ?? string.Empty;
        var error = AdminToken.CheckAdmin(settings.SecretPath, remoteIp, provided, "admin");
        if (error is not null)
        {
            Context.Items[FailureItem] = error;
            return Task.FromResult(AuthenticateResult.Fail(error));
        }
        var identity = new ClaimsIdentity(SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

// Authorization failures answer 401 with the same {"status","message"}
// JSON shape the endpoints use, so CLI clients keep body-parsing.
public sealed class AdminAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _inner = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged || authorizeResult.Forbidden)
        {
            var message = context.Items.TryGetValue(AdminTokenHandler.FailureItem, out var err) && err is string s && !string.IsNullOrEmpty(s)
                ? s
                : "Unauthorized.";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new AdminResult("error", message));
            return;
        }
        await _inner.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class AdminAuthServices
{
    public const string Policy = "Admin";

    public static void AddAdminAuth(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AdminTokenHandler>(AdminTokenHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, p => p.RequireAuthenticatedUser().AddAuthenticationSchemes(AdminTokenHandler.SchemeName));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminAuthorizationResultHandler>();
    }
}
