using System.Net;
using System.Security.Cryptography.X509Certificates;
using Atheriz.Core.Settings;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Hosting;

public static class KestrelConfig
{
    public static void ConfigureKestrel(KestrelServerOptions opts, IConfiguration config)
    {
        var s = config.GetSection("Atheriz").Get<AtherizSettings>() ?? AtherizSettings.Default;
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
                    X509Certificate2 cert;
                    if (!string.IsNullOrEmpty(keyFile) && File.Exists(keyFile))
                    {
                        cert = X509Certificate2.CreateFromPemFile(certFile, keyFile);
                    }
                    else
                    {
                        try { cert = X509Certificate2.CreateFromPemFile(certFile); }
                        catch { cert = new X509Certificate2(certFile); }
                    }
                    listen.UseHttps(cert);
                    Console.WriteLine($"SSL is enabled (cert: {certFile})");
                }
                catch (Exception ex)
                {
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
