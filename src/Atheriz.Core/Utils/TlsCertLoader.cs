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
            var certStart = pemText.IndexOf("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal);
            var certEndIdx = pemText.IndexOf("-----END CERTIFICATE-----", StringComparison.Ordinal);
            if (certStart >= 0 && certEndIdx >= 0)
            {
                certEndIdx += "-----END CERTIFICATE-----".Length;
                var certPemPart = pemText.Substring(certStart, certEndIdx - certStart);
                var keyStart = pemText.IndexOf("-----BEGIN", certEndIdx, StringComparison.Ordinal);
                if (keyStart >= 0)
                {
                    var keyPemPart = pemText.Substring(keyStart);
                    var keyEnd = keyPemPart.IndexOf("-----END", StringComparison.Ordinal);
                    if (keyEnd >= 0)
                    {
                        var endMarkerEnd = keyPemPart.IndexOf("-----", keyEnd + 5, StringComparison.Ordinal);
                        if (endMarkerEnd >= 0) keyPemPart = keyPemPart.Substring(0, endMarkerEnd + 5);
                    }
                    return X509Certificate2.CreateFromPem(certPemPart, keyPemPart);
                }
            }
        }
        try
        {
            return X509Certificate2.CreateFromPem(pemText);
        }
        catch
        {
            try { return X509Certificate2.CreateFromPemFile(certFile); }
            catch { return new X509Certificate2(certFile); }
        }
    }
}
