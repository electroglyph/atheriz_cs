using Atheriz.Core.Objects;
using System.Security.Cryptography;

namespace Atheriz.Server.Hosting;

// Opaque bearer tokens for the landing-page admin API. Tokens name an
// account (never a character): HTTP privilege is the max over the account
// and its characters, and the WS auto-login runs the normal character
// wizard after binding. Tokens never expire: they live until the user logs
// out (revoked) or the server restarts (in-memory only). Separate from the
// loopback AdminToken (CLI-only) and from the in-band password login
// (socket-only).
public sealed class GameTokenService
{
    private readonly Dictionary<string, int> _tokens = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public string Mint(int accountId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (_lock) _tokens[token] = accountId;
        return token;
    }

    public Account? Resolve(string token)
    {
        int accountId;
        lock (_lock)
        {
            if (!_tokens.TryGetValue(token, out accountId)) return null;
        }
        return ObjectRegistry.GetSingle(accountId) as Account;
    }

    public void Revoke(string token)
    {
        lock (_lock) _tokens.Remove(token);
    }

    public void RevokeAccount(int accountId)
    {
        lock (_lock)
        {
            foreach (var key in _tokens.Where(kv => kv.Value == accountId).Select(kv => kv.Key).ToList())
                _tokens.Remove(key);
        }
    }
}
