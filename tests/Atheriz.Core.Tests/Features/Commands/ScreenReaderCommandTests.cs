using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The Common ScreenReaderCommand and its UnloggedIn subclass share key,
// aliases, category, registry wiring, and toggle behavior.
[Collection("Ported")]
public sealed class ScreenReaderCommandTests
{
    [Fact]
    public void ScreenReader_BothNamespacesShareKeyAliasesAndToggle()
    {
        var common = new Atheriz.Core.Commands.Common.ScreenReaderCommand();
        var legacy = new ScreenReaderCommand();
        Assert.Equal("screenreader", common.Key);
        Assert.Equal(common.Key, legacy.Key);
        Assert.Equal(common.Aliases, legacy.Aliases);
        Assert.Contains("sr", legacy.Aliases);
        Assert.Equal(common.Category, legacy.Category);
        CommandRegistry.Reset();
        try
        {
            Assert.IsType<Atheriz.Core.Commands.Common.ScreenReaderCommand>(
                CommandRegistry.LoggedIn.Get("screenreader"));
            Assert.IsType<ScreenReaderCommand>(
                CommandRegistry.UnloggedIn.Get("screenreader"));
        }
        finally { CommandRegistry.Reset(); }
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        var sess = new Session { Connection = conn };
        var go = GameObject.Create("srtoggle", isPc: true);
        go.Session = sess;
        sess.Puppet = go;
        go.ClearMessages();
        legacy.Run(go, null);
        Assert.True(sess.ScreenReader);
        Assert.Contains(go.PeekMessages(), m => m.Contains("Screenreader mode on."));
    }
}
