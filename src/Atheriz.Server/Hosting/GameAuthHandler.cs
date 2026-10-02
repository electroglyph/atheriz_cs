using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Atheriz.Server.Hosting;

// Bearer authentication for the landing-page admin API ("Game" scheme).
// The token comes from POST /api/login (Authorization: Bearer or ?token=
// for event-source style fetches). Missing token yields NoResult (so the
// endpoint falls through to its 401), unknown/revoked token fails closed.
public sealed class GameAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Game";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            token = Request.Query["token"];
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var service = Context.RequestServices.GetService(typeof(GameTokenService)) as GameTokenService;
        var account = service?.Resolve(token);
        if (account is null)
            return Task.FromResult(AuthenticateResult.Fail("Unknown or revoked game token."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Name),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
