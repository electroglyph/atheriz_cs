using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins for the scoped command-entry gaps: the MoveOptions record, the
// Run(CommandContext) primary entry with its untyped adapter, and the async
// entry surface on the creation wizard verbs.
[Collection("Ported")]
public sealed class ScopedGapsPinsTests
{
    // Test-local strict command proving the adapter split: parsed input must
    // reach RunParsed, raw-string input must reach RunRaw.
    private sealed class StrictCommand : Command
    {
        public override string Key => "strict";
        public bool SawParsed;
        public bool SawRaw;
        public override void RunParsed(CommandContext ctx) => SawParsed = true;
        public override void RunRaw(CommandContext ctx) => SawRaw = true;
    }

    private sealed class FakeCaller : IMessageTarget
    {
        public readonly List<string> Msgs = [];
        public void Msg(string text) => Msgs.Add(text);
    }

    [Fact]
    public void CommandAdapter_ParsedArgs_ReachesRunParsed()
    {
        var cmd = new StrictCommand();
        cmd.Run(new FakeCaller(), new GameArgumentParser.ParsedArgs());
        Assert.True(cmd.SawParsed);
        Assert.False(cmd.SawRaw);
    }

    [Fact]
    public void CommandAdapter_RawString_ReachesRunRaw()
    {
        var cmd = new StrictCommand();
        cmd.Run(new FakeCaller(), "raw text");
        Assert.True(cmd.SawRaw);
        Assert.False(cmd.SawParsed);
    }

    [Fact]
    public void MoveOptions_CarriesParsedCoord()
    {
        // The options record carries the parse result into the move itself:
        // RunMove resolves and moves without re-parsing text.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var startCoord = new Coord("limbo", 0, 0, 0);
            var destCoord = new Coord("limbo", 2, 1, 0);
            var startNode = new Node(startCoord);
            nh.AddNode(startNode);
            nh.AddNode(new Node(destCoord));
            var builder = GameObject.Create("gap1mover", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(startNode));
            builder.ClearMessages();

            var opts = new MoveOptions(destCoord);
            Assert.Equal(destCoord, opts.Dest);
            new MoveCommand().RunMove(builder, opts, CancellationToken.None);

            var loc = builder.ResolveLocationObject() as Node;
            Assert.NotNull(loc);
            Assert.Equal(destCoord, loc!.Coord);
            Assert.Contains($"Moved to {destCoord}.", string.Join("\n", builder.PeekMessages()));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ObjectRegistry.ClearAll();
        }
    }

    [Fact]
    public async Task GuestRunAsync_CancelledToken_ReturnsPromptly()
    {
        // An already-cancelled token ends the guest wizard quietly: the first
        // prompt resolves to "" like a cancelled prompt, name validation
        // refuses it, and the wizard returns without throwing or hanging.
        using var env = GlobalTestEnv.Enter();
        var g = AtherizSettings.Global;
        bool og = g.GuestEnabled;
        g.GuestEnabled = true;
        CommandDispatcher.SetSettings(new AtherizSettings());
        try
        {
            var conn = new TestConnection();
            var cmd = new GuestCommand();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ctx = new CommandContext(conn, null, null, "", cts.Token);
            await cmd.RunAsync(ctx, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
            var sent = string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
            Assert.Contains("Name cannot be empty.", sent);
            Assert.Null(conn.Session.Puppet);
        }
        finally
        {
            g.GuestEnabled = og;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
    }
}
