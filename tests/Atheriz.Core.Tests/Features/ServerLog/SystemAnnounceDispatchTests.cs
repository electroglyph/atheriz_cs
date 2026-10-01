using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.ServerLog;

// System announces must take the Channel-typed send: C# overloads bind
// statically, so a GameObject-typed Msg would hit GameObject.Msg
// (log-to-self) instead of Channel.Msg (history + subscriber delivery).
// Behavioral coverage lives in PuppetChannelTests (login) and
// PortedAutosaveTests (autosave); these pins hold the call shapes that
// cannot fail deterministically at runtime.
[Collection("Ported")]
public class SystemAnnounceDispatchTests
{
    [Fact]
    public void ShutdownReloadAnnounces_UseChannelTypedSend()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        Assert.Contains("is Atheriz.Core.Objects.Channel channel", src);
        Assert.DoesNotContain("channel.Msg(message)", src);
    }

    [Fact]
    public void ReloadCommand_UsesChannelTypedSend()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "AdminCommands.cs");
        Assert.DoesNotContain("channel?.Msg(", src);
        Assert.DoesNotContain("channel.Msg(result)", src);
        Assert.Contains("ch.Send(", src);
    }

    [Fact]
    public void Autosave_UsesChannelTypedSend()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "Autosave.cs");
        Assert.DoesNotContain("ch.Msg(", src);
        Assert.Contains("ch.Send(", src);
    }

    [Fact]
    public void LoginAnnounce_UsesChannelTypedSend()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "GameObject.Puppet.cs");
        Assert.Contains("serverChannel.Send(", src);
        Assert.DoesNotContain("serverChannel.Msg(", src);
    }
}
