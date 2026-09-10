using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// TrySet guard-set-return: every flag arm changes only on difference,
// accepts all three name spellings, and rejects unknown names.
[Collection("Ported")]
public class FlagsSetIfChangedTests
{
    private static readonly string[] AllFlags =
    [
        "is_pc", "is_npc", "is_item", "is_mapable", "is_container", "is_script",
        "is_tickable", "is_account", "is_channel", "is_node", "is_modified",
        "is_deleted", "is_connected", "is_temporary", "is_banned", "can_hear",
    ];

    [Fact]
    public void AllSixteenFlags_GuardSetReturn()
    {
        var f = new Flags();
        foreach (var name in AllFlags)
        {
            // Fresh flags default false except is_modified (defaults true).
            bool first = name != "is_modified";
            Assert.True(f.TrySet(name, first));
            Assert.False(f.TrySet(name, first));
            Assert.True(f.TrySet(name, !first));
        }
    }

    [Fact]
    public void AllThreeSpellings_HitSameField()
    {
        var f = new Flags();
        Assert.True(f.TrySet("is_pc", true));
        Assert.False(f.TrySet("IsPc", true));
        Assert.False(f.TrySet("_isPc", true));
        Assert.True(f.TrySet("IsPc", false));
        Assert.True(f.TrySet("_is_tickable", true));
        Assert.False(f.TrySet("is_tickable", true));
    }

    [Fact]
    public void UnknownName_ReturnsFalse()
    {
        Assert.False(new Flags().TrySet("is_wizard", true));
        Assert.False(new Flags().TrySet("", true));
    }
}
