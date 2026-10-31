using System.Net.Http;
using System.Net.Security;

namespace Atheriz.Core.Commands.LoggedIn;

// Shared loopback HTTP mechanics for the process-level verbs (shutdown,
// restart): self-signed dev certs cannot chain-verify, so chain/name errors
// are tolerated ONLY on a loopback host — never blind-trust a non-loopback
// peer. The token is a bearer secret, but the peer is always localhost here.
internal static class AdminEndpointClient
{
    internal static HttpClient CreateLoopbackClient()
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
        {
            if (req.RequestUri is not Uri u) return false;
            bool loopback = u.Host == "localhost" || u.Host == "127.0.0.1" || u.Host == "::1";
            if (errors == SslPolicyErrors.None) return true;
            if (!loopback || cert is null) return false;
            const SslPolicyErrors tolerated =
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
            return (errors & ~tolerated) == 0;
        } };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    // One admin POST attempt over the given scheme. Null on any failure so
    // the caller can retry with the flipped scheme.
    internal static async Task<(bool Ok, int StatusCode, string Body)?> TryPostAsync(string scheme, int port, string token, string path)
    {
        try
        {
            using var client = CreateLoopbackClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{scheme}://localhost:{port}{path}");
            req.Headers.Add("X-Admin-Token", token);
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch { return null; }
    }
}
