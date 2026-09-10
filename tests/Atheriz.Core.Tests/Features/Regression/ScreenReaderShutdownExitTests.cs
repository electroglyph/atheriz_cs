using System.Text.Json;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Regression;

// Pins for screen-reader/shutdown/exit/coord behavior.
[Collection("Ported")]
public class ScreenReaderShutdownExitTests
{
    // Toggling screenreader confirms on the message path: telnet callers
    // with no control-channel display otherwise see nothing change.
    [Fact]
    public void ScreenReader_Toggle_ConfirmsOnMessagePath()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("screenreader");
            o.Session = new Session { ScreenReader = false, TermWidth = 80 };
            new ScreenReaderCommand().Run(o, null);
            Assert.True(o.Session.ScreenReader);
            Assert.Contains("Screenreader mode on.", string.Join("\n", o.PeekMessages()));
            o.ClearMessages();
            new ScreenReaderCommand().Run(o, null);
            Assert.False(o.Session.ScreenReader);
            Assert.Contains("Screenreader mode off.", string.Join("\n", o.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // Shutdown reuses one client on a pool thread: no per-call socket pool,
    // no raw OS thread per invocation.
    [Fact]
    public void Shutdown_ReusesSharedClient_PoolThread()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "ShutdownCommand.cs");
        Assert.Contains("SharedShutdownClient", src);
        Assert.DoesNotContain("new HttpClient()", src);
        Assert.DoesNotContain("new Thread(", src);
    }

    // The door re-close after a refused/failed exit move lives in one helper
    // used by both paths, not two inline copies.
    [Fact]
    public void Exit_DoorRestore_LivesInOneHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "ExitCommand.cs");
        Assert.Contains("internal static void RestoreClosedDoor(", src);
        Assert.Equal(2, SourceScan.Count(src, "RestoreClosedDoor(door, c);"));
        Assert.Equal(1, SourceScan.Count(src, "door.TryClose(c); } catch { closedOk = false; }"));
    }

    // The size gate counts bytes: a short multibyte message over the byte
    // budget is refused even though its char count fits.
    [Fact]
    public void HandleCommand_MultibyteOverBudget_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            bool called = false;
            var lk = new object();
            mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) => { lock (lk) called = true; }));
            var conn = new FakeConnection();
            var small = JsonSerializer.Serialize(new object[] { "text", new object[] { "hi" }, new Dictionary<string, object?>() });
            mgr.HandleCommand(conn, small);
            Assert.True(PortedHelpers.WaitFor(() => { lock (lk) return called; }, 5000));
            lock (lk) called = false;
            // Raw multibyte chars, not \u escapes: char count fits the
            // budget while the UTF-8 byte count exceeds it.
            var big = "[\"text\", [\"" + new string('あ', 22000) + "\"], {}]";
            Assert.True(big.Length <= 65536);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(big) > 65536);
            mgr.HandleCommand(conn, big);
            Thread.Sleep(500);
            lock (lk) Assert.False(called);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }

    // Externally-produced camelCase coord JSON parses under the shared
    // persistence options instead of falling back to limbo.
    [Fact]
    public void ExtractCoord_CamelCase_Parses()
    {
        var dto = new GameObjectDto { Id = 1 };
        using var doc = JsonDocument.Parse("{\"area\":\"coordarea\",\"x\":1,\"y\":2,\"z\":3}");
        dto.Extra["Coord"] = doc.RootElement.Clone();
        var coord = GameObjectDtoConverter.ExtractCoord(dto);
        Assert.Equal("coordarea", coord.Area);
        Assert.Equal((1, 2, 3), (coord.X, coord.Y, coord.Z));
    }
}
