using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// Checkpoint handler resolution is cached handler, then singleton getter;
// an explicit tick still journals and ends clean.
[Collection("Ported")]
public class AutosaveResolveTests
{
    private static string? InvokeResolve(string? cached, Func<string> getter)
    {
        var method = typeof(CheckpointWriter).GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        var generic = method.MakeGenericMethod(typeof(string));
        object?[] args = [cached, getter];
        return (string?)generic.Invoke(null, args);
    }

    [Fact]
    public void Resolve_Cached_WinsOverGetter()
    {
        bool getterCalled = false;
        var result = InvokeResolve("cached", () => { getterCalled = true; return "getter"; });
        Assert.Equal("cached", result);
        Assert.False(getterCalled);
    }

    [Fact]
    public void Resolve_NullCached_FallsBackToGetter()
    {
        var result = InvokeResolve(null, () => "getter");
        Assert.Equal("getter", result);
    }

    [Fact]
    public void AutosaveTick_ExplicitHandlers_CompletesAndMarksClean()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var settings = new AtherizSettings { TimeSystemEnabled = false };
            var mh = new MapHandler(autoLoad: false);
            var nh = new NodeHandler(autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gameTime: null);
            Assert.False(CheckpointJournal.IsDirty(env.TempPath));
        }
        finally
        {
            Autosave.Reset();
        }
    }
}
