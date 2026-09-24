using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from WhitespaceCollapseTests.cs
// Span whitespace collapse (no regex, no pattern cache): same inputs produce
// the same outputs as the old three-regex pipeline, and distinct
// (maxLinebreaks, maxSpacing) caps do not cross-talk.
public class WhitespaceCollapseTests
{
    [Fact]
    public void CompressWhitespace_RepeatedCalls_Agree()
    {
        const string input = "a    b\n\n\nc";
        var first = GameUtils.CompressWhitespace(input, maxLinebreaks: 1, maxSpacing: 2);
        var second = GameUtils.CompressWhitespace(input, maxLinebreaks: 1, maxSpacing: 2);
        Assert.Equal(first, second);
        Assert.Equal("a  b\nc", first);
    }

    [Fact]
    public void CompressWhitespace_DistinctCaps_DoNotCrossTalk()
    {
        Assert.Equal("a  b", GameUtils.CompressWhitespace("a    b", maxSpacing: 2));
        Assert.Equal("a b", GameUtils.CompressWhitespace("a    b", maxSpacing: 1));
        Assert.Equal("a\nb", GameUtils.CompressWhitespace("a\n\n\nb", maxLinebreaks: 1));
    }

    // Defaults preserve a single blank line, and larger caps reach the
    // parameterized pass: the old \s* pre-pass capped every run at 2 breaks
    // first (maxLinebreaks >= 3 was dead) and the old default erased blanks.
    [Fact]
    public void CompressWhitespace_Default_PreservesSingleBlankLine()
    {
        Assert.Equal("a\n\nb", GameUtils.CompressWhitespace("a\n\nb"));
    }

    [Fact]
    public void CompressWhitespace_MaxLinebreaks3_PreservesThreeBreaks()
    {
        Assert.Equal("a\n\n\nb", GameUtils.CompressWhitespace("a\n\n\n\n\nb", maxLinebreaks: 3));
    }

    [Fact]
    public void CompressWhitespace_Quirks_Preserved()
    {
        // Leading spaces stay (the old `(?<=\S)` gate never collapsed them).
        Assert.Equal("  hello  world", GameUtils.CompressWhitespace("  hello   world  ", maxSpacing: 2));
        // Spaces around a break stay: the pre-break run is too short and the
        // post-break run has no non-whitespace predecessor.
        Assert.Equal("a \n b", GameUtils.CompressWhitespace("a \n\n b", maxLinebreaks: 1));
        // A zero cap disables that collapse (the old `{0,}` pattern no-opped).
        Assert.Equal("a    b", GameUtils.CompressWhitespace("a    b", maxSpacing: 0));
        Assert.Equal("a\n\n\nb", GameUtils.CompressWhitespace("a\n\n\nb", maxLinebreaks: 0));
        // Blank gaps holding tabs collapse like blank gaps holding spaces.
        Assert.Equal("a\nb", GameUtils.CompressWhitespace("a\n \t\nb", maxLinebreaks: 1));
        Assert.Equal("", GameUtils.CompressWhitespace(null!));
    }

    [Fact]
    public void CompressWhitespace_HasNoPatternCache()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "GameUtils.cs");
        Assert.DoesNotContain("CompressPatternCache", src);
        Assert.DoesNotContain("GetCompressPatterns", src);
    }

    [Fact]
    public void GameUtils_HasNoRuntimeRegex()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "GameUtils.cs");
        Assert.DoesNotContain("new Regex(", src);
        Assert.Contains("[GeneratedRegex(", src);
    }

    [Fact]
    public void IterToString_SinglePass_MatchesJoinTruncation()
    {
        Assert.Equal("", GameUtils.IterToString(null));
        Assert.Equal("", GameUtils.IterToString(Array.Empty<object?>()));
        Assert.Equal("solo", GameUtils.IterToString(new object?[] { "solo" }));
        Assert.Equal("1 and 2", GameUtils.IterToString(new object?[] { 1, 2 }, sep: " and ", endsep: " and "));
        Assert.Equal("1, 2, and 3", GameUtils.IterToString(new object?[] { 1, 2, 3 }, sep: ",", endsep: ", and "));
        Assert.Equal("\"a\" and \"b\"", GameUtils.IterToString(new object?[] { "a", "b" }, sep: ",", endsep: ", and ", addQuote: true));
    }
}

// Merged from StringDistanceTwoRowTests.cs
// Two-row Levenshtein DP + MinBy best match: identical distances (including
// argument symmetry from the shorter-row swap) and first-minimum ties.
public class StringDistanceTwoRowTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("look", "look", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("look", "lock", 1)]
    [InlineData("", "abc", 3)]
    public void Levenshtein_MatchesFullTableDistances(string a, string b, int expected)
    {
        Assert.Equal(expected, StringDistance.Levenshtein(a, b));
        Assert.Equal(expected, StringDistance.Levenshtein(b, a));
    }

    [Fact]
    public void Levenshtein_OversizedInput_ComparesTruncatedPrefix()
    {
        // Over-length inputs compare on their capped prefixes: the old
        // content-blind constant returned Max(length) for every over-length
        // pair, collapsing them all to the same value.
        var big = new string('x', StringDistance.MaxInputLength + 1);
        Assert.Equal(StringDistance.MaxInputLength, StringDistance.Levenshtein(big, "y"));
    }

    [Fact]
    public void Levenshtein_OversizedInput_StillOrdersByContent()
    {
        // Equal-length over-cap inputs all collapsed to the same constant,
        // so ordering (and BestMatch) degraded to first-candidate-wins.
        var query = "abcdef" + new string('q', 1100);
        var near = "abcdef" + new string('q', 1100);
        var far = new string('f', query.Length);
        Assert.True(StringDistance.Levenshtein(query, near) < StringDistance.Levenshtein(query, far));
    }

    [Fact]
    public void BestMatch_OversizedInput_PicksClosestContent()
    {
        var query = "abcdef" + new string('q', 1100);
        var near = "abcdef" + new string('q', 1100);
        var far = new string('f', query.Length);
        Assert.Equal(near, StringDistance.BestMatch(query, new[] { far, near }));
    }

    [Fact]
    public void BestMatch_ReturnsFirstMinimum_OnTie()
    {
        Assert.Equal("ac", StringDistance.BestMatch("ab", new[] { "ac", "ad", "zzz" }));
    }

    [Fact]
    public void BestMatch_EmptyCandidates_ReturnsNull()
    {
        Assert.Null(StringDistance.BestMatch("lok", Array.Empty<string>()));
    }

    [Fact]
    public void Distance_NullInputs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StringDistance.Levenshtein(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => StringDistance.Levenshtein("a", null!));
        Assert.Throws<ArgumentNullException>(() => StringDistance.BestMatch(null!, new[] { "a" }));
        Assert.Throws<ArgumentNullException>(() => StringDistance.BestMatch("a", null!));
    }
}

// Merged from AnsiStyleFlagTests.cs
// Shared ANSI style-flag applier: byte-identical snapshots for all three
// Wrap* helpers, including flag order and the trailing reset.
public class AnsiStyleFlagTests
{
    [Fact]
    public void WrapXterm256_FlagOrder_BoldThenInverseThenStrikethruThenReset()
    {
        Assert.Equal("\x1b[9m\x1b[1mx\x1b[0m",
            GameUtils.WrapXterm256("x", bold: true, strikethru: true));
    }

    [Fact]
    public void WrapXterm256_AllFlags_BgPrependedLastThenFlagsThenReset()
    {
        // WrapXterm256 prepends fg first, then bg — so bg lands before fg in
        // the output (unlike WrapRgb, which prepends in the opposite order).
        // That caller-side color order predates the shared flag applier.
        Assert.Equal("\x1b[9m\x1b[7m\x1b[4m\x1b[3m\x1b[1m\x1b[48;5;2m\x1b[38;5;1mx\x1b[0m",
            GameUtils.WrapXterm256("x", fg: 1, bg: 2,
                bold: true, italic: true, underline: true, inverse: true, strikethru: true));
    }

    [Fact]
    public void WrapRgb_Defaults_Fg204BgBlackThenReset()
    {
        Assert.Equal("\x1b[38;2;204;204;204m\x1b[48;2;0;0;0mx\x1b[0m",
            GameUtils.WrapRgb("x"));
    }

    [Fact]
    public void WrapRgb_Flags_ShareApplierOrder()
    {
        Assert.Equal("\x1b[4m\x1b[1m\x1b[38;2;204;204;204m\x1b[48;2;0;0;0mx\x1b[0m",
            GameUtils.WrapRgb("x", bold: true, underline: true));
    }

    [Fact]
    public void WrapTruecolor_Defaults_WhiteOnBlackThenReset()
    {
        Assert.Equal("\x1b[38;2;255;255;255m\x1b[48;2;0;0;0mx\x1b[0m",
            GameUtils.WrapTruecolor("x"));
    }
}

// Merged from FsGuardCollapseTests.cs
// Collapsed chmod/guard/directory wrappers: shared cores keep identical
// messages, guards, and create+chmod+probe behavior.
public class FsGuardCollapseTests
{
    [Fact]
    public void GuardSecretPath_Absolute_Passes()
    {
        PathGuards.GuardSecretPath(Path.GetTempPath());
    }

    [Fact]
    public void GuardSecretPath_Relative_ThrowsMentioningSecretPath()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathGuards.GuardSecretPath("secret"));
        Assert.Contains("SECRET_PATH", ex.Message);
    }

    [Fact]
    public void EnsureSecretDirectory_WritableDir_CreatesAndPasses()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"), "secret");
        try
        {
            PathGuards.EnsureSecretDirectory(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { } }
    }

    [Fact]
    public void EnsureSavePathValid_CustomKind_PreservesKindInMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathGuards.EnsureSavePathValid("rel-save", "custom"));
        Assert.Contains("custom", ex.Message);
        Assert.Contains("SAVE_PATH", ex.Message);
    }

    [Fact]
    public void EnsureSavePathValid_DefaultKind_MatchesGuardSavePath()
    {
        string? guardMsg = null;
        string? legacyMsg = null;
        try { PathGuards.GuardSavePath("rel-save"); } catch (InvalidOperationException ex) { guardMsg = ex.Message; }
        try { PathGuards.EnsureSavePathValid("rel-save"); } catch (InvalidOperationException ex) { legacyMsg = ex.Message; }
        Assert.NotNull(guardMsg);
        Assert.Equal(guardMsg, legacyMsg);
    }
}

// Merged from CryptoRandomFillBytesTests.cs
// Shared FillBytes core: UrlSafeToken + HexToken keep identical lengths,
// alphabets, and non-positive guard behavior.
public class CryptoRandomFillBytesTests
{
    [Fact]
    public void UrlSafeToken_DefaultLength_MatchesPython32Bytes()
    {
        Assert.Equal(43, CryptoRandom.UrlSafeToken().Length);
    }

    [Fact]
    public void HexToken_DefaultLength_MatchesPython32Bytes()
    {
        Assert.Equal(64, CryptoRandom.HexToken().Length);
    }

    [Fact]
    public void Tokens_VaryPerCall_AndStayInAlphabet()
    {
        var a = CryptoRandom.UrlSafeToken();
        var b = CryptoRandom.UrlSafeToken();
        Assert.NotEqual(a, b);
        Assert.Matches(@"^[A-Za-z0-9\-_]+$", a);
        Assert.Matches(@"^[0-9a-f]+$", CryptoRandom.HexToken());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TokenHelpers_RejectNonPositiveByteCounts(int bytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.UrlSafeToken(bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.HexToken(bytes));
    }
}

// Merged from SafeFormatMapTests.cs
// Director-stance formatting: one shared compiled pattern; missing keys and
// null values pass through untouched.
[Collection("Ported")]
public class SafeFormatMapTests
{
    [Fact]
    public void Format_SubstitutesPresentKeys_AndKeepsMissing()
    {
        var map = new FuncParserHelpers.SafeFormatMap { ["a"] = 1, ["b"] = null };

        Assert.Equal("1 and {b} and {c}!", map.Format("{a} and {b} and {c}!"));
    }

    [Fact]
    public void Format_EmptyOrPlain_PassesThrough()
    {
        var map = new FuncParserHelpers.SafeFormatMap();

        Assert.Equal("", map.Format(""));
        Assert.Equal("no keys here", map.Format("no keys here"));
    }
}

// Merged from CoordSpanParseTests.cs
// Span-based Coord parse: same accepted input set and false-never-throw
// contract as the split-based parse (no coord-widening).
[Collection("Ported")]
public class CoordSpanParseTests
{
    private static Coord Parse(string s)
    {
        Assert.True(Coord.TryParse(s, out var c), s);
        return c;
    }

    [Fact]
    public void ParenInAreaName_UsesLastOpenParen()
    {
        Assert.Equal(new Coord("My (old) Area", 1, 2, 3), Parse("My (old) Area(1,2,3)"));
    }

    [Fact]
    public void SearchForm_FourParts()
    {
        Assert.Equal(new Coord("Area", 1, 2, 3), Parse("(Area,1,2,3)"));
        Assert.Equal(new Coord("limbo", 4, 4, 4), Parse("(limbo,4,4,4)"));
    }

    [Fact]
    public void WhitespaceForm_FourTokens()
    {
        Assert.Equal(new Coord("Area", 1, 2, 3), Parse("Area 1 2 3"));
        Assert.Equal(new Coord("limbo", 4, 4, 4), Parse("  limbo\t4  4\t4 "));
    }

    [Fact]
    public void SignedAndSpaced_Numerics_Accepted()
    {
        Assert.Equal(new Coord("a", -1, +2, 3), Parse("a(-1, +2,3)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("limbo")]
    [InlineData("Area(1,2)")]
    [InlineData("Area(1,2,3,4)")]
    [InlineData("(Area,1,2)")]
    [InlineData("(Area,1,2,3,4)")]
    [InlineData("Area(a,b,c)")]
    [InlineData("Area 1 2")]
    [InlineData("Area 1 2 3 4")]
    [InlineData("Area 1 two 3")]
    [InlineData("(,1,2,3)")]
    [InlineData("()")]
    [InlineData("(Area,1,2,3")]
    [InlineData("Area)1,2,3(")]
    public void Malformed_ReturnsFalse(string? s)
    {
        Assert.False(Coord.TryParse(s, out var c));
        Assert.Equal(string.Empty, c.Area);
    }
}

// Merged from UtilsBclShapeTests.cs
// Utils on BCL primitives: hand-rolled base64url/hex, per-call DP rows, the
// verbs output-copy shadow, and the one-method-per-chmod API go through
// framework calls instead.
public class UtilsBclShapeTests
{
    [Fact]
    public void CryptoRandom_UsesBclEncoders()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "CryptoRandom.cs");
        Assert.Contains("Base64Url.EncodeToString", src);
        Assert.Contains("Convert.ToHexStringLower", src);
        Assert.DoesNotContain(".Replace('+', '-')", src);
    }

    [Fact]
    public void CryptoRandom_BclOutput_MatchesContract()
    {
        // Same contract as CryptoRandomFillBytesTests pins: 32 bytes ->
        // 43 unpadded urlsafe chars / 64 lowercase hex chars.
        var url = CryptoRandom.UrlSafeToken();
        Assert.Equal(43, url.Length);
        Assert.Matches(@"^[A-Za-z0-9\-_]+$", url);
        Assert.DoesNotContain("=", url);
        var hex = CryptoRandom.HexToken();
        Assert.Equal(64, hex.Length);
        Assert.Matches(@"^[0-9a-f]+$", hex);
    }

    [Fact]
    public void StringDistance_RentsRowsInsteadOfAllocating()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "StringDistance.cs");
        Assert.Contains("ArrayPool<int>.Shared.Rent", src);
        Assert.DoesNotContain("new int[", src);
    }

    [Fact]
    public void StringDistance_PooledRows_MatchKnownDistances()
    {
        Assert.Equal(0, StringDistance.Levenshtein("", ""));
        Assert.Equal(3, StringDistance.Levenshtein("", "abc"));
        Assert.Equal(3, StringDistance.Levenshtein("kitten", "sitting"));
        Assert.Equal("sitting", StringDistance.BestMatch("sittin", new[] { "kitten", "sitting", "bitten" }));
    }

    [Fact]
    public void VerbsTable_ShipsEmbeddedOnly()
    {
        var csproj = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Core/Atheriz.Core.csproj");
        Assert.Contains("<EmbeddedResource Include=\"Objects/VerbConjugation/verbs.txt\" />", csproj);
        Assert.DoesNotContain("verbs.txt\" CopyToOutputDirectory", csproj);
    }

    [Fact]
    public void VerbsTable_LoadsFromEmbeddedResource()
    {
        // No output-dir copy anymore: the table must still resolve (embedded
        // wins in Conjugate either way). These irregulars are absent from the
        // synthetic fallback subset, so they prove the full embedded table.
        Assert.Equal("write", Atheriz.Core.Objects.VerbConjugation.Conjugate.VerbInfinitive("written"));
        Assert.Equal("break", Atheriz.Core.Objects.VerbConjugation.Conjugate.VerbInfinitive("broken"));
        Assert.Equal("is", Atheriz.Core.Objects.VerbConjugation.Conjugate.VerbConjugate("be", "3rd singular present"));
    }

    [Fact]
    public void FsUtil_TryChmod_AcceptsExplicitMode()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var ex = Record.Exception(() => FsUtil.TryChmod(tmp,
                UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.Null(ex);
            Assert.True(File.Exists(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}

// Merged from SmallCoreTypesTests.cs
// Small core types on framework shapes: prompt timeouts via WaitAsync,
// one-time version lookup, record path nodes with integer priorities,
// predicate-pushed registry lookups, frozen verb tables.
public class SmallCoreTypesTests
{
    [Fact]
    public void MenuPrompt_UsesWaitAsync()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "MenuPrompt.cs");
        Assert.Contains(".WaitAsync(timeout)", src);
        Assert.DoesNotContain("Task.WhenAny", src);
        Assert.DoesNotContain("Task.Delay", src);
    }

    [Fact]
    public void ConnectionScreen_LooksUpVersionOnce()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Equal(1, SourceScan.Count(src, "GetName().Version"));
        Assert.Contains("VersionString", src);
    }

    [Fact]
    public void ConnectionScreen_VersionMatchesAssembly()
    {
        var rendered = Atheriz.Core.ConnectionScreen.Render(null);
        // Pin the Core assembly version (the assembly that serves the screen),
        // not the test assembly's.
        var coreVersion = typeof(Atheriz.Core.ConnectionScreen).Assembly.GetName().Version?.ToString();
        Assert.NotNull(coreVersion);
        Assert.Contains(coreVersion, rendered);
        Assert.Equal(rendered, Atheriz.Core.ConnectionScreen.Render(null));
    }

    [Fact]
    public void PathNode_IsRecord_WithIntPriorityQueue()
    {
        var node = SourceScan.Read("src", "Atheriz.Core", "Utils", "PathNode.cs");
        Assert.Contains("sealed record PathNode(", node);
        Assert.Contains("public int F => G + H;", node);
        Assert.DoesNotContain("{ get; set; }", node);
        var pathfind = SourceScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.Contains("PriorityQueue<PathNode, int>", pathfind);
        Assert.DoesNotContain("PriorityQueue<PathNode, PathNode>", pathfind);
        Assert.DoesNotContain(".F =", pathfind);
    }

    [Fact]
    public void ServerEvents_KeepsSnapshotThenScanEarlyExit()
    {
        // Snapshot-then-scan with early exit stays: pushing the predicate
        // into FilterBy would materialize every match instead of stopping
        // at the first (CharCreateExistenceTests pins this shape).
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        Assert.Contains("ObjectRegistry.FilterBy(_ => true)", src);
        Assert.Contains("if (predicate(o)) return true;", src);
        Assert.Contains("if (predicate(o)) return o;", src);
    }

    [Fact]
    public void Conjugate_TablesAreFrozen()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "VerbConjugation", "Conjugate.cs");
        Assert.Contains("FrozenDictionary<string, int> VerbTensesKeys", src);
        Assert.Contains("FrozenDictionary<string, string[]> VerbTenses", src);
        Assert.Contains("FrozenDictionary<string, string> VerbLemmas", src);
        Assert.Contains("ToFrozenDictionary(", src);
    }
}
