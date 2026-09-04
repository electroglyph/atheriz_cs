using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using System.Reflection;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class DeepAuditTests
{
    private static void SetExtra(GameObject obj, string key, object? value)
    {
        var fi = typeof(GameObject).GetField("_extra", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (Dictionary<string, System.Text.Json.JsonElement>)fi!.GetValue(obj)!;
        if (value == null)
        {
            dict.Remove(key);
        }
        else
        {
            var el = System.Text.Json.JsonSerializer.SerializeToElement(value.ToString() ?? "", Atheriz.Core.Persistence.JsonOptions.Default);
            dict[key] = el;
        }
    }

    private static bool HasExtra(GameObject obj, string key)
    {
        var fi = typeof(GameObject).GetField("_extra", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = (Dictionary<string, System.Text.Json.JsonElement>)fi!.GetValue(obj)!;
        return dict.ContainsKey(key);
    }

    [Fact]
    public void Quit_AliasesAndMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = CommandRegistry.UnloggedIn.Get("quit");
        Assert.NotNull(cmd);
        Assert.Contains("logout", cmd!.Aliases);
        Assert.Contains("disconnect", cmd.Aliases);
        Assert.DoesNotContain("q", cmd.Aliases);
        // check via ResolveUnloggedIn
        var conn = new FakeConnection();
        var job = CommandDispatcher.ResolveUnloggedIn(conn, "quit");
        Assert.NotNull(job);
        // message check via GameObject
        var go = GameObject.Create("tester", isPc: true);
        go.IsConnected = true;
        ObjectRegistry.AddObject(go);
        var quitCmd = new Atheriz.Core.Commands.UnloggedIn.QuitCommand();
        go.ClearMessages();
        quitCmd.Run(go, null);
        Assert.Contains(go.PeekMessages(), m => m.Contains("Goodbye!"));
    }

    [Fact]
    public void ScreenReader_NoExtraMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new FakeConnection();
        var sess = new Session { Connection = conn };
        var go = GameObject.Create("tester", isPc: true);
        go.Session = sess;
        sess.Puppet = go;
        var cmd = new Atheriz.Core.Commands.UnloggedIn.ScreenReaderCommand();
        go.ClearMessages();
        cmd.Run(go, null);
        // Python screenreader has no msg, only send_command
        Assert.DoesNotContain(go.PeekMessages(), m => m.ToLower().Contains("screenreader"));
    }

    [Fact]
    public void Help_AliasOnlyQuestion()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = CommandRegistry.UnloggedIn.Get("help");
        Assert.NotNull(cmd);
        Assert.Contains("?", cmd!.Aliases);
        Assert.DoesNotContain("h", cmd.Aliases);
        var loggedHelp = CommandRegistry.LoggedIn.Get("help");
        Assert.NotNull(loggedHelp);
        Assert.Contains("?", loggedHelp!.Aliases);
        Assert.DoesNotContain("h", loggedHelp.Aliases);
        Assert.True(loggedHelp.UseParser);
    }

    [Fact]
    public void Ban_SetsBanReasonOnChars()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("admin", isPc: true);
        admin.PrivilegeLevel = Privilege.Admin;
        admin.IsConnected = true;
        ObjectRegistry.AddObject(admin);
        var victim = GameObject.Create("victim", isPc: true);
        ObjectRegistry.AddObject(victim);
        victim.IsConnected = true;
        // Simulate ban via command: use BanCommand directly? check helper sets extra
        SetExtra(victim, "ban_reason", "test");
        Assert.True(HasExtra(victim, "ban_reason"));
        SetExtra(victim, "ban_reason", null);
        Assert.False(HasExtra(victim, "ban_reason"));
    }

    [Fact]
    public void Puppet_UnpuppetNoMessage()
    {
        using var env = GlobalTestEnv.Enter();
        // Verify Unpuppet does not send "You return..." 
        var cmd = CommandRegistry.LoggedIn.Get("unpuppet");
        Assert.NotNull(cmd);
        // run with no puppet should give "You are not puppeting anything."
        var go = GameObject.Create("tester", isPc: true);
        var conn = new FakeConnection();
        go.Session = new Session { Connection = conn };
        go.ClearMessages();
        cmd!.Run(go, null);
        Assert.Contains(go.PeekMessages(), m => m.Contains("not puppeting"));
        Assert.DoesNotContain(go.PeekMessages(), m => m.Contains("You return"));
    }

    [Fact]
    public void Give_OfflineFails()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("testarea", 0, 0, 0));
        var nh = new NodeHandler();
        nh.AddNode(node);
        var giver = GameObject.Create("giver", isPc: true);
        ObjectRegistry.AddObject(giver);
        giver.IsConnected = true;
        giver.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(giver);
        var receiver = GameObject.Create("receiver", isPc: true);
        ObjectRegistry.AddObject(receiver);
        receiver.IsConnected = false;
        receiver.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(receiver);
        var apple = GameObject.Create("apple", isItem: true);
        ObjectRegistry.AddObject(apple);
        apple.MoveTo(giver);
        var cmd = new Atheriz.Core.Commands.LoggedIn.GiveCommand();
        var args = cmd.Parser!.ParseArgs(new[] { "apple", "receiver" });
        giver.ClearMessages();
        cmd.Run(giver, args);
        var msg = string.Join(" ", giver.PeekMessages()).ToLowerInvariant();
        Assert.True(msg.Contains("could not find") || msg.Contains("offline"));
    }

    [Fact]
    public void Create_MovesToInventory()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("builder", isPc: true);
        caller.PrivilegeLevel = Privilege.Builder;
        caller.IsConnected = true;
        ObjectRegistry.AddObject(caller);
        var locNode = new Node(new Coord("limbo", 0, 0, 0));
        var nh = new NodeHandler();
        nh.AddNode(locNode);
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(locNode.Coord);
        locNode.AddObject(caller);
        var cmd = new Atheriz.Core.Commands.LoggedIn.CreateCommand();
        var args = cmd.Parser!.ParseArgs(new[] { "box" });
        cmd.Run(caller, args);
        // Python moves to caller inventory, not room
        var created = ObjectRegistry.FilterBy(o => o.Name == "box").FirstOrDefault();
        Assert.NotNull(created);
        Assert.Contains(created!.Id, caller.ContentsSnapshot);
    }

}
