// Port of atheriz/tests/test_webclient_deploy.py:1
namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedWebclientDeployTests
{
    private static void RemovePath(string p)
    {
        try { if (Directory.Exists(p)) Directory.Delete(p, true); else if (File.Exists(p)) File.Delete(p); } catch {}
    }
    private static void CleanGeneratedOutput(string staticRoot, bool removeLegacyWebclient = false)
    {
        foreach (var rel in new[] { "assets", "atheriz_draw", "chafa.wasm", "gfonts" })
            RemovePath(Path.Combine(staticRoot, rel));
        if (removeLegacyWebclient)
            RemovePath(Path.Combine(staticRoot, "webclient"));
        else
            RemovePath(Path.Combine(staticRoot, "webclient", "index.html"));
    }

    [Fact]
    public void PackageCleanupRemovesLegacyWebclientAssets()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wdeploy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var legacy = Path.Combine(tmp, "webclient");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "index.html"), "old");
            Directory.CreateDirectory(Path.Combine(legacy, "js"));
            File.WriteAllText(Path.Combine(legacy, "js", "webclient.js"), "old");
            CleanGeneratedOutput(tmp, removeLegacyWebclient: true);
            Assert.False(Directory.Exists(legacy));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void GameCleanupPreservesLegacyWebclientAssets()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wdeploy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var legacy = Path.Combine(tmp, "webclient");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "index.html"), "old");
            Directory.CreateDirectory(Path.Combine(legacy, "js"));
            var legacyScript = Path.Combine(legacy, "js", "webclient.js");
            File.WriteAllText(legacyScript, "old");
            CleanGeneratedOutput(tmp, removeLegacyWebclient: false);
            Assert.False(File.Exists(Path.Combine(legacy, "index.html")));
            Assert.Equal("old", File.ReadAllText(legacyScript));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }
}
