using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Object identity is the registry id (Equals/== by Id), and the hash is a
// snapshot taken at construction / load-path re-key: a member whose public
// Id is reassigned inside a hash container keeps its bucket (findable),
// while same-Id duplicates arranged via SetIdRaw hash equal.
[Collection("Ported")]
public class ObjectIdentityTests
{
    [Fact]
    public void HashCode_FollowsRegistryId()
    {
        // Port of nodes.py:92 — hash(id), matching Equals-by-Id .
        // Same-Id instances (e.g. reload duplicates) hash equal.
        // Arranged via the load-path re-key (public Id reassignment after
        // hashing intentionally leaves the construction-time snapshot, so a
        // member mutated inside a hash container stays findable — see
        // CorrectnessBatchATests.HashSet_MembershipSurvivesIdReassignment).
        var o = new GameObject();
        o.SetIdRaw(4242);
        Assert.Equal(4242.GetHashCode(), o.GetHashCode());
    }

    [Fact]
    public void HashSet_FindsSameIdInstance()
    {
        // Equal (same-Id) instances share a hash bucket: reload duplicates
        // are findable via the hash lookup, not just linear scan.
        // NOTE: must use the hash-bucket lookup (TryGetValue), not
        // Assert.Contains (linear scan that never consults the hash).
        var a = GameObject.Create("hash-a");
        var b = GameObject.Create("hash-b");
        b.SetIdRaw(a.Id);
        var set = new HashSet<GameObject> { a };
        Assert.True(set.TryGetValue(b, out var found));
        Assert.Same(a, found);
    }
}
