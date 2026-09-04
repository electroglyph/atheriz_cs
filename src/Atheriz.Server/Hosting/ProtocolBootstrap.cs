using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Server.Hosting;

public static class ProtocolBootstrap
{
    // F001: explicit allowlist — config names are enable-flags only, never assembly
    // scans. Unknown names keep the legacy "Failed to register protocol" message.
    private static BaseProtocol? CreateKnown(string protoPath) => protoPath switch
    {
        "Atheriz.Core.Network.WebSocketProtocol" => new WebSocketProtocol(),
        "Atheriz.Core.Network.TelnetProtocol" => new TelnetProtocol(),
        _ => null,
    };

    public static void RegisterProtocols(WebApplication app, AtherizSettings settings)
    {
        foreach (var protoPath in settings.NetworkProtocols ?? Array.Empty<string>())
        {
            try
            {
                BaseProtocol? inst = CreateKnown(protoPath);
                if (inst == null)
                {
                    Console.WriteLine($"Failed to register protocol {protoPath}: type not found");
                    continue;
                }
                inst.Setup(app);
                Console.WriteLine($"Registered network protocol: {inst.GetType().Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register protocol {protoPath}: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
            }
        }
    }
}
