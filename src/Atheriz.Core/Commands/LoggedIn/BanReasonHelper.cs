namespace Atheriz.Core.Commands.LoggedIn;

internal static class BanReasonHelper
{
    internal static void SetBanReason(GameObject c, string reason)
    {
        c.BanReason = reason;
    }
    internal static void ClearBanReason(GameObject c)
    {
        c.BanReason = "";
        c.TryRemoveExtraJson("ban_reason");
    }
}
