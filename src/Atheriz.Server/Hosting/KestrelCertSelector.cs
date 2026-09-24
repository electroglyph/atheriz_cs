using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;

namespace Atheriz.Server.Hosting;

// Kestrel-facing front for the startup-loaded TLS cert: the cert is loaded
// once (fail-fast, fail-closed) in KestrelConfig and served for every
// handshake from here, so selection never touches the filesystem. Wired via
// HttpsConnectionAdapterOptions.ServerCertificateSelector (net10 Kestrel
// exposes the selector as a Func, not an interface).
public sealed class KestrelCertSelector
{
    private readonly X509Certificate2 _certificate;

    public KestrelCertSelector(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _certificate = certificate;
    }

    public X509Certificate2? Select(ConnectionContext? context, string? name)
    {
        _ = context;
        _ = name;
        return _certificate;
    }
}
