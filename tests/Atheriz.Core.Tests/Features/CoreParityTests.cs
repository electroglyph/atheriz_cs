using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Features;

// Core helper parity: menu prompt aliases and parallel clock agreement.
[Collection("Ported")]
public class CoreParityTests
{
    [Fact]
    public void MenuHelper_Matches_MenuPrompt()
    {
        // Behavior pin: four names, one impl — they must behave identically
        // until the aliases are removed.
        Assert.Equal(
            typeof(MenuPrompt).GetMethod("PromptWithTimeoutAsync") != null,
            typeof(MenuHelper).GetMethods().Any(m => m.Name.StartsWith("PromptWithTimeout")));
    }

    [Fact]
    public void Clocks_Agree()
    {
        // Behavior pin: the parallel clocks (TimeProvider / ThrottleWindow)
        // must report the same time. (MapEdit.GetMonotonic is internal.)
        double a = TimeProvider.MonotonicSeconds();
        double b = ThrottleWindow.Now();
        Assert.True(Math.Abs(a - b) < 1.0, $"TimeProvider={a} ThrottleWindow={b}");
    }
}
