using System.Net;
using Atheriz.Core.Settings;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Hosting;

public static class KestrelConfig
{
    public static void ConfigureKestrel(KestrelServerOptions opts, IConfiguration config)
    {
        var s = config.GetSection("Atheriz").Get<AtherizSettings>() ?? AtherizSettings.Global;
        // Honored opt-out (owner decision 2026-09-08): no bind at all — not even
        // loopback — so HTTP, the webclient, WebSocket and the admin routes stay
        // dark. Telnet and game protocols run independently of Kestrel.
        if (!s.WebserverEnabled) return;
        var host = s.WebserverInterface ?? "0.0.0.0";
        var port = s.WebserverPort;

        // Global request guardrails (per-route caps like create_account's 64KB
        // still apply inside; these bound everything else Kestrel serves).
        opts.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
        opts.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        opts.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);

        // Fail fast on an unparseable interface: silently serving on loopback
        // (or Any) would expose the admin token on an unintended interface.
        IPAddress ip;
        // "::" binds dual-stack via ListenAnyIP, not via a bare
        // IPv6Any socket whose IPv4 behavior is OS-dependent. No special
        // case needed: IPAddress.TryParse("::") already succeeds.
        if (!IPAddress.TryParse(host, out ip!))
            throw new InvalidOperationException($"Unparseable WebserverInterface '{host}'; refusing to bind an unintended interface.");
        bool dualStackAny = host == "0.0.0.0" || host == "::";

        void ConfigureEndpoint(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen)
        {
            var certFile = s.SslCertFile;
            var keyFile = s.SslKeyFile;
            if (!string.IsNullOrEmpty(certFile) && File.Exists(certFile))
            {
                try
                {
                    var cert = Atheriz.Core.Utils.TlsCertLoader.Load(certFile, keyFile);
                    listen.UseHttps(cert);
                    Console.WriteLine($"SSL is enabled (cert: {certFile})");
                }
                catch (Exception ex)
                {
                    // Fail fast when the operator did not explicitly allow serving the
                    // admin token over plaintext after a cert failure.
                    if (!s.AllowInsecureTlsFallback)
                        throw new InvalidOperationException($"SSL cert configured but unloadable ({certFile}); refusing insecure fallback.", ex);
                    Console.WriteLine($"SSL load failed for {certFile}: {ex.Message}");
                }
            }
            else if (!string.IsNullOrEmpty(certFile))
            {
                // Fail closed like the unloadable-cert path above: a configured
                // cert that is not on disk must never silently serve plaintext.
                if (!s.AllowInsecureTlsFallback)
                    throw new InvalidOperationException($"SSL cert configured but not found ({certFile}); refusing insecure fallback.");
                Console.WriteLine($"WARNING: SSL cert file not found: {certFile}");
                Console.WriteLine("SSL is disabled (set SSL_CERTFILE to enable)");
            }
        }

        // Dual-stack: the 0.0.0.0 default binds IPv4 only via Listen(ip);
        // ListenAnyIP adds the IPv6Any dual-mode socket alongside it.
        if (dualStackAny) opts.ListenAnyIP(port, ConfigureEndpoint);
        else opts.Listen(ip, port, ConfigureEndpoint);
    }
}
