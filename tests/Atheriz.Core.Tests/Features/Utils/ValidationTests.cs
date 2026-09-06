using Atheriz.Core;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Features.Utils;

// Behavior specifications for validation and parsing helpers: each test asserts
// the behavior the code should have. Pure functions only — no engine globals touched.
public sealed class ValidationTests
{
    // --- CryptoRandom: unpredictable decimal, URL-safe, and hex token generation ---

    [Fact]
    public void CryptoRandom_UInt64String_IsDecimalUInt64()
    {
        var s = CryptoRandom.UInt64String();
        Assert.True(ulong.TryParse(s, out _), $"expected decimal UInt64, got '{s}'");
    }

    [Fact]
    public void CryptoRandom_UInt64String_IsUnpredictable()
    {
        Assert.NotEqual(CryptoRandom.UInt64String(), CryptoRandom.UInt64String());
    }

    [Fact]
    public void CryptoRandom_UrlSafeToken_DefaultIs43CharsUrlSafe()
    {
        var t = CryptoRandom.UrlSafeToken();
        Assert.Equal(43, t.Length);
        Assert.DoesNotContain("+", t);
        Assert.DoesNotContain("/", t);
        Assert.DoesNotContain("=", t);
    }

    [Fact]
    public void CryptoRandom_HexToken_DefaultIs64LowerHexChars()
    {
        var t = CryptoRandom.HexToken();
        Assert.Equal(64, t.Length);
        Assert.Matches("^[0-9a-f]{64}$", t);
    }

    [Fact]
    public void CryptoRandom_Tokens_AreUnpredictable()
    {
        Assert.NotEqual(CryptoRandom.UrlSafeToken(), CryptoRandom.UrlSafeToken());
        Assert.NotEqual(CryptoRandom.HexToken(), CryptoRandom.HexToken());
    }

    [Fact]
    public void CryptoRandom_NegativeBytes_ThrowsArgumentOutOfRange()
    {
        // Contract: public int-bytes API validates input with ArgumentOutOfRangeException,
        // not OverflowException/OutOfMemoryException from `new byte[bytes]`.
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.UrlSafeToken(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.HexToken(-1));
    }

    // --- StringDistance: edit distance and closest-match selection ---

    [Fact]
    public void StringDistance_Levenshtein_KnownDistances()
    {
        Assert.Equal(0, StringDistance.Levenshtein("", ""));
        Assert.Equal(0, StringDistance.Levenshtein("look", "look"));
        Assert.Equal(3, StringDistance.Levenshtein("kitten", "sitting"));
        Assert.Equal(1, StringDistance.Levenshtein("look", "lock"));
        Assert.Equal(3, StringDistance.Levenshtein("", "abc"));
    }

    [Fact]
    public void StringDistance_BestMatch_PicksClosestCandidate()
    {
        var best = StringDistance.BestMatch("lok", new[] { "look", "say", "emote" });
        Assert.Equal("look", best);
    }

    [Fact]
    public void StringDistance_BestMatch_EmptyCandidates_ReturnsNull()
    {
        Assert.Null(StringDistance.BestMatch("lok", Array.Empty<string>()));
    }

    [Fact]
    public void StringDistance_NullInput_ThrowsArgumentNull()
    {
        // Contract: non-nullable string params guard with ArgumentNullException, not NRE.
        Assert.Throws<ArgumentNullException>(() => StringDistance.Levenshtein(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => StringDistance.Levenshtein("a", null!));
        Assert.Throws<ArgumentNullException>(() => StringDistance.BestMatch(null!, new[] { "a" }));
        Assert.Throws<ArgumentNullException>(() => StringDistance.BestMatch("a", null!));
    }

    // --- Coord.TryParse: area and coordinate parsing across supported forms ---

    [Fact]
    public void Coord_TryParse_AreaParenForm()
    {
        Assert.True(Coord.TryParse("limbo(1,2,3)", out var c));
        Assert.Equal(new Coord("limbo", 1, 2, 3), c);
    }

    [Fact]
    public void Coord_TryParse_TupleForm()
    {
        Assert.True(Coord.TryParse("(limbo,1,2,3)", out var c));
        Assert.Equal(new Coord("limbo", 1, 2, 3), c);
    }

    [Fact]
    public void Coord_TryParse_SpaceForm()
    {
        Assert.True(Coord.TryParse("limbo 1 2 3", out var c));
        Assert.Equal(new Coord("limbo", 1, 2, 3), c);
    }

    [Fact]
    public void Coord_TryParse_BareArea_IsOrigin()
    {
        Assert.True(Coord.TryParse("limbo", out var c));
        Assert.Equal(new Coord("limbo", 0, 0, 0), c);
    }

    [Fact]
    public void Coord_TryParse_Failures_ReturnFalse()
    {
        Assert.False(Coord.TryParse(null, out _));
        Assert.False(Coord.TryParse("", out _));
        Assert.False(Coord.TryParse("   ", out _));
        Assert.False(Coord.TryParse("limbo(1,2)", out _));
        Assert.False(Coord.TryParse("limbo(a,b,c)", out _));
        Assert.False(Coord.TryParse("(limbo,1,2)", out _));
    }

    [Fact]
    public void Coord_ToString_RoundTripsThroughTryParse()
    {
        var orig = new Coord("grotto", -1, 20, 300);
        Assert.True(Coord.TryParse(orig.ToString(), out var back));
        Assert.Equal(orig, back);
    }

    // --- TimeProvider seam: replaceable clock for monotonic time ---

    private sealed class FakeClock : ITimeProvider
    {
        public double T;
        public double MonotonicSeconds() => T;
        public long MonotonicMilliseconds() => (long)(T * 1000.0);
        public double Now() => T;
    }

    [Fact]
    public void TimeProvider_DefaultSeam_CanBeReplacedAndRestored()
    {
        var orig = TimeProvider.Default;
        try
        {
            var fake = new FakeClock { T = 1234.5 };
            TimeProvider.Default = fake;
            Assert.Equal(1234.5, TimeProvider.Default.MonotonicSeconds());
            Assert.Equal(1234.5, TimeProvider.Default.Now());
        }
        finally
        {
            TimeProvider.Default = orig;
        }
        Assert.Same(orig, TimeProvider.Default);
    }

    [Fact]
    public void TimeProvider_StaticClock_IsMonotonicNonDecreasing()
    {
        var a = TimeProvider.MonotonicSeconds();
        var b = TimeProvider.MonotonicSeconds();
        Assert.True(b >= a);
        Assert.Equal(a, TimeProvider.Now(), precision: 3);
    }

    // --- Validators: single account and character-name ruleset (port of validation.py),
    // including settings overloads ---

    [Fact]
    public void Validators_SingleSourceOfTruth_AgreeOnShortName()
    {
        // "ab" is below the 3-char minimum under every spelling.
        var s = new AtherizSettings();
        Assert.Equal(
            Validation.ValidateAccountName("ab"),
            Validation.ValidateAccountName("ab", s));
        Assert.NotNull(Validation.ValidateAccountName("ab"));
    }

    [Fact]
    public void Validators_SingleSourceOfTruth_AgreeOnApostropheName()
    {
        // Apostrophes are legal (O'Brien); both overloads agree.
        var s = new AtherizSettings();
        Assert.Null(Validation.ValidateCharacterName("O'Brien"));
        Assert.Null(Validation.ValidateCharacterName("O'Brien", s));
    }

    [Fact]
    public void Validators_SingleSourceOfTruth_AgreeOnSpacedName()
    {
        // Single spaces are legal (a b); both overloads agree.
        var s = new AtherizSettings();
        Assert.Null(Validation.ValidateCharacterName("a b"));
        Assert.Null(Validation.ValidateCharacterName("a b", s));
    }

    [Fact]
    public void AccountValidation_NullInput_Parity()
    {
        // Python parity: validate_password(None) hits `if not password` and
        // returns the empty message, while validate_name(None) crashes on
        // None.strip() (AttributeError) — mirrored here as NullReference.
        Assert.Equal("Password cannot be empty.", Validation.ValidatePassword(null!));
        Assert.Throws<ArgumentNullException>(() => Validation.ValidateAccountName(null!));
    }

    // --- PathGuards: absolute-path guards plus legacy spellings ---

    [Fact]
    public void PathGuards_AbsoluteSavePath_PassesGuard()
    {
        var abs = Path.Combine(Path.GetTempPath(), $"atheriz_guard_{Guid.NewGuid():N}");
        PathGuards.GuardSavePath(abs);
        PathGuards.GuardSecretPath(abs);
    }

    [Fact]
    public void PathGuards_LegacyEnsureSavePathValid_MatchesGuardMessage()
    {
        // Contract: the legacy kind= overload is the same guard, not a divergent message.
        const string rel = "definitely_not_absolute_atheriz_test_dir_xyz";
        string? guardMsg = null, legacyMsg = null;
        try { PathGuards.GuardSavePath(rel); } catch (InvalidOperationException ex) { guardMsg = ex.Message; }
        try { PathGuards.EnsureSavePathValid(rel); } catch (InvalidOperationException ex) { legacyMsg = ex.Message; }
        Assert.Equal(guardMsg is null, legacyMsg is null);
        if (guardMsg is not null) Assert.Equal(guardMsg, legacyMsg);
    }

    [Fact]
    public void PathGuards_EnsureSecretPathValid_DelegatesToGuard()
    {
        const string rel = "definitely_not_absolute_atheriz_test_dir_xyz";
        string? guardMsg = null, aliasMsg = null;
        try { PathGuards.GuardSecretPath(rel); } catch (InvalidOperationException ex) { guardMsg = ex.Message; }
        try { PathGuards.EnsureSecretPathValid(rel); } catch (InvalidOperationException ex) { aliasMsg = ex.Message; }
        Assert.Equal(guardMsg is null, aliasMsg is null);
        if (guardMsg is not null) Assert.Equal(guardMsg, aliasMsg);
    }

    // --- TlsCertLoader: certificate and key loading failures ---

    [Fact]
    public void TlsCertLoader_MissingCertFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TlsCertLoader.Load(Path.Combine(Path.GetTempPath(), "no_such_atheriz_cert_xyz.pem"), null));
    }

    [Fact]
    public void TlsCertLoader_MissingKeyFile_ThrowsFileNotFound()
    {
        var cert = Path.GetTempFileName();
        try
        {
            Assert.Throws<FileNotFoundException>(() =>
                TlsCertLoader.Load(cert, Path.Combine(Path.GetTempPath(), "no_such_atheriz_key_xyz.pem")));
        }
        finally
        {
            File.Delete(cert);
        }
    }

    [Fact]
    public void TlsCertLoader_NullCertFile_ThrowsArgumentNull()
    {
        // Null cert path surfaces as ArgumentNullException from the file read.
        Assert.Throws<ArgumentNullException>(() => TlsCertLoader.Load(null!, null));
    }

    // --- AtherizSettingsValidator: startup settings validation ---

    [Fact]
    public void SettingsValidator_DefaultSettings_Passes()
    {
        var v = new AtherizSettingsValidator();
        // Absolute paths: relative defaults depend on CWD-is-game-folder, so pin
        // absolute paths here to avoid CWD dependence.
        var s = new AtherizSettings
        {
            SavePath = Path.Combine(Path.GetTempPath(), "atheriz_shouldbe_save"),
            SecretPath = Path.Combine(Path.GetTempPath(), "atheriz_shouldbe_secret"),
        };
        var result = v.Validate(null, s);
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void SettingsValidator_ZeroMaxCharacters_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = AbsolutePaths(new AtherizSettings { MaxCharacters = 0 });
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("MaxCharacters", result.FailureMessage);
    }

    [Fact]
    public void SettingsValidator_PrivilegedWebserverPort_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = AbsolutePaths(new AtherizSettings { WebserverPort = 80 });
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("WebserverPort", result.FailureMessage);
    }

    [Fact]
    public void SettingsValidator_InvertedPasswordBounds_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = AbsolutePaths(new AtherizSettings { MinPasswordLength = 10, MaxPasswordLength = 5 });
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("MaxPasswordLength", result.FailureMessage);
    }

    private static AtherizSettings AbsolutePaths(AtherizSettings s)
    {
        s.SavePath = Path.Combine(Path.GetTempPath(), "atheriz_shouldbe_save");
        s.SecretPath = Path.Combine(Path.GetTempPath(), "atheriz_shouldbe_secret");
        return s;
    }
}
