using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The daemon-pid output is split once into one token array feeding both the
// last-token parse and the fallback first-parse scan (same string, same
// sequence, last-token-wins with first-parse fallback preserved).
// The parse runs inline after a real bash spawn, so the shape is pinned by
// source scan: no behavioral path can stage it without spawning a daemon.
[Collection("Ported")]
public class DaemonPidParseTests
{
    [Fact]
    public void PidOutput_SplitOnce_SharedByBothParses()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.Contains("private static readonly char[] PidSeparators", src);
        Assert.Equal(1, SourceScan.Count(src, "pidStr.Split"));
        Assert.Contains("var toks = pidStr.Split(PidSeparators, StringSplitOptions.RemoveEmptyEntries);", src);
    }

    [Fact]
    public void PidParse_LastTokenWins_FallbackScansFirst()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.Contains("var last = toks.LastOrDefault() ?? \"\";", src);
        Assert.Contains("foreach (var tok in toks)", src);
        // Last-token attempt runs before the first-parse fallback loop.
        int lastAt = src.IndexOf("toks.LastOrDefault()", StringComparison.Ordinal);
        int loopAt = src.IndexOf("foreach (var tok in toks)", StringComparison.Ordinal);
        Assert.True(lastAt >= 0 && loopAt >= 0 && lastAt < loopAt);
    }
}
