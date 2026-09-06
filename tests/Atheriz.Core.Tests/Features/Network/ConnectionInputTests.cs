using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Network;

// Connection input handling for malformed commands.
[Collection("Ported")]
public class ConnectionInputTests
{
    // --- Numeric command handling ---

    [Fact]
    public void HandleCommand_NumericCommand_IsRejectedAsMalformed()
    {
        // Non-string commands must take the "Invalid message format" path:
        // `cmdElement.GetString() ?? ToString()` cannot accept numbers/arrays
        // because GetString() throws, so numeric input is rejected as malformed.
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            var c = new TestConn("c1", "9.9.9.9");
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                mgr.HandleCommand(c, "[123]");
                Thread.Sleep(50);
                log = cap.Read();
            }
            Assert.Contains("Invalid message format", log);
            Assert.DoesNotContain("Error handling message", log);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }
}
