using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Certificate fallback chain as an attempt loop: fatal crypto errors rethrow,
// missing key files fail fast, and an unloadable file fails loudly.
public class TlsCertLoaderChainTests
{
    [Fact]
    public void Load_MissingKeyFile_ThrowsFileNotFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cert = Path.Combine(dir, "cert.pem");
            File.WriteAllText(cert, "not a real certificate");
            var missing = Path.Combine(dir, "missing.key");
            Assert.Throws<FileNotFoundException>(() => TlsCertLoader.Load(cert, missing));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Load_GarbageFile_FailsLoudly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var garbage = Path.Combine(dir, "garbage.pem");
            File.WriteAllText(garbage, "not a real certificate");
            Assert.ThrowsAny<Exception>(() => TlsCertLoader.Load(garbage, null));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
