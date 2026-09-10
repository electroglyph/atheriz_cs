using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// AtPostPuppet session commands: the snapshot+guard+send core delivers each
// command with byte-identical wire strings — the bare logged_in call and the
// payload-carrying calls arrive at SendCommand with the same normalization.
[Collection("Ported")]
public class SessionCommandWireTests
{
    [Fact]
    public void AtPostPuppet_SendsLoggedIn_And_PlayerCommands()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var conn = new TestConnection("wire");
            var session = new Session(conn);
            var obj = GameObject.Create("pc");
            ObjectRegistry.AddObject(obj);
            obj.Session = session;

            obj.AtPostPuppet();

            var loggedIn = conn.Sent.Where(s => s.Cmd == "logged_in").ToList();
            Assert.Single(loggedIn);
            Assert.Empty(loggedIn[0].Args);
            var playerCommands = conn.Sent.Where(s => s.Cmd == "player_commands").ToList();
            Assert.Single(playerCommands);
            Assert.Single(playerCommands[0].Args);
            // No location: the map block is skipped, so no map_enable goes out.
            Assert.DoesNotContain(conn.Sent, s => s.Cmd == "map_enable");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
