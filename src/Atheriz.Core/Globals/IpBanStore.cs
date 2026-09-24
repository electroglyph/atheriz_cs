namespace Atheriz.Core.Globals;

// IP ban + failed-login stores split out of ObjectRegistry: world state
// (AllObjects) and classification state (bans/logins) no longer share one
// home. Each dict carries its own lock via BoundedDictionary.
public static class IpBanStore
{
    private static readonly BoundedDictionary<string, double> TempBannedIps = new();
    private static readonly BoundedDictionary<string, int> FailedLoginAttempts = new();

    public static bool IsIpBanned(string host, double? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Expiry cleanup is remove-if-equal: a BanIp landing between the read
        // and the cleanup must not delete the fresh ban.
        if (!TempBannedIps.TryGetValue(host, out var exp)) return false;
        if (t < exp) return true;
        TempBannedIps.RemoveIfEqual(host, exp);
        return false;
    }
    public static void BanIp(string host, double? expires = null)
    {
        var exp = expires ?? double.PositiveInfinity;
        TempBannedIps.Set(host, exp);
    }
    public static void UnbanIp(string host) => TempBannedIps.Remove(host);

    public static BoundedDictionary<string, int> FailedLogins => FailedLoginAttempts;

    internal static void Clear()
    {
        TempBannedIps.Clear();
        FailedLoginAttempts.Clear();
    }
}
