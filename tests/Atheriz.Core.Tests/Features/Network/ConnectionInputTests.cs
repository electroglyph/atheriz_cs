using System.Reflection;
using System.Text;
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

    [Fact]
    public void HandleCommand_OversizeMultibyte_LogsByteCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager(new AtherizSettings { WebsocketMaxMessageSize = 16 });
        try
        {
            var c = new TestConn("c-oversize", "10.30.0.1");
            // 14 chars on the wire but 24 bytes of UTF-8: over a 16-byte
            // budget without ever exceeding 16 chars.
            string raw = "[\"éééééééééé\"]";
            int bytes = Encoding.UTF8.GetByteCount(raw);
            Assert.True(raw.Length <= 16 && bytes > 16);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                mgr.HandleCommand(c, raw);
                Assert.True(PortedHelpers.WaitFor(() => cap.Read().Contains("Message too large", StringComparison.Ordinal), 5000));
                log = cap.Read();
            }
            Assert.Contains($"({bytes} bytes > 16 bytes)", log, StringComparison.Ordinal);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }

    [Fact]
    public void HandleCommand_MalformedMultibyte_LogsByteCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            var c = new TestConn("c-malformed", "10.30.0.2");
            // Valid JSON object, not an array: takes the malformed path.
            // 6 chars, 7 bytes of UTF-8.
            string raw = "{\"é\":1}";
            int bytes = Encoding.UTF8.GetByteCount(raw);
            Assert.NotEqual(raw.Length, bytes);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                mgr.HandleCommand(c, raw);
                Assert.True(PortedHelpers.WaitFor(() => cap.Read().Contains("Invalid message format", StringComparison.Ordinal), 5000));
                log = cap.Read();
            }
            Assert.Contains($"({bytes} bytes)", log, StringComparison.Ordinal);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }
}
