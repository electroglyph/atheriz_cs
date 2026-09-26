using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Globals;

// Finding 23: IsStillSaveable took the object's read lock inside the
// AllLock hold, the reverse of MoveTo (object write locks held across
// GetSingle/FindNodeByCoord, which take AllLock). It now checks
// membership under AllLock, reads flags under the object lock, and
// re-checks membership under AllLock, never holding both at once.
//
// Why a source scan pins the order rather than a two-thread repro: the
// method's first touch of the object is `obj.Id`, and the Id getter
// takes the object's read lock itself (GameObject.cs). So a thread
// holding the object's write lock parks any IsStillSaveable caller at
// the Id read — before AllLock is ever taken — in BOTH the buggy and
// the fixed shape. No thread schedule distinguishes them at runtime;
// the audit's own D1 proof used AddObjectUnique as a stand-in for this
// reason. The scan asserts the fixed shape directly: two disjoint
// AllLock holds with the object-lock take between them.
public sealed class RegistryObjectLockOrderTests
{
    [Fact]
    public void IsStillSaveable_NeverHoldsRegistryLockAcrossObjectLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = SourceScan.Region(src, "private static bool IsStillSaveable(");
        Assert.Equal(2, SourceScan.Count(region, "lock (AllLock)"));
        var firstHold = region.IndexOf("lock (AllLock)", StringComparison.Ordinal);
        var secondHold = region.IndexOf("lock (AllLock)", firstHold + 1, StringComparison.Ordinal);
        var between = region.Substring(firstHold, secondHold - firstHold);
        Assert.Contains("EnterReadLock", between, StringComparison.Ordinal);
        Assert.Contains("ExitReadLock", between, StringComparison.Ordinal);
    }

    [Fact]
    public void IsStillSaveable_ReturnsExpectedVerdicts()
    {
        using var env = GlobalTestEnv.Enter();
        var saveable = typeof(ObjectRegistry).GetMethod("IsStillSaveable", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(saveable);
        object? Call(GameObject o, bool forSave, bool force)
            => saveable!.Invoke(null, new object?[] { o, forSave, force });

        var live = GameObject.Create("gem", isItem: true);
        ObjectRegistry.AddObject(live);
        Assert.Equal(true, Call(live, forSave: false, force: false));
        // Fresh objects are modified until saved; clear the flag to pin
        // the save filter's skip branch.
        live.IsModified = false;
        Assert.Equal(false, Call(live, forSave: true, force: false));
        Assert.Equal(true, Call(live, forSave: true, force: true));

        var stranger = GameObject.Create("stranger", isItem: true);
        Assert.Equal(false, Call(stranger, forSave: false, force: false));

        ObjectRegistry.RemoveObject(live);
        Assert.Equal(false, Call(live, forSave: false, force: false));
    }
}
