// Pins for the typed puppet snapshot (GameObject.PuppetRestoreSnapshot): the
// DTO converter reads the same transient snapshot through the record, and
// restore applies the record fields exactly.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;

namespace Atheriz.Core.Tests.Features.Objects;

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
    public void BuildDto_WhilePuppeted_ReflectsOriginalSnapshot()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("builder", isPc: true, privilege: Privilege.Builder);
        var sess = new Session(new TestConnection());
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
    public void BuildDto_PuppetRestore_ReadsSnapshotRecord()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("goblin", isPc: true, privilege: Privilege.Admin);
        target.SetPuppetRestore(new GameObject.PuppetRestoreSnapshot(false, Privilege.Guest));

        var dto = GameObjectDtoConverter.BuildDto(target);

        Assert.False(dto.IsPc);
        Assert.Equal(Privilege.Guest, dto.PrivilegeLevel);
    }

    [Fact]
    public void RestorePuppetSnapshot_Record_RestoresFields()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("goblin", isPc: true, privilege: Privilege.Admin);
        target.RestorePuppetSnapshot(new GameObject.PuppetRestoreSnapshot(false, Privilege.Guest));

        Assert.False(target.IsPc);
        Assert.Equal(Privilege.Guest, target.PrivilegeLevel);
    }

    [Fact]
    public void GetPuppetRestore_ReturnsInstalledRecord()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("goblin", isPc: true, privilege: Privilege.Admin);
        Assert.Null(target.GetPuppetRestore());

        var snapshot = new GameObject.PuppetRestoreSnapshot(true, Privilege.Builder);
        target.SetPuppetRestore(snapshot);

        Assert.Equal(snapshot, target.GetPuppetRestore());
    }
}
