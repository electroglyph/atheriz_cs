using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

// Non-string/non-list tags raise (objects.py:169 set(tag) TypeError).
[Collection("Ported")]
public class TagTypeTests
{
    [Fact]
    public void GetByTag_NonStringTag_Throws()
    {
        Assert.Throws<System.ArgumentException>(() => ObjectRegistry.GetByTag(123));
    }
}
