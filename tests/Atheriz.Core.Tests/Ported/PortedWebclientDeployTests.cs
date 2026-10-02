// Port of atheriz/tests/test_webclient_deploy.py:1 — rewritten against the
// real WebDeployer (the package target is gone; game semantics preserve the
// legacy webclient tree).
namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedWebclientDeployTests
{
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
            Atheriz.Server.Deployment.WebDeployer.CleanGeneratedOutput(tmp);
            Assert.False(File.Exists(Path.Combine(legacy, "index.html")));
            Assert.Equal("old", File.ReadAllText(legacyScript));
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }
}
