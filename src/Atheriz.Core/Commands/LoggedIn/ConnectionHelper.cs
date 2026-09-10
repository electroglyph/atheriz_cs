// Shared quiet-close behind the quit/shutdown/reload verbs: one switch on
// ISessionProvider covering the GameObject/Session/BaseConnection caller
// shapes. "Goodbye!" delivery stays at the call site (message before close);
// each branch closes exactly what the old inline code closed and swallows
// every error the same way.
using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.LoggedIn;

internal static class ConnectionHelper
{
    internal static void CloseQuietly(IMessageTarget caller)
    {
        switch (caller)
        {
            case GameObject go:
                try { go.Session?.Connection?.Close(); } catch (Exception) { }
                break;
            case Session sess:
                try { sess.Connection?.Close(); } catch (Exception) { }
                break;
            // close raw connections like the unlogged-in twin does.
            case BaseConnection bc:
                try { bc.Close(); } catch (Exception) { }
                break;
            default:
                break;
        }
    }
}
