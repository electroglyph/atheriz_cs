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
        var host = s.WebserverInterface ?? "0.0.0.0";
        var port = s.WebserverPort;

        IPAddress ip;
        if (host == "::") ip = IPAddress.IPv6Any;
        else if (!IPAddress.TryParse(host, out ip!))
        {
            ip = IPAddress.Any;
        }

        opts.Listen(ip, port, listen =>
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
                Console.WriteLine($"WARNING: SSL cert file not found: {certFile}");
                Console.WriteLine("SSL is disabled (set SSL_CERTFILE to enable)");
            }
        });
    }
}
