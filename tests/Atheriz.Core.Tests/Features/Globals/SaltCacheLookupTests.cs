using Atheriz.Core.Globals;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared cache probe preserves both GetSalt lookups: default-slot and
// per-path hits return the cached salt without touching disk, and the
// fail-closed storage paths are untouched.
[Collection("Ported")]
public class SaltCacheLookupTests
{
    [Fact]
    public void GetSalt_DefaultSlot_HitsCacheWithoutDisk()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            SaltProvider.SetSalt("cacheprobe");
            Assert.Equal("cacheprobe", SaltProvider.GetSalt());
            Assert.Equal("cacheprobe", SaltProvider.GetSalt());
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public void GetSalt_ExplicitPath_HitsCacheAfterFileDeleted()
    {
        using var env = GlobalTestEnv.Enter();
        string dir = Path.Combine(env.TempPath, "saltcache");
        Directory.CreateDirectory(dir);
        try
        {
            string first = SaltProvider.GetSalt(dir);
            Assert.False(string.IsNullOrWhiteSpace(first));
            // The cached value survives the backing file: a regeneration
            // would return a different salt.
            File.Delete(Path.Combine(dir, "salt.txt"));
            Assert.Equal(first, SaltProvider.GetSalt(dir));
            Assert.Equal(first, SaltProvider.GetSalt(dir));
        }
        finally
        {
            SaltProvider.Clear();
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }
}
