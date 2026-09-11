// Pins for the widened puppet-snapshot consts (GameObject.Puppet.cs): the DTO
// converter reads the same transient snapshot through the shared keys, in
// both enum and int privilege forms, and the key values stay byte-identical.
using System.Reflection;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class PuppetSnapshotKeyTests
{
    private static Atheriz.Core.Commands.GameArgumentParser.ParsedArgs PArgs(string target)
    {
        var pa = new Atheriz.Core.Commands.GameArgumentParser.ParsedArgs();
        pa["target"] = target;
        return pa;
    }

    [Fact]
    public void PuppetRestoreKeys_MatchPersistedLiterals()
    {
        var isPc = typeof(GameObject).GetField("PuppetRestoreIsPcKey", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        var priv = typeof(GameObject).GetField("PuppetRestorePrivilegeKey", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(isPc);
        Assert.NotNull(priv);
        Assert.Equal("is_pc", isPc!.GetValue(null));
        Assert.Equal("privilege_level", priv!.GetValue(null));
    }

    [Fact]
    public void BuildDto_WhilePuppeted_ReflectsOriginalSnapshot()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("builder", isPc: true, privilege: Privilege.Builder);
        var sess = new Session(new FakeConnection());
        caller.Session = sess;
        sess.Puppet = caller;
        ObjectRegistry.AddObject(caller);
        var target = GameObject.Create("goblin", isNpc: true, privilege: Privilege.Guest);
        ObjectRegistry.AddObject(target);
        new PuppetCommand().Run(caller, PArgs($"#{target.Id}"));
        Assert.True(target.IsPc);

        var dto = GameObjectDtoConverter.BuildDto(target);

        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
    }

    [Fact]
    public void BuildDto_PuppetRestoreIntForm_ReadsSnapshot()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("goblin", isPc: true, privilege: Privilege.Admin);
        target.SetPuppetRestore(new Dictionary<string, object>
        {
            ["is_pc"] = false,
            ["privilege_level"] = (int)Privilege.Guest,
        });

        var dto = GameObjectDtoConverter.BuildDto(target);

        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
    }

    [Fact]
    public void RestorePuppetSnapshot_EnumPrivilege_RestoresLevel()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("goblin", isPc: true, privilege: Privilege.Admin);
        target.RestorePuppetSnapshot(new Dictionary<string, object>
        {
            ["is_pc"] = false,
            ["privilege_level"] = Privilege.Guest,
        });

        Assert.False(target.IsPc);
        Assert.Equal(Privilege.Guest, target.PrivilegeLevel);
    }

    [Fact]
    public void RestorePuppetSnapshot_IntPrivilege_RestoresLevel()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("goblin", isPc: true, privilege: Privilege.Admin);
        target.RestorePuppetSnapshot(new Dictionary<string, object>
        {
            ["is_pc"] = false,
            ["privilege_level"] = (int)Privilege.Guest,
        });

        Assert.False(target.IsPc);
        Assert.Equal(Privilege.Guest, target.PrivilegeLevel);
    }
}
