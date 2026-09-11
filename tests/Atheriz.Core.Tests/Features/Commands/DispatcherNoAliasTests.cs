// Dispatcher no-alias keys: the raw command key arrives already lowered, so
// the NoAliasCommands gate needs no second lowering pass.
using Atheriz.Core.Commands;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class DispatcherNoAliasTests
{
    [Fact]
    public void Dispatch_UppercaseNoAliasKey_BlockedLikeLowercase()
    {
        using var env = GlobalTestEnv.Enter();
        CommandRegistry.Reset();
        var _ = CommandRegistry.LoggedIn;
        CommandDispatcher.SetSettings(new AtherizSettings { AutoCommandAliasing = true });
        try
        {
            var lowerPuppet = new GameObject { Name = "HeroLow" };
            lowerPuppet.ClearMessages();
            Assert.Null(CommandDispatcher.DispatchLoggedIn(lowerPuppet, "n", immediate: true));
            Assert.Contains("You can't do that.", lowerPuppet.PeekMessages());

            var upperPuppet = new GameObject { Name = "HeroUp" };
            upperPuppet.ClearMessages();
            Assert.Null(CommandDispatcher.DispatchLoggedIn(upperPuppet, "N", immediate: true));
            Assert.Contains("You can't do that.", upperPuppet.PeekMessages());
        }
        finally
        {
            CommandDispatcher.SetSettings(new AtherizSettings());
            CommandRegistry.Reset();
        }
    }
}
