using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Object hash must not depend on the mutable Id: transient objects sit in
// hash sets (exclusion sets in Node/ForContents paths) while ids are
// assigned, and a shifting hash strands them in the wrong bucket
// (GameObject.cs:131 with :300-306).
[Collection("Ported")]
public class ObjectIdentityTests
{
    [Fact]
    public void HashCode_StableAcrossIdAssignment()
    {
        // Correct: assigning the registry id never changes the hash.
        var o = new GameObject(); // transient Id == -1
        int before = o.GetHashCode();
        o.Id = 4242;
        Assert.Equal(before, o.GetHashCode());
    }

    [Fact]
    public void HashSet_FindsObjectAfterIdAssignment()
    {
        // Correct: an object stays findable in a hash set across id assignment.
        // NOTE: must use the hash-bucket lookup (TryGetValue), not
        // Assert.Contains (linear scan that never consults the hash).
        var o = new GameObject();
        var set = new HashSet<GameObject> { o };
        o.Id = 4243;
        Assert.True(set.TryGetValue(o, out var found));
        Assert.Same(o, found);
    }
}
