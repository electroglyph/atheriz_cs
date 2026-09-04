namespace Atheriz.Core.Network;

// Port of atheriz/network/protocol.py:4-17
// Original BaseProtocol.setup(cls, app: FastAPI) is a classmethod that registers
// endpoints/lifespan tasks with the FastAPI app. In ASP.NET Core the equivalent
// is WebApplication, so we mirror as Setup(object app) where app is expected to be
// WebApplication/IHost. Using object avoids requiring Microsoft.AspNetCore.App
// FrameworkReference in Core (tests run without aspnet runtime).

/// <summary>
/// Interface for protocol lifecycle hooks. Mirrors <c>atheriz/network/protocol.py:BaseProtocol</c>.
/// </summary>
public abstract class BaseProtocol
{
    /// <summary>
    /// Required hook called during application startup.
    /// Should register any necessary endpoints, lifespan tasks, or background workers
    /// with the <paramref name="app"/> object.
    /// Mirrors <c>BaseProtocol.setup(app: FastAPI)</c> at protocol.py:10-17.
    /// In C# <paramref name="app"/> is expected to be a <c>WebApplication</c>.
    /// </summary>
    public abstract void Setup(object app);
}

/// <summary>
/// Alias for <see cref="BaseProtocol"/> matching task spec naming (<c>Protocol</c>).
/// </summary>
public abstract class Protocol : BaseProtocol
{
}
