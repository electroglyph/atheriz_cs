using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;


namespace Atheriz.Core.Tests.Features;

// Core helper parity: menu prompt aliases and parallel clock agreement.
[Collection("Ported")]
public class CoreParityTests
{
    [Fact]
    public void MenuHelper_Matches_MenuPrompt()
    {
        // the MenuHelper alias is removed — one spelling lives on
        // MenuPrompt (PromptWithTimeout + PromptWithTimeoutAsync).
        Assert.NotNull(typeof(MenuPrompt).GetMethod("PromptWithTimeoutAsync"));
        Assert.NotNull(typeof(MenuPrompt).GetMethod("PromptWithTimeout"));
    }

    [Fact]
    public void Clocks_Agree()
    {
        // Behavior pin: the parallel clocks (GameClock / ThrottleWindow)
        // must report the same time. (MapEdit.GetMonotonic is internal.)
        double a = GameClock.MonotonicSeconds();
        double b = ThrottleWindow.Now();
        Assert.True(Math.Abs(a - b) < 1.0, $"GameClock={a} ThrottleWindow={b}");
    }
}
