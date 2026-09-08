using System.Security.Cryptography.X509Certificates;

namespace Atheriz.Core.Utils;

/// <summary>
/// Single shared TLS certificate loader for the Kestrel (HTTPS/WSS) and telnet listeners.
/// Supports split cert+key files as well as a combined PEM (certificate with embedded
/// private key). Pure: throws on failure, callers decide downgrade vs fail-fast and
/// keep their own operator-facing messages.
/// </summary>
public static class TlsCertLoader
{
    public static X509Certificate2 Load(string certFile, string? keyFile)
    {
        if (!string.IsNullOrEmpty(keyFile))
        {
            if (!File.Exists(keyFile))
                throw new FileNotFoundException($"SSL key file not found: {keyFile}");
            return X509Certificate2.CreateFromPemFile(certFile, keyFile);
        }

        var pemText = File.ReadAllText(certFile);
        // Combined PEM: certificate with embedded private key — split and load both parts.
        if (pemText.Contains("PRIVATE KEY"))
        {
            var certBlocks = SplitPemBlocks(pemText, "CERTIFICATE");
            var keyBlocks = SplitPemBlocks(pemText, "PRIVATE KEY");
            if (certBlocks.Count > 0 && keyBlocks.Count > 0)
            {
                var leafPem = certBlocks[0];
                var keyPem = keyBlocks[0];
                var leaf = X509Certificate2.CreateFromPem(leafPem, keyPem);
                // Preserve the chain: bundle leaf + intermediates into an in-memory
                // PFX so the handshake can present more than the leaf. Falls back
                // to the leaf alone if any intermediate is unparseable.
                if (certBlocks.Count > 1)
                {
                    try
                    {
                        var bundle = new X509Certificate2Collection { leaf };
                        for (int i = 1; i < certBlocks.Count; i++)
                            bundle.Add(X509Certificate2.CreateFromPem(certBlocks[i]));
                        var pfx = bundle.Export(X509ContentType.Pfx);
                        if (pfx == null) return leaf;
                        return new X509Certificate2(pfx, (string?)null,
                            X509KeyStorageFlags.EphemeralKeySet);
                    }
                    catch { return leaf; }
                }
                return leaf;
            }
        }
        // Keyless public-only file: fail HERE, not at handshake time
        // (Kestrel would otherwise die on first connection).
        try
        {
            return RequireKey(X509Certificate2.CreateFromPem(pemText), certFile);
        }
        catch (System.Security.Cryptography.CryptographicException) { throw; }
        catch
        {
            try { return RequireKey(X509Certificate2.CreateFromPemFile(certFile), certFile); }
            catch (System.Security.Cryptography.CryptographicException) { throw; }
            catch { return RequireKey(new X509Certificate2(certFile), certFile); }
        }
    }

    private static X509Certificate2 RequireKey(X509Certificate2 cert, string certFile)
    {
        if (!cert.HasPrivateKey)
        {
            cert.Dispose();
            throw new System.Security.Cryptography.CryptographicException(
                $"SSL certificate has no private key: {certFile}");
        }
        return cert;
    }

    /// <summary>
    /// Splits PEM text into blocks whose BEGIN header line contains
    /// <paramref name="fragment"/> (e.g. "CERTIFICATE", or "PRIVATE KEY" which
    /// also matches "RSA PRIVATE KEY"). End marker mirrors the BEGIN suffix.
    /// </summary>
    private static List<string> SplitPemBlocks(string text, string fragment)
    {
        var blocks = new List<string>();
        int idx = 0;
        while (true)
        {
            int begin = text.IndexOf("-----BEGIN", idx, StringComparison.Ordinal);
            if (begin < 0) break;
            int headerEnd = text.IndexOf('\n', begin);
            if (headerEnd < 0) headerEnd = text.Length;
            string header = text.Substring(begin, headerEnd - begin);
            if (!header.Contains(fragment, StringComparison.Ordinal)) { idx = headerEnd; continue; }
            string endMarker = "-----END" + header.Substring("-----BEGIN".Length);
            int end = text.IndexOf(endMarker, headerEnd, StringComparison.Ordinal);
            if (end < 0) break;
            end += endMarker.Length;
            blocks.Add(text.Substring(begin, end - begin));
            idx = end;
        }
        return blocks;
    }
}
