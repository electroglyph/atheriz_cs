using Atheriz.Core.Network;

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
                if (inst is null)
                {
                    AtherizLogger.LogError($"Failed to register protocol {protoPath}: type not found");
                    continue;
                }
                inst.Setup(app);
                AtherizLogger.LogInformation($"Registered network protocol: {inst.GetType().Name}");
            }
            catch (Exception ex)
            {
                AtherizLogger.LogError($"Failed to register protocol {protoPath}: {ex.Message}");
                AtherizLogger.LogError(ex.ToString());
            }
        }
    }
}
