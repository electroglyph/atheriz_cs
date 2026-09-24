// Regression pins: account loading, give/get, argument parsing, menus, set, limiter, deletion.
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class CorrectnessBatchBTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (NodeHandler Nh, Node Node) EnterRoom(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        return (nh, node);
    }

    // One corrupt `characters` entry must not abort the whole account load.
    [Fact]
    public void AccountFromDto_CorruptCharactersEntry_SkipsEntry()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("pin-corrupt", "pw-corrupt-xyz");
        var ch = GameObject.Create("pin-corrupt-char");
        ObjectRegistry.AddObject(ch);
        acc.AddCharacter(ch);
        var dto = acc.ToDto();
        dto.Extra["characters"] = JsonSerializer.SerializeToElement(new object?[] { ch.Id, "two", null, true });
        var loaded = Account.FromDto(dto);
        Assert.Equal([ch.Id], loaded.Characters);
        Assert.True(loaded.CheckPassword("pw-corrupt-xyz"));
    }

    // `give ALL` must bulk like `give all` (same messages, whole inventory).
    [Fact]
    public void Give_AllUppercase_BulksEntireInventory()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("pin-give");
        try
        {
            var giver = GameObject.Create("giver", isPc: true);
            ObjectRegistry.AddObject(giver);
            Assert.True(giver.MoveTo(node));
            var taker = GameObject.Create("taker", isNpc: true);
            ObjectRegistry.AddObject(taker);
            Assert.True(taker.MoveTo(node));
            var one = GameObject.Create("one", isItem: true);
            ObjectRegistry.AddObject(one);
            Assert.True(one.MoveTo(giver));
            var two = GameObject.Create("two", isItem: true);
            ObjectRegistry.AddObject(two);
            Assert.True(two.MoveTo(giver));
            giver.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(giver, "give ALL to taker", immediate: true));

            Assert.Contains(one.Id, taker.ContentsSnapshot);
            Assert.Contains(two.Id, taker.ContentsSnapshot);
            Assert.DoesNotContain(one.Id, giver.ContentsSnapshot);
            Assert.DoesNotContain(two.Id, giver.ContentsSnapshot);
            var text = string.Join("\n", giver.PeekMessages());
            Assert.Contains("You give one to taker.", text);
            Assert.Contains("You give two to taker.", text);
            Assert.DoesNotContain("You don't have that.", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // `get ALL` must bulk like `get all` (no spurious self-grab failures).
    [Fact]
    public void Get_AllUppercase_PicksUpWithoutSpuriousFailures()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("pin-get");
        try
        {
            var holder = GameObject.Create("holder", isPc: true);
            ObjectRegistry.AddObject(holder);
            Assert.True(holder.MoveTo(node));
            var sword = GameObject.Create("sword", isItem: true);
            ObjectRegistry.AddObject(sword);
            Assert.True(sword.MoveTo(node));
            var shield = GameObject.Create("shield", isItem: true);
            ObjectRegistry.AddObject(shield);
            Assert.True(shield.MoveTo(node));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "get ALL", immediate: true));

            Assert.Contains(sword.Id, holder.ContentsSnapshot);
            Assert.Contains(shield.Id, holder.ContentsSnapshot);
            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("You picked up: sword", text);
            Assert.Contains("You picked up: shield", text);
            Assert.DoesNotContain("You can't get", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Append with Type converts each element like the single-store path.
    [Fact]
    public void Append_WithType_ConvertsEachElement()
    {
        var p = new GameArgumentParser("prog");
        p.AddArgument("--n").Action(GameArgumentParser.ArgAction.Append).Type<int>();
        var pa = p.ParseArgs(["--n", "5", "--n", "6"]);
        var vals = pa.GetObjList("n");
        Assert.Equal(2, vals.Count);
        Assert.All(vals, v => Assert.IsType<int>(v));
        Assert.Equal([5, 6], vals.Cast<int>().ToList());
    }

    [Fact]
    public void AppendList_WithType_ConvertsEachElement()
    {
        var p = new GameArgumentParser("prog");
        p.AddArgument("--n").Nargs("*").Action(GameArgumentParser.ArgAction.Append).Type<int>();
        var pa = p.ParseArgs(["--n", "5", "6"]);
        var vals = pa.GetObjList("n");
        Assert.Equal([5, 6], vals.Cast<int>().ToList());
    }

    [Fact]
    public void Append_WithType_InvalidValue_ThrowsLikeSingleStore()
    {
        var p = new GameArgumentParser("prog");
        p.AddArgument("--n").Action(GameArgumentParser.ArgAction.Append).Type<int>();
        var ex = Assert.Throws<CommandError>(() => p.ParseArgs(["--n", "abc"]));
        Assert.Contains("invalid int value", ex.Message);
    }

    [Fact]
    public void Append_WithoutType_KeepsStringsVisibleViaGetList()
    {
        var p = new GameArgumentParser("prog");
        p.AddArgument("--s").Action(GameArgumentParser.ArgAction.Append);
        var pa = p.ParseArgs(["--s", "a", "--s", "b"]);
        Assert.Equal(["a", "b"], pa.GetList("s"));
        Assert.Equal(["a", "b"], pa.GetObjList("s").Cast<string>().ToList());
    }

    // Null input means "no such key" (stay) — no NullReferenceException.
    [Fact]
    public async Task MenuEngine_HandleInput_Null_StaysWithoutThrow()
    {
        var engine = new MenuEngine(null, ctx => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("a", "A", stay: true)])));
        await engine.RenderAsync();
        var ex = await Record.ExceptionAsync(() => engine.HandleInputAsync(null));
        Assert.Null(ex);
        Assert.True(await engine.HandleInputAsync(null));
        Assert.True(engine.HasNode);
        Assert.Equal("text", engine.CurrentText);
    }

    [Fact]
    public async Task MenuEngine_HandleInputAsync_Null_StaysWithoutThrow()
    {
        var engine = new MenuEngine(null, ctx => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("a", "A", stay: true)])));
        await engine.RenderAsync();
        Assert.True(await engine.HandleInputAsync(null));
        Assert.True(engine.HasNode);
    }

    // Multi-word values join back with spaces.
    [Fact]
    public void Set_MultiWordValue_JoinsTokens()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = GameObject.Create("pin-setter", privilege: Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        builder.ClearMessages();

        RunJob(CommandDispatcher.DispatchLoggedIn(builder, "set me desc hello world", immediate: true));

        Assert.Equal("hello world", builder.Desc);
        Assert.Contains("hello world", string.Join("\n", builder.PeekMessages()));
    }

    [Fact]
    public void Set_MissingValue_StillReportsRequired()
    {
        var ex = Assert.Throws<CommandError>(() => new SetCommand().Parser!.ParseArgs(["me", "desc"]));
        Assert.Contains("the following arguments are required: value", ex.Message);
    }

    // An over-release must not wipe unrelated legitimate debt.
    [Fact]
    public void ReleaseSync_OverRelease_PreservesUnrelatedDebt()
    {
        var lim = new PendingLimiter(maxBytes: 10000);
        Assert.True(lim.TryReserve(200));
        lim.ReleaseSync(5000);
        Assert.Equal((200, 1, 0), lim.Snapshot());
        // The surviving debt still releases exactly.
        lim.ReleaseSync(200);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    // A game-deleted object must stay dead across save + load
    // (the journaled row dies with the checkpoint).
    [Fact]
    public void Delete_SaveLoad_DoesNotResurrect()
    {
        using var env = GlobalTestEnv.Enter();
        var item = GameObject.Create("pin-victim");
        ObjectRegistry.AddObject(item);
        var id = item.Id;
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force: true); }
        using (var db = new AtherizDbContext(env.TempPath)) { Assert.NotNull(db.Objects.Find(id)); }
        Assert.NotNull(item.Delete(null, false));
        using (var db = new AtherizDbContext(env.TempPath)) { ObjectRegistry.SaveObjects(db, force: true); }
        using (var db = new AtherizDbContext(env.TempPath)) { Assert.Null(db.Objects.Find(id)); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        Assert.Null(ObjectRegistry.GetSingle(id));
    }

    // Same via DeleteCommand (its discarded ops are covered by the journal).
    [Fact]
    public void DeleteCommand_SaveLoad_DoesNotResurrect()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("pin-destroyer", privilege: Privilege.Builder);
        ObjectRegistry.AddObject(caller);
        var coord = new Coord("pin-del", 0, 0, 0);
        var room = new Node(coord);
        ObjectRegistry.AddObject(room);
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room.Coord);
        room.AddObject(caller);
        var item = GameObject.Create("pin-doomed");
        ObjectRegistry.AddObject(item);
        item.MoveTo(room);
        var id = item.Id;
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force: true); }
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(["pin-doomed"]));
        Assert.True(item.IsDeleted);
        using (var db = new AtherizDbContext(env.TempPath)) { ObjectRegistry.SaveObjects(db, force: true); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        Assert.Null(ObjectRegistry.GetSingle(id));
    }

    // Channel deletes journal their rows too.
    [Fact]
    public void ChannelDelete_Save_RemovesRow()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("pin-chan");
        var id = ch.Id;
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force: true); }
        using (var db = new AtherizDbContext(env.TempPath)) { Assert.NotNull(db.Objects.Find(id)); }
        Assert.NotNull(ch.Delete(null, false));
        using (var db = new AtherizDbContext(env.TempPath)) { ObjectRegistry.SaveObjects(db, force: true); }
        using (var db = new AtherizDbContext(env.TempPath)) { Assert.Null(db.Objects.Find(id)); }
    }
}
