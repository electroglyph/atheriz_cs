// Port of Python secrets: secrets.randbits(64), secrets.token_urlsafe(32), secrets.token_hex(32)
// Used at atheriz/globals/salt.py:47, atheriz/globals/mapedit.py:66, atheriz/atheriz.py:557
using System.Security.Cryptography;

namespace Atheriz.Core.Utils;

/// <summary>
/// Port of Python <c>secrets</c> helpers used in Atheriz.
/// Mirrors <c>secrets.randbits(64)</c> (salt), <c>secrets.token_urlsafe(32)</c> (mapedit),
/// and <c>secrets.token_hex(32)</c> (admin token) via <c>RandomNumberGenerator</c>.
/// </summary>
public static class CryptoRandom
{
    /// <summary>
    /// Port of <c>secrets.randbits(64)</c> at <c>atheriz/globals/salt.py:47</c>.
    /// Returns decimal string of a random UInt64 (8 random bytes).
    /// </summary>
    public static string UInt64String()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes).ToString();
    }

    /// <summary>
    /// Port of <c>secrets.token_urlsafe(32)</c> at <c>atheriz/globals/mapedit.py:66</c>.
    /// Generates <paramref name="bytes"/> random bytes and returns urlsafe base64 without padding.
    /// Default 32 bytes → 43 chars (like Python).
    /// </summary>
    public static string UrlSafeToken(int bytes = 32)
    {
        var arr = new byte[bytes];
        RandomNumberGenerator.Fill(arr);
        string b64 = Convert.ToBase64String(arr);
        return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// Port of <c>secrets.token_hex(32)</c> at <c>atheriz/atheriz.py:557</c>.
    /// Generates <paramref name="bytes"/> random bytes and returns lowercase hex (2*bytes chars).
    /// </summary>
    public static string HexToken(int bytes = 32)
    {
        var arr = new byte[bytes];
        RandomNumberGenerator.Fill(arr);
        return Convert.ToHexString(arr).ToLowerInvariant();
    }
}
