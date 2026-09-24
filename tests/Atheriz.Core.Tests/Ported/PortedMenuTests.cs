// Port of atheriz/tests/test_menu.py:1
using Atheriz.Core;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMenuTests
{
    private static Task<(string, List<Choice>)> NStart(MenuContext c) =>
        Task.FromResult<(string, List<Choice>)>(("Welcome! Choose an option.", new List<Choice> { new("1", "Go to confirm", NConfirm), new("2", "Stay here", NStart), new("Q", "Quit", null) }));
    private static Task<(string, List<Choice>)> NConfirm(MenuContext c)
    {
        c.State["confirmed"] = true;
        return Task.FromResult<(string, List<Choice>)>(("Are you sure?", new List<Choice> { new("Y", "Yes", NFinish), new("N", "No", NStart) }));
    }
    private static Task<(string, List<Choice>)> NFinish(MenuContext c) =>
        Task.FromResult<(string, List<Choice>)>(("Done!", new List<Choice> { new("X", "Exit", null) }));
    private static Task<(string, List<Choice>)> NCallback(MenuContext c)
    {
        Task Cb(MenuContext x) { x.State["selected"] = true; return Task.CompletedTask; }
        return Task.FromResult<(string, List<Choice>)>(("Pick one", new List<Choice> { new("1", "Select", null, Cb) }));
    }
    private static Task<(string, List<Choice>)> NStay(MenuContext c)
    {
        Task Cb(MenuContext x) { x.State["toggled"] = true; return Task.CompletedTask; }
        return Task.FromResult<(string, List<Choice>)>(("Toggle", new List<Choice> { new("1", "Toggle", null, Cb, true) }));
    }
    private static Task<(string, List<Choice>)> NEmpty(MenuContext c) =>
        Task.FromResult<(string, List<Choice>)>(("Dead end", new List<Choice>()));
    private static Task<(string, List<Choice>)> NHello(MenuContext c) =>
        Task.FromResult<(string, List<Choice>)>(("Hello", new List<Choice> { new("1", "One", null) }));

    private static async Task<MenuEngine> Rendered(object? caller, Func<MenuContext, Task<(string, List<Choice>)>> start)
    {
        var e = new MenuEngine(caller, start);
        await e.RenderAsync();
        return e;
    }

    [Fact] public void MenuContextDefaults() { using var env = GlobalTestEnv.Enter(); var ctx = new MenuContext("player"); Assert.Equal("player", ctx.Caller); Assert.Empty(ctx.State); }
    [Fact] public void ChoiceDefaults() { var c = new Choice("1", "Option"); Assert.Equal("1", c.Key); Assert.Null(c.Goto); Assert.Null(c.Callback); Assert.False(c.Stay); }
    [Fact] public async Task EngineInit() { var e = await Rendered("player", NStart); Assert.Equal(NStart, e.CurrentNode); Assert.Contains("Welcome!", e.CurrentText); }
    [Fact] public async Task EngineGetDisplay() { var e = await Rendered("player", NStart); var d = e.Display; Assert.Contains("Welcome!", d); Assert.Contains("[1]", d); }
    [Fact] public async Task EngineHandleInputTransitions() { var e = await Rendered("player", NStart); Assert.True(await e.HandleInputAsync("1")); Assert.Equal(NConfirm, e.CurrentNode); }
    [Fact] public async Task EngineHandleInputExits() { var e = await Rendered("player", NStart); Assert.False(await e.HandleInputAsync("q")); Assert.Null(e.CurrentNode); }
    [Fact] public async Task EngineHandleInputInvalidStays() { var e = await Rendered("player", NStart); Assert.True(await e.HandleInputAsync("z")); Assert.Equal(NStart, e.CurrentNode); }
    [Fact] public async Task EngineHandleInputCaseInsensitive() { var e = await Rendered("player", NStart); Assert.False(await e.HandleInputAsync("Q")); Assert.Null(e.CurrentNode); }
    [Fact] public async Task EngineCallbackExecuted() { var e = await Rendered("player", NCallback); await e.HandleInputAsync("1"); Assert.Equal(true, e.Context.State["selected"]); }
    [Fact] public async Task EngineStayExecutesCallbackAndStays() { var e = await Rendered("player", NStay); await e.HandleInputAsync("1"); Assert.True((bool)e.Context.State["toggled"]!); Assert.Equal(NStay, e.CurrentNode); }
    [Fact] public async Task EngineEmptyChoicesExits() { var e = await Rendered("player", NEmpty); Assert.False(await e.HandleInputAsync("anything")); Assert.Null(e.CurrentNode); }
    [Fact] public async Task EngineClose() { var e = await Rendered("player", NStart); await e.HandleInputAsync("1"); e.Close(); Assert.Null(e.CurrentNode); Assert.Empty(e.Context.State); }
    [Fact] public async Task MenuDisplayUsesCrlfForTelnet() { var e = await Rendered("player", NHello); var d = e.Display; Assert.Contains("\r\n", d); }

    // ---- missing ----
    [Fact] public void MenuContextWithState() { var ctx = new MenuContext("player"); ctx.State["key"] = "val"; Assert.Equal("val", ctx.State["key"]); var ctx2 = new MenuContext("player"); ctx2.State["key"] = "val"; Assert.Equal(new Dictionary<string, object?> { { "key", "val" } }, ctx2.State); }
    // faithful to test_menucontext_with_state:63
    [Fact] public void ChoiceWithGotoAndCallback() { Func<MenuContext, Task<(string, List<Choice>)>> gotoFunc = NStart; Func<MenuContext, Task> cb = ctx => Task.CompletedTask; var c = new Choice("Y", "Yes", gotoFunc, cb); Assert.Equal(gotoFunc, c.Goto); Assert.Equal(cb, c.Callback); }
    [Fact] public async Task EngineGetDisplayEmptyWhenClosed() { var e = await Rendered("player", NStart); e.Close(); Assert.Equal("", e.Display); var e2 = await Rendered("player", NStart); await e2.HandleInputAsync("q"); Assert.Equal("", e2.Display); }
    [Fact] public async Task EngineHandleInputStripsWhitespace() { var e = await Rendered("player", NStart); Assert.False(await e.HandleInputAsync("  q  ")); Assert.Null(e.CurrentNode); }
    [Fact] public async Task EngineBackwardNavigation() { var e = await Rendered("player", NStart); await e.HandleInputAsync("1"); Assert.Equal(NConfirm, e.CurrentNode); await e.HandleInputAsync("n"); Assert.Equal(NStart, e.CurrentNode); }
    [Fact] public async Task EngineDisplayUpdatesAfterTransition() { var e = await Rendered("player", NStart); Assert.Contains("Welcome!", e.Display); await e.HandleInputAsync("1"); Assert.Contains("Are you sure?", e.Display); }
    [Fact] public async Task EngineStatePersistsAcrossNodes() { var e = await Rendered("player", NStart); await e.HandleInputAsync("1"); Assert.Equal(true, e.Context.State["confirmed"]); await e.HandleInputAsync("n"); Assert.Equal(true, e.Context.State["confirmed"]); }

    [Fact]
    public async Task RunMenuFullFlow()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Atheriz.Core.Objects.Session();
        var renders = new List<string>();
        Task<(string, List<Choice>)> Track(MenuContext c)
        {
            renders.Add("track");
            return Task.FromResult<(string, List<Choice>)>(("T", new List<Choice>
            {
                new("A", "Again", Track),
                new("Q", "Quit", null),
            }));
        }
        var engine = new MenuEngine(session, Track);
        var run = engine.RunAsync(session, TimeSpan.FromSeconds(5));
        // Feed answers through the session prompt slot, waiting for each
        // fresh prompt (the slot is replaced per turn).
        object? last = null;
        foreach (var answer in new[] { "A", "A", "Q" })
        {
            var spin = 0;
            while ((session.InputFuture is null || ReferenceEquals(session.InputFuture, last)) && !run.IsCompleted && spin++ < 500) await Task.Delay(10);
            if (run.IsCompleted) break;
            Assert.NotNull(session.InputFuture);
            Assert.False(ReferenceEquals(session.InputFuture, last));
            last = session.InputFuture;
            session.InputFuture.TrySetResult(answer);
        }
        var completed = await Task.WhenAny(run, Task.Delay(5000));
        Assert.Same(run, completed);
        // Two "Again" turns re-rendered plus the initial render.
        Assert.Equal(new[] { "track", "track", "track" }, renders);
    }

    [Fact]
    public async Task RunMenuExitImmediately()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Atheriz.Core.Objects.Session();
        var engine = new MenuEngine(session, NStart);
        var run = engine.RunAsync(session, TimeSpan.FromSeconds(5));
        var spin = 0;
        while (session.InputFuture is null && spin++ < 500) await Task.Delay(10);
        Assert.NotNull(session.InputFuture);
        session.InputFuture.TrySetResult("Q");
        var completed = await Task.WhenAny(run, Task.Delay(5000));
        Assert.Same(run, completed);
    }

    [Fact]
    public async Task RunMenuMultiStep()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Atheriz.Core.Objects.Session();
        var engine = new MenuEngine(session, NStart);
        var run = engine.RunAsync(session, TimeSpan.FromSeconds(5));
        object? last = null;
        foreach (var answer in new[] { "1", "Y", "X" })
        {
            var spin = 0;
            while ((session.InputFuture is null || ReferenceEquals(session.InputFuture, last)) && !run.IsCompleted && spin++ < 500) await Task.Delay(10);
            if (run.IsCompleted) break;
            Assert.NotNull(session.InputFuture);
            Assert.False(ReferenceEquals(session.InputFuture, last));
            last = session.InputFuture;
            session.InputFuture.TrySetResult(answer);
            if (run.IsCompleted) break;
        }
        var completed = await Task.WhenAny(run, Task.Delay(5000));
        Assert.Same(run, completed);
    }
}
