using Atheriz.Core.Objects;
using Microsoft.AspNetCore.Authorization;

namespace Atheriz.Server.Hosting;

// Landing-page admin API (/api/*). Bearer "Game" tokens from POST /api/login;
// every other endpoint requires builder-or-above (effective account level).
// Reads and writes go through the same ObjectRegistry/SetHelper paths as the
// in-game commands, so HTTP sees the live world and edits persist at the
// next save checkpoint.
public static class GameAdminRoutes
{
    public sealed record LoginRequest(string Account, string Password);
    public sealed record CharacterJson(int Id, string Name, int Privilege, bool Banned);
    public sealed record AccountJson(int Id, string Name, int Privilege, bool Builder, bool Admin, List<CharacterJson> Characters);

    private static RouteHandlerBuilder RequireGameAuth(RouteHandlerBuilder builder) =>
        builder.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = GameAuthHandler.SchemeName });

    public static void MapGameAdminRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapPost("/login", LoginAsync);
        RequireGameAuth(api.MapPost("/logout", LogoutAsync));
        RequireGameAuth(api.MapGet("/me", MeAsync));
        RequireGameAuth(api.MapGet("/objects", ListAsync));
        RequireGameAuth(api.MapGet("/objects/{id:int}", GetAsync));
        RequireGameAuth(api.MapPatch("/objects/{id:int}", PatchAsync));
        RequireGameAuth(api.MapDelete("/objects/{id:int}/extras/{key}", DeleteExtraAsync));
    }

    private static AccountJson ToAccountJson(Account account)
    {
        var level = AdminAccess.EffectiveLevel(account);
        var chars = new List<CharacterJson>();
        foreach (var cid in account.Characters)
        {
            var c = ObjectRegistry.GetSingle(cid);
            if (c is null || c.IsDeleted) continue;
            chars.Add(new CharacterJson(c.Id, c.Name, (int)c.PrivilegeLevel, c.IsBanned));
        }
        return new AccountJson(account.Id, account.Name, (int)level,
            level >= Privilege.Builder, level >= Privilege.Admin, chars);
    }

    // Credential checks mirror ConnectCommand.TryAuthenticate: dummy hash on
    // unknown accounts (timing), ban gates before and after the PBKDF2
    // window, per-host failure counting with cooldown bans.
    private static async Task<IResult> LoginAsync(HttpContext ctx, GameTokenService tokens)
    {
        LoginRequest? body;
        try { body = await ctx.Request.ReadFromJsonAsync<LoginRequest>().ConfigureAwait(false); }
        catch { return Results.BadRequest(new { message = "Invalid request body." }); }
        if (body is null || string.IsNullOrWhiteSpace(body.Account) || body.Password is null)
            return Results.BadRequest(new { message = "Account and password are required." });

        string host;
        try { host = ctx.Connection.RemoteIpAddress?.ToString() ?? "?"; } catch { host = "?"; }
        var settings = AtherizSettings.Global;

        // Throttled hosts wait out the cooldown: same FailedLogins/BanIp
        // state the in-band login feeds, answered 429 instead of burning
        // another PBKDF2 round (or leaking ban state via timing).
        if (AdminAccess.IsLoginThrottled(host))
            return Results.Json(
                new { message = "Too many failed login attempts. Try again later." },
                statusCode: 429);

        var accounts = ObjectRegistry.FilterBy(x =>
            x.IsAccount && x.Name.Equals(body.Account.Trim(), StringComparison.OrdinalIgnoreCase));
        if (accounts.Count == 0)
        {
            try { Account.HashPassword(body.Password); } catch { }
            return Results.Unauthorized();
        }
        if (accounts.Count > 1)
            return Results.Problem("Multiple accounts share that name. Please contact server admin.", statusCode: 500);
        var account = (Account)accounts[0];
        if (account.IsBanned)
            return Results.Json(
                new { message = $"You have been banned from this server. Reason: {account.BanReason ?? "None specified"}" },
                statusCode: 403);
        if (!account.CheckPassword(body.Password))
        {
            int attempts = 0;
            if (host != "?")
                attempts = ObjectRegistry.FailedLogins.AddOrUpdate(host, (exists, cur) => cur + 1);
            if (attempts >= settings.MaxLoginAttempts && host != "?")
                ObjectRegistry.BanIp(host, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + settings.LoginAttemptCooldown);
            return Results.Unauthorized();
        }
        if (host != "?")
        {
            try { ObjectRegistry.FailedLogins.Remove(host); } catch { }
        }
        // Re-verify the ban after the PBKDF2 window, like the in-band login.
        if (account.IsBanned)
            return Results.Json(
                new { message = $"You have been banned from this server. Reason: {account.BanReason ?? "None specified"}" },
                statusCode: 403);
        return Results.Json(new { token = tokens.Mint(account.Id), account = ToAccountJson(account) });
    }

    private static IResult LogoutAsync(HttpContext ctx, GameTokenService tokens)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            tokens.Revoke(header["Bearer ".Length..].Trim());
        return Results.Json(new { ok = true });
    }

    private static IResult MeAsync(HttpContext ctx)
    {
        var account = AdminAccess.GetAccount(ctx);
        if (account is null) return Results.Unauthorized();
        return Results.Json(ToAccountJson(account));
    }

    private static readonly HashSet<string> ListTypes = new(StringComparer.OrdinalIgnoreCase)
        { "all", "player", "room", "item", "npc", "account", "channel", "other", "script" };

    private static bool MatchesQuery(GameObject o, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        query = query.Trim();
        if (query.StartsWith('#') && int.TryParse(query[1..], out var id)) return o.Id == id;
        if (query.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            var tag = query["tag:".Length..].Trim();
            if (tag.Length == 0) return true;
            try { return o.TagsSnapshot.Contains(tag); } catch { return false; }
        }
        return o.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static IResult ListAsync(HttpContext ctx, string? type, string? q, int? limit, int? offset)
    {
        var account = AdminAccess.GetAccount(ctx);
        if (account is null) return Results.Unauthorized();
        if (!AdminAccess.IsBuilderOrAbove(account))
            return Results.Json(new { message = "Builders only." }, statusCode: 403);
        if (type is not null && !ListTypes.Contains(type))
            return Results.BadRequest(new { message = $"Unknown type '{type}'." });

        int take = Math.Clamp(limit ?? 50, 1, 200);
        int skip = Math.Max(offset ?? 0, 0);
        var all = ObjectRegistry.FilterBy(o =>
            (type is null || type.Equals("all", StringComparison.OrdinalIgnoreCase)
                || AdminAccess.ObjectType(o).Equals(type, StringComparison.OrdinalIgnoreCase))
            && MatchesQuery(o, q));
        all.Sort((a, b) => a.Id.CompareTo(b.Id));
        var items = all.Skip(skip).Take(take).Select(AdminAccess.ToListItem).ToList();
        return Results.Json(new { total = all.Count, items });
    }

    private static IResult GetAsync(HttpContext ctx, int id)
    {
        var account = AdminAccess.GetAccount(ctx);
        if (account is null) return Results.Unauthorized();
        if (!AdminAccess.IsBuilderOrAbove(account))
            return Results.Json(new { message = "Builders only." }, statusCode: 403);
        var target = ObjectRegistry.GetSingle(id);
        if (target is null) return Results.NotFound(new { message = $"No object with ID {id}." });
        return Results.Json(AdminAccess.ToDetail(account, target));
    }

    private static async Task<IResult> PatchAsync(HttpContext ctx, int id)
    {
        var account = AdminAccess.GetAccount(ctx);
        if (account is null) return Results.Unauthorized();
        if (!AdminAccess.IsBuilderOrAbove(account))
            return Results.Json(new { message = "Builders only." }, statusCode: 403);
        var target = ObjectRegistry.GetSingle(id);
        if (target is null) return Results.NotFound(new { message = $"No object with ID {id}." });

        JsonElement body;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        }
        catch { return Results.BadRequest(new { message = "Invalid request body." }); }
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("attr", out var attrElement)
            || attrElement.ValueKind != JsonValueKind.String)
            return Results.BadRequest(new { message = "Body must be {attr, value}." });
        string attr = attrElement.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(attr))
            return Results.BadRequest(new { message = "Body must be {attr, value}." });
        if (!body.TryGetProperty("value", out var valueElement))
            return Results.BadRequest(new { message = "Body must be {attr, value}." });

        object? value = AdminAccess.FromJson(valueElement);
        if (!AdminAccess.TryApplySet(account, target, attr, value, out var error, out bool isNew))
        {
            if (error == "You cannot modify an object of equal or higher privilege.")
                return Results.Json(new { message = error }, statusCode: 403);
            return Results.Json(new { message = error ?? $"Could not set '{attr}'." }, statusCode: 422);
        }
        var detail = AdminAccess.ToDetail(account, target);
        var row = detail.Props.FirstOrDefault(p => p.Name == attr);
        if (isNew)
            return Results.Json(new { ok = true, warning = $"'{attr}' is a new attribute on {target.Name}.", prop = row });
        return Results.Json(new { ok = true, prop = row });
    }

    private static IResult DeleteExtraAsync(HttpContext ctx, int id, string key)
    {
        var account = AdminAccess.GetAccount(ctx);
        if (account is null) return Results.Unauthorized();
        if (!AdminAccess.IsBuilderOrAbove(account))
            return Results.Json(new { message = "Builders only." }, statusCode: 403);
        var target = ObjectRegistry.GetSingle(id);
        if (target is null) return Results.NotFound(new { message = $"No object with ID {id}." });
        if (!AdminAccess.TryRemoveExtra(account, target, key, out var error))
        {
            if (error == "You cannot modify an object of equal or higher privilege.")
                return Results.Json(new { message = error }, statusCode: 403);
            return Results.Json(new { message = error ?? $"Could not remove '{key}'." }, statusCode: 422);
        }
        return Results.Json(new { ok = true });
    }
}
