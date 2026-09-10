using System.Reflection;
using System.Text.RegularExpressions;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for the P3 batch 11 (S-24 – S-29).
[Collection("Ported")]
public class P3BatchElevenTests
{
    // The refusal message has one owner: a single StaleVerdict format site
    // formats the shared constant, and both stale paths (pre-create check
    // and FileExists retry) route through it. Program.cs keeps no prose
    // copy for a grep-pin to pass on while the live literal lives elsewhere.
    [Fact]
    public void AlreadyRunning_MessageOwnedByPidFile()
    {
        var pid = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Contains("AlreadyRunningMessagePrefix", pid);
        Assert.Equal(1, SourceScan.Count(pid, "AlreadyRunningMessagePrefix}"));
        Assert.Equal(2, SourceScan.Count(pid, "= StaleVerdict();"));
        var prog = SourceScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.DoesNotContain("already running", prog);
    }

    // The template remarks describe the scaffold output only — no
    // references to out-of-project folders.
    [Fact]
    public void GameSettings_RemarksDescribeScaffoldOnly()
    {
        var src = SourceScan.Read("src", "Atheriz.GameTemplate", "GameSettings.cs");
        Assert.DoesNotContain("test/", src);
        Assert.DoesNotContain("samples", src);
    }

    // Every accepted CLI flag is documented in its usage line.
    [Fact]
    public void CliUsage_DocumentsAcceptedFlags()
    {
        var newSrc = SourceScan.Read("src", "Atheriz.Server", "Cli", "NewHandler.cs");
        Assert.Contains("[--overwrite|--force]", newSrc);
        var prog = SourceScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.Contains("[--overwrite|--force]", prog);
        Assert.Contains("--yes", prog);
    }

    // AtherizSettings.Default is shared-mutable with zero mutating
    // borrowers: fail if any source file ever assigns through it.
    [Fact]
    public void SettingsDefault_NeverMutated()
    {
        var root = Path.Combine(SourceScan.RepoRoot(), "src");
        var rx = new Regex(@"\.Default\.[A-Za-z_][A-Za-z0-9_]*\s*=(?![=>])", RegexOptions.Compiled);
        var hits = new List<string>();
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(f); } catch { continue; }
            if (rx.IsMatch(text)) hits.Add(f);
        }
        Assert.True(hits.Count == 0, "Default mutated in: " + string.Join(", ", hits));
    }

    // Test args travel via ArgumentList (no quote-breakout shaping), and the
    // foreground wait policy holds: --help runs to completion, exit 0.
    [Fact]
    public void TestHandler_UsesArgumentList()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "TestHandler.cs");
        Assert.Contains("ArgumentList.Add", src);
        Assert.DoesNotContain("string.Join(\" \", a.Select", src);
        Assert.Equal(0, TestHandler.HandleTest(new[] { "--help" }));
    }

    private void NastyDefaults(string a = "q\"b\\c\nd\te\rf\0end", char ch = '\'', string plain = "ok") { }

    // String/char defaults with quotes, backslashes and newlines emit
    // escaped (compilable) literals in generated Custom*.cs.
    [Fact]
    public void TemplateGenerator_EscapesStringDefaults()
    {
        var m = typeof(P3BatchElevenTests).GetMethod("NastyDefaults", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var gen = typeof(GameTemplateGenerator).GetMethod("BuildParamList", BindingFlags.NonPublic | BindingFlags.Static)!;
        var decl = (string)gen.Invoke(null, new object[] { m })!;
        Assert.Contains("\\\"", decl);
        Assert.Contains("\\\\", decl);
        Assert.Contains("\\n", decl);
        Assert.Contains("\\t", decl);
        Assert.Contains("\\r", decl);
        Assert.Contains("'\\''", decl);
        Assert.Contains("= \"ok\"", decl);
    }
}
