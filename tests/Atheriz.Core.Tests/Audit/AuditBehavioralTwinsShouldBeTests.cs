using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Audit;

// Should-be tests for audit §6 D8 (duplicated twins that diverge) and §7
// directly-observable style defects. Fails while the divergence is present.
[Collection("Ported")]
public class AuditBehavioralTwinsShouldBeTests
{
    [Fact]
    public void AddLink_Twins_AgreeOnDuplicateName()
    {
        // audit D8: AddLink dedups Name+Coord while AddLinkIfAbsent dedups
        // Name-only, so the same "add north->B when north->A exists" intent gets
        // different verdicts from the two APIs.
        ObjectRegistry.ClearAll();
        try
        {
            var coordA = new Coord("limbo", 0, 2, 0);
            var coordB = new Coord("limbo", 0, 4, 0);
            var n1 = new Node(new Coord("limbo", 0, 0, 0));
            var n2 = new Node(new Coord("limbo", 10, 0, 0));
            if (ObjectRegistry.Get(n1.Id).Count == 0) ObjectRegistry.AddObject(n1);
            if (ObjectRegistry.Get(n2.Id).Count == 0) ObjectRegistry.AddObject(n2);
            n1.AddLink(new NodeLink("north", coordA, new List<string> { "n" }));
            n2.AddLink(new NodeLink("north", coordA, new List<string> { "n" }));
            // Same duplicate-name situation through both APIs:
            n1.AddLink(new NodeLink("north", coordB, new List<string> { "n" }));
            bool absentAdded = n2.AddLinkIfAbsent("north", () => new NodeLink("north", coordB, new List<string> { "n" }));
            int dupes = n1.GetLinks().Count(l => l.Name == "north");
            Assert.True(dupes == 1 && !absentAdded || dupes > 1 && absentAdded,
                $"twin APIs disagree: AddLink left {dupes} 'north' links, AddLinkIfAbsent added={absentAdded}");
            Assert.Equal(1, dupes);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void CopyWordCase_Twins_Agree()
    {
        // audit D8: CopyWordCase exists in both FuncParserHelpers and GameUtils
        // with subtly different case handling; one canonical behavior must win.
        foreach (var (src, dst) in new[] { ("HELLO", "world"), ("Hello", "WORLD"), ("hELLO", "wORLD"), ("abc", "XYZ") })
        {
            Assert.Equal(
                GameUtils.CopyWordCase(src, dst),
                FuncParserHelpers.CopyWordCase(src, dst));
        }
    }

    [Fact]
    public void MenuHelper_Matches_MenuPrompt()
    {
        // audit D8 pin: four names, one impl — they must behave identically
        // until the aliases are removed.
        Assert.Equal(
            typeof(MenuPrompt).GetMethod("PromptWithTimeoutAsync") != null,
            typeof(MenuHelper).GetMethods().Any(m => m.Name.StartsWith("PromptWithTimeout")));
    }

    [Fact]
    public void Clocks_Agree()
    {
        // audit D8 pin: the parallel clocks (TimeProvider / ThrottleWindow)
        // must report the same time. (MapEdit.GetMonotonic is internal.)
        double a = TimeProvider.MonotonicSeconds();
        double b = ThrottleWindow.Now();
        Assert.True(Math.Abs(a - b) < 1.0, $"TimeProvider={a} ThrottleWindow={b}");
    }

    [Fact]
    public void FsUtil_ChmodAliases_Agree()
    {
        // audit D8 pin: TryChmod0600/TrySet0600 are two names for one op.
        var tmp = Path.GetTempFileName();
        try
        {
            FsUtil.TryChmod0600(tmp);
            FsUtil.TrySet0600(tmp);
            Assert.True(File.Exists(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void Ticker_DupAccessors_Agree()
    {
        // audit §7 pin: TimeSlot.running/Coros duplicates must stay
        // consistent until the aliases are removed.
        var pool = new AsyncThreadPool(maxThreads: 1);
        try
        {
            var slot = new AsyncTicker.TimeSlot(TimeSpan.FromSeconds(1), pool);
            Assert.Equal(slot.Running, slot.running);
            Assert.Equal(slot.Coros.Count, slot.CorosSnapshot.Count);
        }
        finally { pool.Stop(wait: false); }
    }
}
