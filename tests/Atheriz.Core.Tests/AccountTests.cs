using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests;

public class AccountTests
{
    private const string FixedSalt = "testsalt";

    [Fact]
    public void HashPassword_Deterministic_WithFixedSalt()
    {
        var h1 = Account.HashPassword("s3cret", FixedSalt);
        var h2 = Account.HashPassword("s3cret", FixedSalt);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length); // 32 bytes hex
        var h3 = Account.HashPassword("different", FixedSalt);
        Assert.NotEqual(h1, h3);
    }

    [Fact]
    public void CheckPassword_Roundtrip()
    {
        ObjectRegistry.ClearAll(); // Fix for Account.Create registry check (test_account.py:122)
        var acc = Account.Create("bob", "hunter2", FixedSalt);
        Assert.True(acc.CheckPassword("hunter2", FixedSalt));
        Assert.False(acc.CheckPassword("wrong", FixedSalt));
    }

    [Fact]
    public void Login_CaseInsensitiveName()
    {
        ObjectRegistry.ClearAll();
        var acc = Account.Create("Alice", "pw123456", FixedSalt);
        Assert.True(acc.Login("alice", "pw123456", FixedSalt));
        Assert.True(acc.LoggedIn);
        Assert.False(acc.Login("ALICE", "wrong", FixedSalt));
        Assert.False(acc.LoggedIn);
    }

    [Fact]
    public void Create_UniquenessCheck_Throws()
    {
        ObjectRegistry.ClearAll();
        GameObject.SetNextId(2000);
        var exists = (string n) => n.Equals("bob", StringComparison.OrdinalIgnoreCase);
        var acc1 = Account.Create("bob", "pw12345678", FixedSalt, existsCheck: _ => false);
        Assert.Equal("bob", acc1.Name);
        Assert.Throws<InvalidOperationException>(() => Account.Create("BOB", "otherpass", FixedSalt, exists));
    }

    [Fact]
    public void Create_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => Account.Create("", "pass", FixedSalt));
        Assert.Throws<ArgumentException>(() => Account.Create("name", "", FixedSalt));
    }

    [Fact]
    public void AddRemoveCharacter_Tracks()
    {
        ObjectRegistry.ClearAll();
        var acc = Account.Create("charlie", "pass12345", FixedSalt);
        var hero = GameObject.Create("Hero", isPc:true);
        var sidekick = GameObject.Create("Side", isPc:true);
        acc.AddCharacter(hero);
        acc.AddCharacter(sidekick);
        Assert.Contains(hero.Id, acc.Characters);
        Assert.Contains(sidekick.Id, acc.Characters);
        // duplicate no-op
        acc.AddCharacter(hero);
        Assert.Equal(2, acc.Characters.Count);
        acc.RemoveCharacter(hero);
        Assert.DoesNotContain(hero.Id, acc.Characters);
        Assert.Contains(sidekick.Id, acc.Characters);
    }

    [Fact]
    public void AtDisconnect_ClearsLoggedIn()
    {
        ObjectRegistry.ClearAll();
        var acc = Account.Create("dave", "pw123456", FixedSalt);
        acc.Login("dave", "pw123456", FixedSalt);
        Assert.True(acc.LoggedIn);
        acc.AtDisconnect();
        Assert.False(acc.LoggedIn);
    }

    [Fact]
    public void ToDto_FromDto_RoundTrip()
    {
        ObjectRegistry.ClearAll();
        GameObject.SetNextId(3000);
        var acc = Account.Create("eve", "secret123", FixedSalt);
        var hero = GameObject.Create("Hero");
        acc.AddCharacter(hero);
        acc.BanReason = "spam";
        var dto = acc.ToDto();
        Assert.Equal("account", dto.Type);
        Assert.True(dto.Extra.ContainsKey("password"));
        Assert.True(dto.Extra.ContainsKey("characters"));

        var json = GameObjectDtoSerializer.ToJson(dto);
        var dto2 = GameObjectDtoSerializer.FromJson(json);
        var acc2 = Account.FromDto(dto2);
        Assert.Equal("eve", acc2.Name);
        Assert.Equal("spam", acc2.BanReason);
        Assert.Contains(hero.Id, acc2.Characters);
        Assert.True(acc2.CheckPassword("secret123", FixedSalt));
    }

    [Fact]
    public void SaltProvider_FixedInjection()
    {
        ObjectRegistry.ClearAll();
        SaltProvider.SetSaltForTesting(FixedSalt);
        try
        {
            Assert.Equal(FixedSalt, SaltProvider.GetSalt());
            var acc = Account.Create("frank", "pwd12345"); // uses injected salt
            Assert.True(acc.CheckPassword("pwd12345"));
        }
        finally { SaltProvider.Clear(); }
    }
}

public class ChannelTests
{
    [Fact]
    public void Channel_HistoryLimit_EvictsOldest()
    {
        var ch = new Channel(historyLimit: 3);
        ch.Send("a"); ch.Send("b"); ch.Send("c"); ch.Send("d");
        Assert.Equal(3, ch.History.Count);
        Assert.DoesNotContain("a", ch.History);
        Assert.Contains("d", ch.History);
    }

    [Fact]
    public void Channel_Listeners_AddRemove()
    {
        var ch = new Channel();
        var o1 = GameObject.Create("o1");
        var o2 = GameObject.Create("o2");
        ch.AddListener(o1);
        ch.AddListener(o2);
        Assert.Contains(o1.Id, ch.Listeners);
        ch.RemoveListener(o1);
        Assert.DoesNotContain(o1.Id, ch.Listeners);
    }

    [Fact]
    public void Channel_Dto_RoundTrip()
    {
        var ch = new Channel(historyLimit: 2);
        ch.Name = "ooc";
        ch.Send("hello");
        ch.Send("world");
        var dto = ch.ToDto();
        Assert.Equal("channel", dto.Type);
        Assert.True(dto.Extra.ContainsKey("history"));
    }
}

public class NodeTests
{
    [Fact]
    public void Node_LinkAndNoun()
    {
        var coord = new Coord("limbo", 0, 0, 0);
        var node = new Node(coord, "Start", "A room");
        Assert.Equal(coord, node.Coord);
        Assert.True(node.IsNode);
        var link = new NodeLink("north", new Coord("limbo", 0, 1, 0));
        node.AddLink(link);
        Assert.Single(node.Links);
        Assert.Equal(link, node.GetLink("north"));
        Assert.Equal(link, node.GetLink("NORTH"));
        node.AddNoun("statue", "a stone statue");
        Assert.Equal("a stone statue", node.GetNoun("statue"));
        Assert.Equal("a stone statue", node.GetNoun("STATUE"));
        node.RemoveLink("north");
        Assert.Empty(node.Links);
    }

    [Fact]
    public void NodeGrid_AreaZ()
    {
        var area = new NodeArea("limbo");
        var grid = area.GetOrAddGrid(0);
        var n1 = new Node(new Coord("limbo", 0, 0, 0));
        var n2 = new Node(new Coord("limbo", 1, 0, 0));
        grid.AddNode(n1);
        grid.AddNode(n2);
        Assert.Equal(2, grid.Count);
        Assert.Equal(n1, grid.GetNode(0, 0));
        Assert.Equal(n2, grid.GetNode(1, 0));
        Assert.Null(grid.GetNode(9, 9));
        Assert.Single(area.Grids);
    }

    [Fact]
    public void Door_OpenClose()
    {
        var from = new Coord("limbo", 0, 0, 0);
        var to = new Coord("limbo", 1, 0, 0);
        var door = new Door(from, to, "wooden door");
        Assert.False(door.IsClosed);
        door.Close();
        Assert.True(door.IsClosed);
        door.Open();
        Assert.False(door.IsClosed);
        door.IsLocked = true;
        Assert.True(door.IsLocked);
    }
}
