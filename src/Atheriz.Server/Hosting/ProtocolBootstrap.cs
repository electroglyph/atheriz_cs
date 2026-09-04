using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Server.Hosting;

public static class ProtocolBootstrap
{
    public static void RegisterProtocols(WebApplication app, AtherizSettings settings)
    {
        foreach (var protoPath in settings.NetworkProtocols ?? Array.Empty<string>())
        {
            try
            {
                Type? t = Type.GetType(protoPath);
                if (t == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = asm.GetType(protoPath);
                        if (t != null) break;
                    }
                }
                if (t == null)
                {
                    try { t = typeof(BaseProtocol).Assembly.GetType(protoPath); } catch { }
                }
                if (t == null)
                {
                    Console.WriteLine($"Failed to register protocol {protoPath}: type not found");
                    continue;
                }
                if (!typeof(BaseProtocol).IsAssignableFrom(t))
                {
                    Console.WriteLine($"Failed to register protocol {protoPath}: not a BaseProtocol");
                    continue;
                }
                var inst = (BaseProtocol?)Activator.CreateInstance(t);
                if (inst == null) { Console.WriteLine($"Failed to register protocol {protoPath}: Activator returned null"); continue; }
                inst.Setup(app);
                Console.WriteLine($"Registered network protocol: {t.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register protocol {protoPath}: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
            }
        }
    }
}
