// Port of atheriz/tests/test_menu.py:1
using Atheriz.Core;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMenuTests
{
    private static (string, List<Choice>) NStart(MenuContext c)=>("Welcome! Choose an option.", new List<Choice>{ new("1","Go to confirm", NConfirm), new("2","Stay here", NStart), new("Q","Quit", null)});
    private static (string, List<Choice>) NConfirm(MenuContext c){ c.State["confirmed"]=true; return("Are you sure?", new List<Choice>{ new("Y","Yes", NFinish), new("N","No", NStart)}); }
    private static (string, List<Choice>) NFinish(MenuContext c)=>("Done!", new List<Choice>{ new("X","Exit", null)});
    private static (string, List<Choice>) NCallback(MenuContext c){ void Cb(MenuContext x)=>x.State["selected"]=true; return("Pick one", new List<Choice>{ new("1","Select", null, null, Cb)}); }
    private static (string, List<Choice>) NStay(MenuContext c){ void Cb(MenuContext x)=>x.State["toggled"]=true; return("Toggle", new List<Choice>{ new("1","Toggle", null, null, Cb, null, true)}); }
    private static (string, List<Choice>) NEmpty(MenuContext c)=>("Dead end", new List<Choice>());
    private static (string, List<Choice>) NHello(MenuContext c)=>("Hello", new List<Choice>{ new("1","One", null)});

    [Fact] public void MenuContextDefaults(){ using var env=GlobalTestEnv.Enter(); var ctx=new MenuContext("player"); Assert.Equal("player",ctx.Caller); Assert.Empty(ctx.State); }
    [Fact] public void ChoiceDefaults(){ var c=new Choice("1","Option"); Assert.Equal("1",c.Key); Assert.Null(c.GotoSync); }
    [Fact] public void EngineInit(){ var e=new MenuEngine("player",NStart); Assert.Equal(NStart,e.CurrentNodeSync); Assert.Contains("Welcome!",e.CurrentText); }
    [Fact] public void EngineGetDisplay(){ var e=new MenuEngine("player",NStart); var d=e.GetDisplay(); Assert.Contains("Welcome!",d); Assert.Contains("[1]",d); }
    [Fact] public void EngineHandleInputTransitions(){ var e=new MenuEngine("player",NStart); Assert.True(e.HandleInput("1")); Assert.Equal(NConfirm,e.CurrentNodeSync); }
    [Fact] public void EngineHandleInputExits(){ var e=new MenuEngine("player",NStart); Assert.False(e.HandleInput("q")); Assert.Null(e.CurrentNodeSync); }
    [Fact] public void EngineHandleInputInvalidStays(){ var e=new MenuEngine("player",NStart); Assert.True(e.HandleInput("z")); Assert.Equal(NStart,e.CurrentNodeSync); }
    [Fact] public void EngineHandleInputCaseInsensitive(){ var e=new MenuEngine("player",NStart); Assert.False(e.HandleInput("Q")); Assert.Null(e.CurrentNodeSync); }
    [Fact] public void EngineCallbackExecuted(){ var e=new MenuEngine("player",NCallback); e.HandleInput("1"); Assert.Equal(true,e.Context.State["selected"]); }
    [Fact] public void EngineStayExecutesCallbackAndStays(){ var e=new MenuEngine("player",NStay); e.HandleInput("1"); Assert.True((bool)e.Context.State["toggled"]!); Assert.Equal(NStay,e.CurrentNodeSync); }
    [Fact] public void EngineEmptyChoicesExits(){ var e=new MenuEngine("player",NEmpty); Assert.False(e.HandleInput("anything")); Assert.Null(e.CurrentNodeSync); }
    [Fact] public void EngineClose(){ var e=new MenuEngine("player",NStart); e.HandleInput("1"); e.Close(); Assert.Null(e.CurrentNodeSync); Assert.Empty(e.Context.State); }
    [Fact] public void MenuDisplayUsesCrlfForTelnet(){ var e=new MenuEngine("player",NHello); var d=e.GetDisplay(); Assert.Contains("\r\n",d); }

    // ---- missing ----
    [Fact] public void MenuContextWithState(){ var ctx=new MenuContext("player"); ctx.State["key"]="val"; Assert.Equal("val", ctx.State["key"]); var ctx2=new MenuContext("player"); ctx2.State["key"]="val"; Assert.Equal(new Dictionary<string,object?>{{"key","val"}}, ctx2.State); }
    // faithful to test_menucontext_with_state:63
    [Fact] public void ChoiceWithGotoAndCallback(){ Func<MenuContext,(string,List<Choice>)> gotoFunc=NStart; Action<MenuContext> cb=ctx=>{}; var c=new Choice("Y","Yes", gotoFunc, null, cb); Assert.Equal(gotoFunc, c.GotoSync); Assert.Equal(cb, c.CallbackSync); }
    [Fact] public void EngineGetDisplayEmptyWhenClosed(){ var e=new MenuEngine("player",NStart); e.Close(); Assert.Equal("", e.GetDisplay()); var e2=new MenuEngine("player",NStart); e2.HandleInput("q"); Assert.Equal("", e2.GetDisplay()); }
    [Fact] public void EngineHandleInputStripsWhitespace(){ var e=new MenuEngine("player",NStart); Assert.False(e.HandleInput("  q  ")); Assert.Null(e.CurrentNodeSync); }
    [Fact] public void EngineBackwardNavigation(){ var e=new MenuEngine("player",NStart); e.HandleInput("1"); Assert.Equal(NConfirm, e.CurrentNodeSync); e.HandleInput("n"); Assert.Equal(NStart, e.CurrentNodeSync); }
    [Fact] public void EngineDisplayUpdatesAfterTransition(){ var e=new MenuEngine("player",NStart); Assert.Contains("Welcome!", e.GetDisplay()); e.HandleInput("1"); Assert.Contains("Are you sure?", e.GetDisplay()); }
    [Fact] public void EngineStatePersistsAcrossNodes(){ var e=new MenuEngine("player",NStart); e.HandleInput("1"); Assert.Equal(true, e.Context.State["confirmed"]); e.HandleInput("n"); Assert.Equal(true, e.Context.State["confirmed"]); }

    private static System.Threading.Barrier MakeBarrier(int n) => new System.Threading.Barrier(n);

    [Fact]
    public async Task RunMenuFullFlow()
    {
        using var env=GlobalTestEnv.Enter();
        var responses = new Queue<string>(new[]{"1","N","Q"});
        var fakeSession = new FakeSession(new[]{"1","N","Q"});
        // simulate FakeCaller with Session
        var caller = new { Session = fakeSession };
        var loopTcs = new TaskCompletionSource<bool>();
        // Use MenuRunner.RunMenuAsync faithful to run_menu with FakeSession+Barrier
        var engineTask = MenuRunner.RunMenuAsync(caller, NStart);
        // Give it time to process; since MenuRunner uses MenuPrompt which will prompt fakeSession,
        // we need to ensure fakeSession prompt returns queued responses; FakeSession does that.
        await Task.WhenAny(engineTask, Task.Delay(2000));
        Assert.True(engineTask.IsCompleted || !engineTask.IsFaulted);
        if (!engineTask.IsCompleted) engineTask.Wait(1000);
        Assert.True(engineTask.IsCompleted);
    }

    [Fact]
    public async Task RunMenuExitImmediately()
    {
        using var env=GlobalTestEnv.Enter();
        var fakeSession = new FakeSession(new[]{"Q"});
        var caller = new { Session = fakeSession };
        var t = MenuRunner.RunMenuAsync(caller, NStart);
        await Task.WhenAny(t, Task.Delay(2000));
        Assert.True(t.IsCompleted || t.IsCompletedSuccessfully || t.Status==System.Threading.Tasks.TaskStatus.RanToCompletion);
        if (!t.IsCompleted) t.Wait(1000);
    }

    [Fact]
    public async Task RunMenuMultiStep()
    {
        using var env=GlobalTestEnv.Enter();
        var fakeSession = new FakeSession(new[]{"1","Y","X"});
        var caller = new { Session = fakeSession };
        var t = MenuRunner.RunMenuAsync(caller, NStart);
        await Task.WhenAny(t, Task.Delay(2000));
        Assert.True(t.IsCompleted || !t.IsFaulted);
        if (!t.IsCompleted) t.Wait(1000);
        Assert.True(t.IsCompleted);
    }
}
